using Sill.Windows.Protocol;
using Sill.Windows.Video;

namespace Sill.Windows.Coherence;

/// <summary>Where the controller's messages go: the session's connection (thread-safe).</summary>
public interface ICoherenceLink
{
    bool Send(Kind kind, byte[] payload);
}

/// <summary>Where one window's stream goes: its VideoPipeline (thread-safe, queues only).</summary>
public interface IWindowVideo
{
    void Submit(ParameterSets sets);
    void Submit(byte[] frame, bool key, double timestamp);
}

/// <summary>What a kind 8 event is, for aiming it at a window.</summary>
public enum InputKind { Move, Scroll, Button, Key, Text }

/// <summary>One session's Coherence: the Mac windows this PC shows and everything said about them.
/// <list type="bullet">
/// <item>Kinds 45 and 41 go through the <see cref="WindowSet"/>; the UI gets back what changed and
///   makes, updates or closes its windows for it.</item>
/// <item>Kind 29 (<see cref="SideStreamPlan"/>) goes whenever the set, the focus, the hidden list, the
///   scale or the screen change, coalesced (<see cref="RequestCoalescer"/>): at once, then at most
///   every 100 ms, never the same twice.</item>
/// <item>Kinds 30 and 31 are routed by window ID to that window's video, straight from the reader's
///   thread; the parameter sets of a window whose video is not there yet are kept for it.</item>
/// <item>Kind 44 `{window}` per window by the iOS rule (<see cref="KeyframeRecovery"/>, one each), fed
///   by that window's video thread and looked at every 250 ms.</item>
/// <item>Kind 46 per window through a <see cref="PlaceThrottle"/>; kind 15 `activate` and `close`.</item>
/// </list>
/// The UI thread calls everything but <see cref="Route"/>, <see cref="Interrupted"/> and
/// <see cref="PictureBack"/>, which take their own lock. The clock is seconds on a monotonic clock,
/// handed in, so WindowsClient/Sill.Checks runs it on a simulated one. Pure.</summary>
public sealed class CoherenceController
{
    /// <summary>How often the kind 44 rule is looked at, as the session's tick.</summary>
    public const double KeyframeLook = 0.25;

    readonly Func<double> clock;
    readonly object gate = new();
    // Under `gate`: touched by the reader's thread and each window's video thread.
    readonly Dictionary<uint, IWindowVideo> videos = new();
    readonly Dictionary<uint, ParameterSets> heldSets = new();
    readonly Dictionary<uint, KeyframeRecovery> recoveries = new();
    readonly Dictionary<uint, long> framesIn = new();
    long framesDropped;
    double worstRttMs = 50;
    // The UI thread's.
    readonly Dictionary<uint, PlaceThrottle> throttles = new();
    readonly RequestCoalescer coalescer = new();
    ICoherenceLink? link;
    int placeToken;
    double nextKeyframeLook;
    volatile bool active;

    public WindowSet Set { get; } = new();
    public SideStreamPlan Plan { get; } = new();
    public bool Active => active;
    /// <summary>The host's `windowInput` generation (WindowList), for aiming input.</summary>
    public int? WindowInput { get; set; }
    /// <summary>Lines for the log (which window asked for a keyframe, what kind 29 said).</summary>
    public event Action<string>? Note;

    public CoherenceController(Func<double> clock) { this.clock = clock; }

    /// <summary>The worst round trip of the last second, for how long a keyframe ask waits.</summary>
    public double WorstRttMs { get { lock (gate) return worstRttMs; } set { lock (gate) worstRttMs = value; } }

    /// <summary>A session: the first, or one that replaced a lost one. Its kind 45 generations start
    /// again, and in Coherence the set is asked for again at once.</summary>
    public void Attach(ICoherenceLink session)
    {
        link = session;
        Set.ResetGeneration();
        coalescer.Reset();
        placeToken = 0;
        foreach (var t in throttles.Values) t.NewSession();
        lock (gate) foreach (var r in recoveries.Values) r.LeftForeground();
        if (active) { coalescer.Mark(); Flush(); }
    }

    // MARK: entering and leaving

    /// <summary>Starts Coherence with these windows (each streamed once the Mac lists it), these apps
    /// (whose new windows join by themselves), or every app's; kind 29 goes at once.</summary>
    public void Enter(IEnumerable<uint> windows, IEnumerable<string>? apps = null, bool all = false, uint? focus = null)
    {
        double now = clock();
        Set.Clear();
        Plan.Clear();
        foreach (var id in windows) Plan.Ask(id, now);
        if (apps != null) foreach (var a in apps) if (!string.IsNullOrWhiteSpace(a) && !Plan.Apps.Contains(a)) Plan.Apps.Add(a);
        Plan.All = all;
        Plan.Focus = focus;
        active = true;
        coalescer.Reset();
        coalescer.Mark();
        Flush();
    }

    /// <summary>Ends Coherence: kind 29 with no window, and everything forgotten. The windows that were
    /// shown, for the UI to close.</summary>
    public List<MacWindow> Leave()
    {
        var shown = Set.All.ToList();
        if (active) link?.Send(Kind.SideStreams, Json.Encode(SideStreamPlan.Stop()));
        active = false;
        Set.Clear();
        Plan.Clear();
        throttles.Clear();
        coalescer.Reset();
        lock (gate)
        {
            videos.Clear();
            heldSets.Clear();
            recoveries.Clear();
            framesIn.Clear();
        }
        return shown;
    }

    // MARK: from the Mac

    /// <summary>Kind 45. Nothing outside Coherence.</summary>
    public WindowSetUpdate Apply(WindowState m)
    {
        if (!active) return WindowSetUpdate.Dropped;
        var u = Set.Apply(m);
        if (!u.Applied) return u;
        Plan.Listed(Set, u.Full, clock());
        Forget(u.Removed);
        coalescer.Mark();
        Flush();
        return u;
    }

    /// <summary>Kind 41: the person at the Mac took these windows back.</summary>
    public WindowSetUpdate TakenBack(TakenBack m)
    {
        if (!active) return WindowSetUpdate.Dropped;
        var u = Set.TakeBack(m.Windows);
        foreach (var id in m.Windows) Plan.Forget(id);
        Forget(u.Removed);
        coalescer.Mark();
        Flush();
        return u;
    }

    void Forget(IEnumerable<MacWindow> removed)
    {
        foreach (var w in removed)
        {
            Plan.Forget(w.Id);
            throttles.Remove(w.Id);
            lock (gate)
            {
                videos.Remove(w.Id);
                heldSets.Remove(w.Id);
                recoveries.Remove(w.Id);
                framesIn.Remove(w.Id);
            }
        }
    }

    // MARK: from this PC

    /// <summary>This PC stops showing a window (and its children); the Mac keeps it.</summary>
    public WindowSetUpdate StopShowing(uint id)
    {
        var u = Set.Forget(id);
        Plan.Forget(id);
        Forget(u.Removed);
        coalescer.Mark();
        Flush();
        return u;
    }

    /// <summary>Another window to show.</summary>
    public void Add(uint id)
    {
        if (!active || Set.Contains(id)) return;
        Plan.Ask(id, clock());
        coalescer.Mark();
        Flush();
    }

    /// <summary>Another app whose new windows join by themselves (kind 29's `apps`).</summary>
    public void AddApp(string bundleID)
    {
        if (!active || string.IsNullOrWhiteSpace(bundleID) || Plan.Apps.Contains(bundleID)) return;
        Plan.Apps.Add(bundleID);
        coalescer.Mark();
        Flush();
    }

    /// <summary>A window of this PC's was activated: the Mac makes it key (kind 15 `activate`), and it
    /// is the focus, streamed at the focused rate.</summary>
    public void Activated(uint id)
    {
        if (!active) return;
        link?.Send(Kind.WindowCommand, Json.Encode(new WindowCommand { Id = id, Action = WindowActions.Activate }));
        if (Plan.Focus == id) return;
        Plan.Focus = id;
        Plan.SetMinimized(id, false);
        coalescer.Mark();
        Flush();
    }

    /// <summary>The window's close box: the Mac closes it (kind 15 `close`), which may ask first ("Save
    /// changes?"); the window stays here until the Mac drops it.</summary>
    public void Close(uint id)
    {
        if (active) link?.Send(Kind.WindowCommand, Json.Encode(new WindowCommand { Id = id, Action = WindowActions.Close }));
    }

    /// <summary>Minimized here (or cloaked), or back: the hidden list.</summary>
    public void SetMinimized(uint id, bool on)
    {
        if (Plan.IsMinimized(id) == on) return;
        Plan.SetMinimized(id, on);
        if (on && Plan.Focus == id) Plan.Focus = null;
        coalescer.Mark();
        Flush();
    }

    public void SetScale(double scale)
    {
        if (Math.Abs(Plan.Scale - scale) < 0.001) return;
        Plan.Scale = scale;
        coalescer.Mark();
        Flush();
    }

    public void SetScreen(double w, double h)
    {
        if (Math.Abs(Plan.Screen.w - w) < 1 && Math.Abs(Plan.Screen.h - h) < 1) return;
        Plan.Screen = (w, h);
        coalescer.Mark();
        Flush();
    }

    public void SetRates(int fps, int focusFps)
    {
        if (Plan.Fps == fps && Plan.FocusFps == focusFps) return;
        Plan.Fps = fps; Plan.FocusFps = focusFps;
        coalescer.Mark();
        Flush();
    }

    /// <summary>A window here was sized to (w, h) points (<see cref="PlaceThrottle"/>).</summary>
    public void Resized(uint id, double w, double h, bool final)
    {
        if (!active || !Set.Contains(id)) return;
        if (!throttles.TryGetValue(id, out var t)) throttles[id] = t = new PlaceThrottle();
        if (t.Change(w, h, clock(), final) is { } size) Place(id, t, size);
    }

    /// <summary>Whether the Mac's size for a window applies to it now (<see cref="PlaceThrottle.TakesMacSize"/>).</summary>
    public bool TakesMacSize(uint id, int? answering, bool resizing) =>
        !throttles.TryGetValue(id, out var t) || t.TakesMacSize(answering, resizing, clock());

    /// <summary>The window a kind 46 token was sent for, while its answer has not come.</summary>
    public uint? WindowOfToken(int token)
    {
        foreach (var (id, t) in throttles) if (t.Outstanding == token) return id;
        return null;
    }

    /// <summary>The window here took the Mac's size.</summary>
    public void Settled(uint id, double w, double h)
    {
        if (!throttles.TryGetValue(id, out var t)) throttles[id] = t = new PlaceThrottle();
        t.Settled(w, h);
    }

    void Place(uint id, PlaceThrottle t, (double w, double h) size)
    {
        int token = ++placeToken;
        t.Sent(token);
        link?.Send(Kind.PlaceWindow, Json.Encode(new PlaceWindow { Id = id, Size = new[] { size.w, size.h }, Token = token }));
    }

    /// <summary>What is owed when its time comes: kind 29 held by the interval, sizes held by the
    /// throttle, keyframe asks. The UI calls it every 50 ms or so.</summary>
    public void Tick()
    {
        double now = clock();
        Flush();
        foreach (var (id, t) in throttles) if (t.Tick(now) is { } size) Place(id, t, size);
        if (now >= nextKeyframeLook)
        {
            nextKeyframeLook = now + KeyframeLook;
            List<uint> ids;
            lock (gate) ids = recoveries.Keys.ToList();
            foreach (var id in ids) ConsiderKeyframe(id);
        }
    }

    /// <summary>Kind 29 now, if one is owed and the interval allows (else at a later <see cref="Tick"/>).</summary>
    void Flush()
    {
        if (!active || link == null || !coalescer.Due(clock())) return;
        var request = Plan.Request(Set);
        var payload = Json.Encode(request);
        if (!coalescer.Offer(payload, clock())) return;
        link.Send(Kind.SideStreams, payload);
        Note?.Invoke($"coherence: kind 29 {System.Text.Encoding.UTF8.GetString(payload)}");
    }

    /// <summary>The next kind 29 as it would go (for the log and the checks).</summary>
    public SideStreamRequest Request => Plan.Request(Set);

    // MARK: video (any thread)

    /// <summary>A window's video is ready: the parameter sets kept for it go first.</summary>
    public void AttachVideo(uint id, IWindowVideo video)
    {
        ParameterSets? held;
        lock (gate)
        {
            videos[id] = video;
            heldSets.Remove(id, out held);
            if (!recoveries.ContainsKey(id)) recoveries[id] = new KeyframeRecovery();
        }
        if (held != null) video.Submit(held);
    }

    public void DetachVideo(uint id)
    {
        lock (gate) { videos.Remove(id); recoveries.Remove(id); }
    }

    /// <summary>Kinds 30 and 31, from the reader's thread: to the window's video by its ID. A frame for
    /// a window with no video is dropped (its picture will wait for a keyframe, which the rule asks
    /// for); its parameter sets are kept.</summary>
    public void Route(Kind kind, byte[] payload, bool keyframe, double timestamp)
    {
        if (SideStream.Unpack(payload) is not var (id, rest)) return;
        IWindowVideo? video;
        if (kind == Kind.SideParameterSets)
        {
            if (ParameterSets.Parse(rest) is not { } sets) return;
            lock (gate)
            {
                if (!videos.TryGetValue(id, out video)) { if (active) heldSets[id] = sets; return; }
            }
            video.Submit(sets);
        }
        else if (kind == Kind.SideFrame)
        {
            lock (gate)
            {
                if (!videos.TryGetValue(id, out video)) { framesDropped++; return; }
                framesIn[id] = framesIn.GetValueOrDefault(id) + 1;
            }
            video.Submit(rest, keyframe, timestamp);
        }
    }

    /// <summary>Frames each window got since the last call, and those for no window (for the log).</summary>
    public (Dictionary<uint, long> perWindow, long dropped) TakeFrameCounts()
    {
        lock (gate)
        {
            var copy = new Dictionary<uint, long>(framesIn);
            framesIn.Clear();
            long d = framesDropped;
            framesDropped = 0;
            return (copy, d);
        }
    }

    /// <summary>The window's picture waits for a keyframe (its video thread).</summary>
    public void Interrupted(uint id)
    {
        lock (gate)
        {
            if (!recoveries.TryGetValue(id, out var r)) return;
            r.Interrupted(clock());
        }
        ConsiderKeyframe(id);
    }

    /// <summary>The window's picture is back (its video thread).</summary>
    public void PictureBack(uint id)
    {
        lock (gate) if (recoveries.TryGetValue(id, out var r)) r.PictureBack(clock(), active: true);
    }

    void ConsiderKeyframe(uint id)
    {
        if (!active) return;
        // Streaming: a window in the set this PC asks for, and not hidden (a hidden one's frames are
        // dropped on the Mac, so its keyframe would not come either; it is asked once it shows).
        bool streaming = Set.Contains(id) && !Plan.IsMinimized(id);
        KeyframeDecision decision;
        lock (gate)
        {
            if (!recoveries.TryGetValue(id, out var r)) return;
            decision = r.Consider(clock(), active: true, streaming, KeyframeRecovery.AnswerWait(worstRttMs));
        }
        if (decision.Ask != KeyframeAsk.AskNow) return;
        link?.Send(Kind.KeyframeRequest, KeyframeRequest.Payload(id, Capabilities.Coherence));
        Note?.Invoke($"keyframe: asked the Mac for one of window {id}");
    }

    // MARK: input

    /// <summary>A kind 8 event aimed at a window (docs/coherence-protocol.md): every event carries
    /// `window` to a host whose list says `windowInput` 2; to one that says 1 (#66's) only moves and
    /// scrolls do, and a click on a window that is not the focus is sent after an `activate` of it,
    /// so the Mac clicks where the window is key. The payload, and whether to activate first.</summary>
    public static (byte[] payload, bool activateFirst) Aim(byte[] eventJson, uint window, InputKind kind, int? windowInput, bool focused)
    {
        int gen = windowInput ?? 0;
        if (gen >= Capabilities.WindowInputEvery) return (InputEvents.InWindow(eventJson, window), false);
        if (gen >= 1 && kind is InputKind.Move or InputKind.Scroll) return (InputEvents.InWindow(eventJson, window), false);
        return (eventJson, kind == InputKind.Button && !focused);
    }
}
