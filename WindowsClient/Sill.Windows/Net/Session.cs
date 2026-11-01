using System.Diagnostics;
using System.Reflection;
using Sill.Windows.Protocol;
using Sill.Windows.Video;

namespace Sill.Windows.Net;

/// <summary>How a session ended, for the connect screen's line and whether to dial again.</summary>
public sealed record SessionEnd(string Message, bool Reconnect, string? Reason = null);

/// <summary>One session with a Mac over one connection, as StreamClient runs it on the device: the
/// hello first, the viewport, pings and the once-a-second stats; the catalog, the Mac's settings,
/// cursor and clipboard as they come; frames straight to the video thread. Events fire on the
/// connection's threads: a form marshals them itself.</summary>
public sealed partial class Session : IDisposable
{
    readonly Connection connection;
    readonly VideoPipeline? video;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly System.Threading.Timer timer;
    readonly object gate = new();
    int token;
    Goodbye? goodbye;
    bool listed, clipboardSubscribed;
    DateTime lastAutoDesktop = DateTime.MinValue;
    long choicesSent;
    Source previousActive = Source.None;
    Viewport? lastViewport;
    double nextPing, nextStats = 1;

    // The second being measured (on the reader's thread and the timer's, under `gate`).
    readonly List<double> ages = new(), rtts = new();
    int frames;
    double worstRtt = 50;
    // When to ask the Mac for a keyframe after the picture was interrupted (kind 44), fed by the video
    // thread's events and looked at on each tick; under `gate`.
    readonly KeyframeRecovery keyframes = new();

    public string MacName { get; private set; }
    public string DeviceName { get; } = $"{Identity.DeviceName} (Windows)";
    public WindowList? Windows { get; private set; }
    public HostSettingsState? Settings { get; private set; }
    public List<AppInfo> Apps { get; private set; } = new();
    public Dictionary<uint, byte[]> Thumbnails { get; } = new();
    public Dictionary<string, byte[]> Icons { get; } = new();
    public ParameterSets? Sets { get; private set; }
    public bool Tls => connection.Dialed.Tls;
    public string Address => connection.Dialed.Endpoint.ToString();

    /// <summary>The last second's figures, for the status line.</summary>
    public (int fps, double rttMs, double ageMs) Figures { get; private set; }

    public event Action? WindowsChanged;
    public event Action<uint>? ThumbnailChanged;
    public event Action? AppsChanged;
    public event Action? SettingsChanged;
    public event Action<(float hotX, float hotY, float width, float height, byte[] png)>? CursorChanged;
    public event Action<string>? ClipboardReceived;
    /// <summary>A kind 43 payload (the Mac's sound), on the reader's thread.</summary>
    public event Action<byte[]>? AudioReceived;
    public event Action<SessionEnd>? Ended;
    public event Action? FiguresChanged;

    public Session(Dialed dialed, string macName, VideoPipeline? video)
    {
        MacName = macName;
        this.video = video;
        connection = new Connection(dialed);
        connection.Received += OnMessage;
        connection.Ended += OnEnded;
        timer = new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        if (video != null)
        {
            video.Interrupted += OnPictureInterrupted;
            video.PictureBack += OnPictureBack;
        }
    }

    public static string AppVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.5.1";

    public void Start(Viewport? viewport)
    {
        connection.Start();
        // The hello is the first message of every session connection (Compatibility.swift).
        // The hello lists the sound's codecs this PC plays (pcm-s16le alone: Windows has no AAC-ELD
        // decoder), so a Mac that makes it sends kind 43; Mute is this PC's own and changes nothing here.
        connection.Send(Kind.Hello, Json.Encode(new Hello
        {
            AppVersion = AppVersion, Build = "1", Protocol = 1, Device = DeviceName, Audio = AudioCodec.DevicePlays.ToList(),
            // Coherence (Session.Coherence.cs): kind 45 is taken, and kind 4's icons at 256 px for the taskbar.
            Coherence = Capabilities.Coherence, IconSize = CoherenceIconSize,
        }));
        if (viewport != null) SendViewport(viewport);
        timer.Change(250, 250);
    }

    // MARK: sending

    public void Select(Source source)
    {
        Interlocked.Increment(ref choicesSent);
        connection.Send(Kind.SelectSource, Json.Encode(source.ToJson()));
    }

    public void Launch(string bundleID)
    {
        Interlocked.Increment(ref choicesSent);
        connection.Send(Kind.LaunchApp, Json.Encode(new LaunchApp { BundleID = bundleID }));
    }

    public void WindowCommand(uint id, string action) => connection.Send(Kind.WindowCommand, Json.Encode(new WindowCommand { Id = id, Action = action }));

    public void SendInput(byte[] json) => connection.Send(Kind.Input, json);

    public void SendViewport(Viewport v)
    {
        lastViewport = v;
        connection.Send(Kind.Viewport, Json.Encode(v));
    }

    /// <summary>Kind 17 with the next token: only the fields one control changed.</summary>
    public void ChangeSettings(HostSettingsChange change)
    {
        change.Token = Interlocked.Increment(ref token);
        connection.Send(Kind.ChangeSettings, Json.Encode(change));
    }

    /// <summary>Text for the Mac's clipboard (only to a Mac that takes it), as a device's Paste sends it.</summary>
    public bool SendClipboard(string text)
    {
        if ((Windows?.Clipboard ?? 0) < 1 || string.IsNullOrEmpty(text)) return false;
        var bytes = System.Text.Encoding.UTF8.GetByteCount(text);
        if (bytes > 256 * 1024) return false;
        // JSON writes a control character as six bytes, so 256 KB of text can be more than the Mac
        // takes in a message: an expected case, so measured here rather than refused by Send.
        var payload = Json.Encode(new ClipboardPayload { Text = text });
        if (!Wire.Fits(payload.Length)) return false;
        if (!connection.Send(Kind.Clipboard, payload)) return false;
        clipboardSubscribed = true;
        return true;
    }

    /// <summary>Subscribes to the Mac's copies, or stops (the switch).</summary>
    public void ShareClipboard(bool on)
    {
        if ((Windows?.Clipboard ?? 0) < 1) return;
        if (on && !clipboardSubscribed)
        {
            connection.Send(Kind.Clipboard, "{}"u8);
            clipboardSubscribed = true;
        }
        else if (!on && clipboardSubscribed)
        {
            connection.Send(Kind.Clipboard, Json.Encode(new ClipboardPayload { Stop = true }));
            clipboardSubscribed = false;
        }
    }

    public void Close() { closedByUser = true; connection.Close(); }

    public void Dispose()
    {
        closedByUser = true;
        if (video != null)
        {
            video.Interrupted -= OnPictureInterrupted;
            video.PictureBack -= OnPictureBack;
        }
        timer.Dispose();
        connection.Close();
    }

    // MARK: the picture's recovery

    void OnPictureInterrupted(string why)
    {
        lock (gate) keyframes.Interrupted(clock.Elapsed.TotalSeconds);
        TestArguments.Log($"keyframe: the picture waits for one ({why})");
        ConsiderKeyframe();
    }

    void OnPictureBack()
    {
        lock (gate) keyframes.PictureBack(clock.Elapsed.TotalSeconds, active: true);
        TestArguments.Log("keyframe: the picture is back");
    }

    /// <summary>Asks the Mac for a keyframe (kind 44) when KeyframeRecovery says one is owed and due:
    /// at the interruption, then on the tick (every 250 ms) when the spacing lets a held ask go or an
    /// ask's keyframe is overdue. Sent to every Mac, as the iOS client does: one from before kind 44
    /// skips it (an unknown kind), and the rule's backoff asks it ever less often. Empty, the picked
    /// stream's; `{"window":…}` only once there are windows of their own, to a host that says
    /// `coherence` 1 (KeyframeRequest.Payload).</summary>
    void ConsiderKeyframe()
    {
        KeyframeDecision decision;
        bool wasOut;
        double wait, spacing;
        lock (gate)
        {
            wait = KeyframeRecovery.AnswerWait(worstRtt);
            wasOut = keyframes.Asked;
            bool streaming = Windows is { } list && list.Active.Kind != SourceKind.None;
            decision = keyframes.Consider(clock.Elapsed.TotalSeconds, active: true, streaming, wait);
            spacing = keyframes.SpacingNow;
        }
        if (decision.Ask != KeyframeAsk.AskNow) return;
        connection.Send(Kind.KeyframeRequest, KeyframeRequest.Payload(null, Windows?.Coherence));
        TestArguments.Log(wasOut ? $"keyframe: none came {wait:0.0} s after the last ask; asked again (spacing {spacing:0} s)"
                                 : $"keyframe: asked the Mac for one (spacing {spacing:0} s)");
    }

    // MARK: receiving

    void OnMessage(Header h, byte[] payload)
    {
        switch ((Kind)h.Kind)
        {
            case Kind.ParameterSets:
                if (ParameterSets.Parse(payload) is { } sets)
                {
                    Sets = sets;
                    video?.Submit(sets);
                }
                break;
            case Kind.Frame:
                {
                    double age = (Wire.Now - h.Timestamp) * 1000;
                    lock (gate)
                    {
                        frames++;
                        if (double.IsFinite(age)) ages.Add(Math.Clamp(age, 0, 10_000));
                    }
                    video?.Submit(payload, h.IsKeyframe, h.Timestamp);
                }
                break;
            case Kind.WindowList:
                if (WindowList.Parse(payload) is { } list) OnWindowList(list);
                break;
            case Kind.Thumbnail:
                if (Blobs.Thumbnail(payload) is var (id, jpeg))
                {
                    lock (Thumbnails) Thumbnails[id] = jpeg;
                    ThumbnailChanged?.Invoke(id);
                }
                break;
            case Kind.AppIcon:
                if (Blobs.Icon(payload) is var (bundle, png))
                {
                    lock (Icons) Icons[bundle] = png;
                    AppsChanged?.Invoke();
                }
                break;
            case Kind.AppList:
                if (Json.Decode<List<AppInfo>>(payload) is { } apps)
                {
                    Apps = apps;
                    AppsChanged?.Invoke();
                }
                break;
            case Kind.CursorShape:
                if (Blobs.Cursor(payload) is { } c) CursorChanged?.Invoke(c);
                break;
            case Kind.HostSettings:
                if (Json.Decode<HostSettingsState>(payload) is { } state)
                {
                    Settings = state;
                    SettingsChanged?.Invoke();
                }
                break;
            case Kind.Pong:
                {
                    double sent = Connection.PongSeconds(payload);
                    if (payload.Length == 8 && double.IsFinite(sent))
                    {
                        double rtt = (clock.Elapsed.TotalSeconds - sent) * 1000;
                        if (rtt >= 0) lock (gate) rtts.Add(Math.Min(rtt, 10_000));
                    }
                }
                break;
            case Kind.Goodbye:
                goodbye = Json.Decode<Goodbye>(payload) ?? new Goodbye();
                break;
            case Kind.Clipboard:
                if (Json.Decode<ClipboardPayload>(payload)?.Text is { Length: > 0 } text) ClipboardReceived?.Invoke(text);
                break;
            case Kind.Audio:
                AudioReceived?.Invoke(payload);
                break;
            case Kind.MacInfo:
                if (Json.Decode<SignedMacInfo>(payload) is { } signed && MacInfoCheck.Verify(signed) is var (info, fp) &&
                    connection.Dialed.MacFingerprint is { } key && key.AsSpan().SequenceEqual(fp))
                {
                    var name = info.Name;
                    if (!string.IsNullOrWhiteSpace(name)) MacName = name;
                }
                break;
            // Coherence's windows and the Mac's menus (Session.Coherence.cs).
            case Kind.SideParameterSets:
            case Kind.SideFrame:
            case Kind.WindowState:
            case Kind.TakenBack:
            case Kind.MacMenu:
                OnCoherenceMessage(h, payload);
                break;
        }
    }

    void OnWindowList(WindowList list)
    {
        var previous = previousActive;
        previousActive = list.Active;
        Windows = list;
        if (!string.IsNullOrWhiteSpace(list.MacName)) MacName = list.MacName;
        lock (Thumbnails)
        {
            var live = list.Windows.Select(w => w.Id).ToHashSet();
            foreach (var gone in Thumbnails.Keys.Where(k => !live.Contains(k)).ToList()) Thumbnails.Remove(gone);
        }
        if (!listed)
        {
            listed = true;
            ShareClipboard(Net.Settings.Current.ShareClipboard);
            SubscribeToMenus();
        }
        WindowsChanged?.Invoke();
        // The device starts on the Desktop, never on an empty panel (DiscoveryPolicy.autoDesktop): on
        // a connection's first list, when the Mac stopped the Desktop it streamed, and when the window
        // watched has closed (after 2 s, unless the user chose something meanwhile). At most every 10 s.
        if (list.Active.Kind != SourceKind.None) return;
        // In Coherence nothing is picked on purpose: the windows stream as side streams.
        if (Coherent) return;
        if (DateTime.UtcNow - lastAutoDesktop < TimeSpan.FromSeconds(10)) return;
        switch (previous.Kind)
        {
            case SourceKind.None:
            case SourceKind.Desktop:
                lastAutoDesktop = DateTime.UtcNow;
                connection.Send(Kind.SelectSource, Json.Encode(Source.Desktop.ToJson()));
                break;
            case SourceKind.Window when !list.Windows.Any(w => w.Id == previous.WindowID):
                var choices = Interlocked.Read(ref choicesSent);
                _ = Task.Delay(2000 + (int)Math.Min(worstRtt, 2000)).ContinueWith(_ =>
                {
                    if (!connection.IsOpen || Interlocked.Read(ref choicesSent) != choices) return;
                    if (Windows?.Active.Kind != SourceKind.None || Windows.Windows.Any(w => w.Id == previous.WindowID)) return;
                    lastAutoDesktop = DateTime.UtcNow;
                    connection.Send(Kind.SelectSource, Json.Encode(Source.Desktop.ToJson()));
                });
                break;
        }
    }

    void Tick()
    {
        double now = clock.Elapsed.TotalSeconds;
        if (now >= nextPing)
        {
            connection.Send(Kind.Ping, Connection.PingPayload(now));
            nextPing = now + 0.5;
        }
        ConsiderKeyframe();
        // Liveness: no byte for max(6 s, 4 × the worst round trip) is a connection gone.
        double silent = (DateTime.UtcNow - connection.LastReceived).TotalSeconds;
        if (silent > Math.Max(6, 4 * worstRtt / 1000))
        {
            silence = new TimeoutException($"Nothing came from {MacName} for {silent:0} s.");
            connection.Close();
            return;
        }
        if (now < nextStats) return;
        nextStats = now + 1;
        // Once a second: the stats the Mac logs, and the status line's.
        int f; double age, ageMax, rtt, rttMax;
        lock (gate)
        {
            f = frames; frames = 0;
            (age, ageMax) = MedianMax(ages); ages.Clear();
            (rtt, rttMax) = MedianMax(rtts); rtts.Clear();
            if (rttMax >= 0) worstRtt = rttMax;
        }
        connection.Send(Kind.ClientStats, Json.Encode(new ClientStats
        {
            Fps = f, FrameAgeMs = (int)Math.Round(age), RttMs = (int)Math.Round(rtt), Device = DeviceName,
            FrameAgeMaxMs = (int)Math.Round(ageMax), RttMaxMs = (int)Math.Round(rttMax),
        }));
        Figures = (f, rtt, age);
        FiguresChanged?.Invoke();
    }

    static (double median, double max) MedianMax(List<double> v)
    {
        if (v.Count == 0) return (-1, -1);
        var s = v.OrderBy(x => x).ToList();
        return (s[s.Count / 2], s[^1]);
    }

    int endedOnce;
    volatile Exception? silence;
    volatile bool closedByUser;

    void OnEnded(Exception? why)
    {
        if (Interlocked.Exchange(ref endedOnce, 1) != 0) return;
        try { timer.Change(Timeout.Infinite, Timeout.Infinite); } catch (ObjectDisposedException) { }
        Ended?.Invoke(Describe(why ?? silence));
    }

    SessionEnd Describe(Exception? why)
    {
        var g = goodbye;
        if (g != null)
        {
            // GoodbyePolicy: today's reasons keep their words; any other is the Mac's own, with its reconnect.
            return g.Reason switch
            {
                "quit" => new SessionEnd($"{MacName} quit Sill.", false, g.Reason),
                "removed" => new SessionEnd($"{MacName} removed this PC. Connect to pair again.", false, g.Reason),
                "pairingRequired" => new SessionEnd($"{MacName} now asks devices to pair. Connect to pair this PC.", false, g.Reason),
                "busy" => new SessionEnd($"{MacName} has too many devices connected.", false, g.Reason),
                "remoteOff" or "internetOff" => new SessionEnd($"{MacName} turned off Remote Access.", false, g.Reason),
                _ => new SessionEnd(string.IsNullOrWhiteSpace(g.Message) ? $"{MacName} ended the session." : Clean(g.Message!), g.Reconnect == true, g.Reason),
            };
        }
        if (why == null || closedByUser) return new SessionEnd("Disconnected.", false);
        // A TLS session whose first read fails before any message: the Mac refused this PC's key.
        if (!listed && Tls && why is IOException or System.Security.Authentication.AuthenticationException)
            return new SessionEnd($"{MacName} doesn’t know this PC. Connect to pair again.", false, "unpaired");
        return new SessionEnd($"Lost the connection to {MacName}.", true);
    }

    static string Clean(string s)
    {
        var t = new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return t.Length > 300 ? t[..300] : t;
    }
}
