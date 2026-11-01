using Sill.Windows.Protocol;

namespace Sill.Windows.Coherence;

/// <summary>What kind 29 asks for in Coherence (docs/coherence-protocol.md): the whole set each time,
/// since a window left out stops. The windows are every one this PC shows (<see cref="WindowSet.Streamed"/>:
/// top-level windows, and the children that spill out of their owner's picture), then the windows this
/// PC asked for that the Mac has not listed yet. The hidden list's rules:
/// <list type="bullet">
/// <item>a window minimized on this PC, or cloaked (on another virtual desktop), is hidden;</item>
/// <item>a child is hidden with the window it belongs to;</item>
/// <item>the focused window never is (it is being brought back), and only windows in the set are.</item>
/// </list>
/// A window asked for and not listed after <see cref="AskedGrace"/> seconds is dropped at the next
/// `full` kind 45, which lists every window this PC should have. Pure.</summary>
public sealed class SideStreamPlan
{
    /// <summary>How long a window asked for may go unlisted by a `full` kind 45 before it is dropped:
    /// the full may have been written before the host read the ask.</summary>
    public const double AskedGrace = 2;

    readonly Dictionary<uint, double> asked = new();
    readonly HashSet<uint> minimized = new();

    public List<string> Apps { get; } = new();
    public bool All { get; set; }
    public uint? Focus { get; set; }
    /// <summary>This PC's pixels per point (the monitor's DPI scale).</summary>
    public double Scale { get; set; } = 1;
    /// <summary>The largest screen in points.</summary>
    public (double w, double h) Screen { get; set; } = (1920, 1080);
    /// <summary>Frames a second for each window but the focused one (kind 29's `fps`).</summary>
    public int Fps { get; set; } = 30;
    public int FocusFps { get; set; } = SideStreamRequest.DefaultFocusFPS;

    public IReadOnlyCollection<uint> Asked => asked.Keys;

    /// <summary>A window this PC wants: in the next kind 29 until the Mac lists it, or a full forgets it.</summary>
    public void Ask(uint id, double now)
    {
        if (!asked.ContainsKey(id)) asked[id] = now;
    }

    public void Forget(uint id)
    {
        asked.Remove(id);
        minimized.Remove(id);
        if (Focus == id) Focus = null;
    }

    public void SetMinimized(uint id, bool on)
    {
        if (on) minimized.Add(id); else minimized.Remove(id);
    }

    public bool IsMinimized(uint id) => minimized.Contains(id);

    public void Clear()
    {
        asked.Clear();
        minimized.Clear();
        Apps.Clear();
        All = false;
        Focus = null;
    }

    /// <summary>After a kind 45: windows asked for that the Mac now lists are no longer pending; a full
    /// one drops those it leaves out, once their grace is over; state of windows gone is dropped.</summary>
    public void Listed(WindowSet set, bool full, double now)
    {
        foreach (var id in asked.Keys.ToList())
        {
            if (set.Contains(id)) asked.Remove(id);
            else if (full && now - asked[id] >= AskedGrace) asked.Remove(id);
        }
        minimized.RemoveWhere(id => !set.Contains(id) && !asked.ContainsKey(id));
        if (Focus is { } f && !set.Contains(f) && !asked.ContainsKey(f)) Focus = null;
    }

    /// <summary>The windows of the next kind 29, in the order this PC has them.</summary>
    public List<uint> Windows(WindowSet set)
    {
        var ids = new List<uint>();
        var seen = new HashSet<uint>();
        foreach (var w in set.Streamed) if (seen.Add(w.Id)) ids.Add(w.Id);
        foreach (var id in asked.OrderBy(a => a.Value).Select(a => a.Key)) if (seen.Add(id)) ids.Add(id);
        return ids;
    }

    /// <summary>The hidden list (the rules above), in the order of <see cref="Windows"/>.</summary>
    public List<uint> Hidden(WindowSet set)
    {
        var hidden = new List<uint>();
        foreach (var id in Windows(set))
        {
            if (id == Focus) continue;
            if (IsHidden(set, id)) hidden.Add(id);
        }
        return hidden;
    }

    bool IsHidden(WindowSet set, uint id)
    {
        if (minimized.Contains(id)) return true;
        if (set[id] is { } w && set.TopOwner(w) is { } owner) return owner.Id != Focus && minimized.Contains(owner.Id);
        return false;
    }

    /// <summary>The kind 29 of Coherence, its optional fields left out when empty (nil on the wire is none).</summary>
    public SideStreamRequest Request(WindowSet set)
    {
        var windows = Windows(set);
        var hidden = Hidden(set);
        uint? focus = Focus is { } f && windows.Contains(f) ? f : null;
        return new SideStreamRequest
        {
            Windows = windows,
            Fps = Math.Clamp(Fps, 1, SideStreamRequest.MaxFPS),
            WholeApp = true,
            Screen = new[] { Math.Round(Screen.w), Math.Round(Screen.h) },
            Coherence = true,
            Focus = focus,
            FocusFPS = Math.Clamp(FocusFps, 1, SideStreamRequest.MaxFocusFPS),
            Apps = Apps.Count > 0 ? Apps.Distinct().ToList() : null,
            All = All ? true : null,
            Hidden = hidden.Count > 0 ? hidden : null,
            Scale = Math.Round(Math.Clamp(Scale, 0.5, 4), 3),
        };
    }

    /// <summary>The kind 29 that ends Coherence: no window, no coherence.</summary>
    public static SideStreamRequest Stop() => new() { Windows = new() };
}

/// <summary>When kind 29 goes: whenever the set, the focus, the hidden list or the scale changes, but
/// at most once every <see cref="Interval"/> seconds (the first change at once, the rest together at
/// the interval's end), and never the same request twice in a row. Pure: the caller hands it the time.</summary>
public sealed class RequestCoalescer
{
    /// <summary>The least time between two kind 29s.</summary>
    public double Interval { get; init; } = 0.1;
    byte[]? last;
    double lastAt = double.NegativeInfinity;

    /// <summary>Something changed since the last request went.</summary>
    public bool Dirty { get; private set; }

    public void Mark() => Dirty = true;

    /// <summary>When the next request may go (null: nothing to send).</summary>
    public double? NextAt => Dirty ? lastAt + Interval : null;

    /// <summary>Whether a request may go now.</summary>
    public bool Due(double now) => Dirty && now >= lastAt + Interval;

    /// <summary>The request as it would go: true to send it (counted as sent), false when it is the
    /// same as the last one sent (nothing goes, and nothing is owed).</summary>
    public bool Offer(byte[] payload, double now)
    {
        Dirty = false;
        if (last != null && last.AsSpan().SequenceEqual(payload)) return false;
        last = payload;
        lastAt = now;
        return true;
    }

    /// <summary>A new session: the next request goes whatever the last one was.</summary>
    public void Reset()
    {
        last = null;
        lastAt = double.NegativeInfinity;
        Dirty = false;
    }
}
