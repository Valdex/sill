using Sill.Windows.Protocol;

namespace Sill.Windows.Coherence;

// The device's view of the Mac windows it shows in Coherence (docs/coherence-protocol.md, kinds 45 and
// 41), and what each becomes on Windows. Pure (no WinForms, no Win32): WindowsClient/Sill.Checks runs it.

/// <summary>What a Mac window becomes on this PC.</summary>
public enum Presentation
{
    /// <summary>Nothing of its own: a child inside its owner's picture (`inCrop`), a child whose owner
    /// this PC does not have, or a menu, popover or tooltip with no owner to place it by.</summary>
    None,
    /// <summary>A Windows window with a taskbar button (a ProxyWindow).</summary>
    Window,
    /// <summary>A sheet or dialog that spills out of its owner: an owned top-level window with no
    /// taskbar button, streamed on its own.</summary>
    OwnedWindow,
    /// <summary>A menu, popover or tooltip that spills out of its owner: an owned, non-activating
    /// popup, streamed on its own.</summary>
    Popup,
}

/// <summary>What changed in a window between two kind 45s, so the UI touches only that.</summary>
[Flags]
public enum WindowChange
{
    None = 0,
    Title = 1,
    Size = 2,
    /// <summary>`min`, `max` or `resizable`.</summary>
    Limits = 4,
    Role = 8,
    /// <summary>`owner`, `rect` or `inCrop`: where a child goes, and whether it needs a stream.</summary>
    Placement = 16,
    Focus = 32,
    TitleBar = 64,
    /// <summary>`minimized`, `fullScreen` or `modal`.</summary>
    State = 128,
    /// <summary>`app` or `appName`.</summary>
    App = 256,
}

/// <summary>One Mac window as the device knows it: kind 45's entry, with the fields a partial update
/// left out kept from the one before.</summary>
public sealed class MacWindow
{
    public uint Id { get; init; }
    public string? App { get; set; }
    public string? AppName { get; set; }
    public string? Title { get; set; }
    /// <summary>The wire's role (null: none sent).</summary>
    public string? RawRole { get; set; }
    public double[]? Size { get; set; }
    public double[]? Min { get; set; }
    public double[]? Max { get; set; }
    public bool? Resizable { get; set; }
    public bool Focused { get; set; }
    public bool Minimized { get; set; }
    public bool FullScreen { get; set; }
    public bool Modal { get; set; }
    public double TitleBar { get; set; }
    public bool Joined { get; set; }
    public uint? Owner { get; set; }
    public double[]? Rect { get; set; }
    public bool? InCrop { get; set; }

    /// <summary>The role, a window for one this build does not know (or none).</summary>
    public string Role => RawRole is { } r && WindowState.Roles.All.Contains(r) ? r : WindowState.Roles.Window;
    /// <summary>The size in points, [width, height], when the Mac sent one.</summary>
    public (double w, double h)? SizePoints => Size is { Length: >= 2 } s && s[0] > 0 && s[1] > 0 ? (s[0], s[1]) : null;
    public bool IsMenuLike => Role is WindowState.Roles.Menu or WindowState.Roles.Popover or WindowState.Roles.Tooltip;
    /// <summary>The title shown on this PC: the Mac's, cleaned to one line, else the app's name.</summary>
    public string DisplayTitle => SafeText.Clean(Title, 200) ?? SafeText.Clean(AppName, 100) ?? SafeText.Clean(App, 100) ?? "Mac window";

    public MacWindow Clone() => (MacWindow)MemberwiseClone();

    /// <summary>A partial entry laid over this window: each field it carries replaces the one kept,
    /// each it leaves out stays. A full entry carries what it has, so the result is the same either way
    /// the host writes a change (the whole entry, or only what changed).</summary>
    public WindowChange Merge(WindowState.Window e)
    {
        var c = WindowChange.None;
        if (e.App != null && e.App != App) { App = e.App; c |= WindowChange.App; }
        if (e.AppName != null && e.AppName != AppName) { AppName = e.AppName; c |= WindowChange.App; }
        if (e.Title != null && e.Title != Title) { Title = e.Title; c |= WindowChange.Title; }
        if (e.Role != null && e.Role != RawRole) { RawRole = e.Role; c |= WindowChange.Role; }
        if (e.Size != null && !Same(e.Size, Size)) { Size = e.Size; c |= WindowChange.Size; }
        if (e.Min != null && !Same(e.Min, Min)) { Min = e.Min; c |= WindowChange.Limits; }
        if (e.Max != null && !Same(e.Max, Max)) { Max = e.Max; c |= WindowChange.Limits; }
        if (e.Resizable != null && e.Resizable != Resizable) { Resizable = e.Resizable; c |= WindowChange.Limits; }
        if (e.Focused is { } f && f != Focused) { Focused = f; c |= WindowChange.Focus; }
        if (e.Minimized is { } mi && mi != Minimized) { Minimized = mi; c |= WindowChange.State; }
        if (e.FullScreen is { } fs && fs != FullScreen) { FullScreen = fs; c |= WindowChange.State; }
        if (e.Modal is { } mo && mo != Modal) { Modal = mo; c |= WindowChange.State; }
        if (e.TitleBar is { } tb && Math.Max(0, tb) != TitleBar) { TitleBar = Math.Max(0, tb); c |= WindowChange.TitleBar; }
        if (e.Joined == true) Joined = true;
        if (e.Owner != null && e.Owner != Owner) { Owner = e.Owner; c |= WindowChange.Placement; }
        if (e.Rect != null && !Same(e.Rect, Rect)) { Rect = e.Rect; c |= WindowChange.Placement; }
        if (e.InCrop != null && e.InCrop != InCrop) { InCrop = e.InCrop; c |= WindowChange.Placement; }
        return c;
    }

    static bool Same(double[]? a, double[]? b) => a == null ? b == null : b != null && a.AsSpan().SequenceEqual(b);
}

/// <summary>What one kind 45 (or 41) did to the set.</summary>
public sealed class WindowSetUpdate
{
    public static readonly WindowSetUpdate Dropped = new() { Applied = false };

    /// <summary>False: older than the last applied (`gen`), so nothing changed.</summary>
    public bool Applied { get; init; } = true;
    public bool Full { get; init; }
    public List<MacWindow> Added { get; } = new();
    public List<(MacWindow window, WindowChange change)> Changed { get; } = new();
    public List<MacWindow> Removed { get; } = new();
    /// <summary>The kind 46 token this answers.</summary>
    public int? Answering { get; init; }
    public bool Any => Added.Count + Changed.Count + Removed.Count > 0;
}

/// <summary>Kind 45's reducer: the Mac windows this PC should have, by the rules of the contract. A
/// message whose `gen` is not newer than the last applied is dropped whole. `full`: the windows it
/// lists are all there are (any other is removed); otherwise each listed window is added or merged
/// (<see cref="MacWindow.Merge"/>), and `removed` goes. A removed window takes its children with it,
/// so nothing is left owned by a window this PC no longer has. The message's `focus` (the Mac's key
/// window) sets every window's `focused`, so a host that names only the window that gained focus
/// still clears the one that lost it. Kind 41 removes as `removed` does, with no generation.</summary>
public sealed class WindowSet
{
    readonly Dictionary<uint, MacWindow> windows = new();
    readonly List<uint> order = new();   // first seen first: the order windows are listed and placed

    /// <summary>The last generation applied; null before the first, and after <see cref="ResetGeneration"/>.</summary>
    public long? LastGen { get; private set; }
    /// <summary>The Mac's Secure Event Input, as the last message said.</summary>
    public bool SecureInput { get; private set; }

    public int Count => windows.Count;
    public IEnumerable<MacWindow> All => order.Select(id => windows[id]);
    public MacWindow? this[uint id] => windows.TryGetValue(id, out var w) ? w : null;
    public bool Contains(uint id) => windows.ContainsKey(id);

    /// <summary>A new session's generations start again (a reconnect, another launch of the host):
    /// the next message is applied whatever its `gen`, and its `full` replaces what is kept.</summary>
    public void ResetGeneration() => LastGen = null;

    public void Clear()
    {
        windows.Clear();
        order.Clear();
        LastGen = null;
        SecureInput = false;
    }

    public WindowSetUpdate Apply(WindowState m)
    {
        if (!WindowState.Newer(m.Gen, LastGen)) return WindowSetUpdate.Dropped;
        LastGen = m.Gen;
        if (m.SecureInput is { } secure) SecureInput = secure;
        var u = new WindowSetUpdate { Full = m.Full == true, Answering = m.Answering };
        var listed = new HashSet<uint>();
        foreach (var e in m.Windows ?? new())
        {
            // A window that names itself as its owner would loop: it has no owner.
            if (e.Owner == e.Id) e.Owner = null;
            listed.Add(e.Id);
            if (windows.TryGetValue(e.Id, out var w))
            {
                var c = w.Merge(e);
                if (c != WindowChange.None) u.Changed.Add((w, c));
            }
            else
            {
                w = new MacWindow { Id = e.Id };
                w.Merge(e);
                windows[e.Id] = w;
                order.Add(e.Id);
                u.Added.Add(w);
            }
        }
        var gone = new HashSet<uint>(m.Removed ?? new());
        if (u.Full) foreach (var id in windows.Keys) if (!listed.Contains(id)) gone.Add(id);
        RemoveWithChildren(gone, u);
        if (m.Focus is { } focus)
        {
            foreach (var w in windows.Values)
            {
                bool f = w.Id == focus;
                if (w.Focused == f) continue;
                w.Focused = f;
                Note(u, w, WindowChange.Focus);
            }
        }
        return u;
    }

    /// <summary>Kind 41: the person at the Mac took these windows back.</summary>
    public WindowSetUpdate TakeBack(IEnumerable<uint> ids)
    {
        var u = new WindowSetUpdate();
        RemoveWithChildren(new HashSet<uint>(ids), u);
        return u;
    }

    /// <summary>This PC stops showing a window (and its children) by its own choice.</summary>
    public WindowSetUpdate Forget(uint id)
    {
        var u = new WindowSetUpdate();
        RemoveWithChildren(new HashSet<uint> { id }, u);
        return u;
    }

    void RemoveWithChildren(HashSet<uint> gone, WindowSetUpdate u)
    {
        // Children of a removed window go too, and theirs, until nothing more is owned by one gone.
        bool more = true;
        while (more)
        {
            more = false;
            foreach (var w in windows.Values)
                if (w.Owner is { } o && gone.Contains(o) && gone.Add(w.Id)) more = true;
        }
        foreach (var id in gone)
        {
            if (!windows.Remove(id, out var w)) continue;
            order.Remove(id);
            u.Added.RemoveAll(a => a.Id == id);
            u.Changed.RemoveAll(c => c.window.Id == id);
            u.Removed.Add(w);
        }
    }

    static void Note(WindowSetUpdate u, MacWindow w, WindowChange c)
    {
        if (u.Added.Contains(w)) return;
        int i = u.Changed.FindIndex(x => x.window.Id == w.Id);
        if (i >= 0) u.Changed[i] = (w, u.Changed[i].change | c);
        else u.Changed.Add((w, c));
    }

    /// <summary>What a window becomes on this PC (<see cref="Presentation"/>).</summary>
    public Presentation PresentationOf(MacWindow w)
    {
        if (w.Owner is not { } owner)
            return w.IsMenuLike ? Presentation.None : Presentation.Window;
        if (!windows.ContainsKey(owner)) return Presentation.None;
        // Absent inCrop counts as inside: a host that cannot tell gives no stream it was not asked for.
        if (w.InCrop != false) return Presentation.None;
        return w.IsMenuLike ? Presentation.Popup : Presentation.OwnedWindow;
    }

    /// <summary>The window a child is shown beside: its owner's top-level ancestor that this PC shows.</summary>
    public MacWindow? TopOwner(MacWindow w)
    {
        var seen = new HashSet<uint>();
        var cur = w;
        while (cur.Owner is { } o && seen.Add(cur.Id) && windows.TryGetValue(o, out var parent)) cur = parent;
        return ReferenceEquals(cur, w) ? null : cur;
    }

    /// <summary>The windows that need a stream of their own: each that is shown on this PC.</summary>
    public IEnumerable<MacWindow> Streamed => All.Where(w => PresentationOf(w) != Presentation.None);

    /// <summary>A Mac menu is open (a window of the menu role, inside a picture or out of it).</summary>
    public bool HasOpenMenu => windows.Values.Any(w => w.Role == WindowState.Roles.Menu);

    /// <summary>The window a menu that is open belongs to, for the Esc that closes it.</summary>
    public uint? OpenMenuOwner => windows.Values.FirstOrDefault(w => w.Role == WindowState.Roles.Menu) is { } m ? (TopOwner(m)?.Id ?? m.Id) : null;
}
