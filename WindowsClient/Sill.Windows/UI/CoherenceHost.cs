using System.Diagnostics;
using System.Runtime.InteropServices;
using Sill.Windows.Coherence;
using Sill.Windows.Input;
using Sill.Windows.Net;
using Sill.Windows.Protocol;
using Sill.Windows.Shell;

namespace Sill.Windows.UI;

/// <summary>What Coherence enters with: windows (each shown once the Mac lists it), apps (whose windows
/// join by themselves, kind 29's `apps`), or every app's (`all`), and the window to have in front.</summary>
public sealed record CoherenceChoice(IReadOnlyList<uint> Windows, IReadOnlyList<string> Apps, bool All = false, uint? Focus = null);

/// <summary>Coherence on the UI thread: every streamed Mac window a Windows window of its own. It makes,
/// updates and closes the windows (<see cref="ProxyWindow"/>, <see cref="PopupWindow"/>) as kind 45 and
/// 41 say (through the <see cref="CoherenceController"/>, which decides what is sent), aims the mouse
/// and the keyboard at the right Mac window, gives each window its Mac app's taskbar identity and icon,
/// and keeps Sill itself in the notification area while the stream window is away (the windows, the
/// apps, leaving Coherence, disconnecting). The stream window owns it and its session.</summary>
sealed class CoherenceHost : IDisposable
{
    const ushort EscUsage = 0x29;
    const int DWMWA_CLOAKED = 14;

    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    readonly StreamForm form;
    readonly KeyboardForwarder keyboard;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer tick = new() { Interval = 50 };
    readonly System.Windows.Forms.Timer slow = new() { Interval = 1000 };
    readonly NotifyIcon tray;
    readonly ContextMenuStrip trayMenu = new() { Renderer = new DarkRenderer(), ShowImageMargin = true };
    readonly Dictionary<uint, ProxyWindow> proxies = new();
    readonly Dictionary<uint, PopupWindow> popups = new();
    readonly HashSet<uint> cloaked = new();
    readonly Dictionary<string, Cursor> cursors = new();
    readonly HashSet<string> shortcutsWaitingForIcon = new();
    (float hotX, float hotY, float width, float height, byte[] png)? cursorShape;
    Session? session;
    bool controllerAttached;
    int created;
    Point cascadeStart;
    Rectangle cascadeArea;
    uint? activateFirst;
    bool activateNext;
    long keyWindow = -1;
    // Pointer moves, at most one every 4 ms, the newest last (as the stream window sends them).
    readonly object moveGate = new();
    (uint id, double x, double y)? pendingMove;
    long lastMoveTicks;
    System.Threading.Timer? moveTimer;
    readonly Dictionary<uint, MouseButtons> buttonsDown = new();

    public CoherenceController Controller { get; }
    public MacMenuService Menus { get; }
    public bool Active => Controller.Active;
    /// <summary>The scale kind 29 says (this PC's pixels per point).</summary>
    public double Scale => Controller.Plan.Scale;
    public IReadOnlyCollection<ProxyWindow> Windows => proxies.Values;
    /// <summary>Coherence started or ended (the stream window's View item and button follow).</summary>
    public event Action? ActiveChanged;

    public CoherenceHost(StreamForm form, KeyboardForwarder keyboard, MacMenuService menus)
    {
        this.form = form;
        this.keyboard = keyboard;
        Menus = menus;
        Controller = new CoherenceController(() => clock.Elapsed.TotalSeconds);
        Controller.Note += line => TestArguments.Log(line);
        tray = new NotifyIcon { Icon = Theme.AppIcon, Text = "Sill", ContextMenuStrip = trayMenu, Visible = false };
        trayMenu.Opening += (_, _) => BuildTrayMenu();
        tray.DoubleClick += (_, _) => BringAllForward();
        tick.Tick += (_, _) => Controller.Tick();
        slow.Tick += (_, _) => SlowTick();
        menus.TopChanged += () => { foreach (var p in proxies.Values) p.RefreshMenus(); };
    }

    double Now => clock.Elapsed.TotalSeconds;

    // MARK: the session

    /// <summary>A session for the stream window (the first, or a reconnect): its kinds for Coherence are
    /// routed here; in Coherence the set is asked for again at its first window list (after its hello).</summary>
    public void Attach(Session next)
    {
        session = next;
        controllerAttached = false;
        if (Active) next.Coherent = true;
        next.SideStreamReceived += (kind, payload, key, ts) => { if (ReferenceEquals(session, next)) Controller.Route(kind, payload, key, ts); };
        next.WindowStateReceived += m => Ui(next, () => Apply(m));
        next.TakenBackReceived += m => Ui(next, () => TakenBack(m));
        next.CursorChanged += c => Ui(next, () => { cursorShape = c; ApplyCursors(); });
        next.WindowsChanged += () => Ui(next, OnWindows);
        next.AppsChanged += () => Ui(next, OnApps);
        next.Ended += e => Ui(next, () => OnEnded(e));
        next.FiguresChanged += () => { if (ReferenceEquals(session, next)) Controller.WorstRttMs = next.WorstRttMs; };
    }

    void Ui(Session s, Action a)
    {
        if (!ReferenceEquals(session, s) || form.IsDisposed || !form.IsHandleCreated) return;
        try { form.BeginInvoke(() => { if (ReferenceEquals(session, s)) a(); }); }
        catch (InvalidOperationException) { }
    }

    void OnWindows()
    {
        if (session is not { } s) return;
        Controller.WindowInput = s.Windows?.WindowInput;
        if (!controllerAttached)
        {
            // After the hello (this list answers it): a reconnect in Coherence asks for the set again here.
            controllerAttached = true;
            Controller.Attach(s);
            if (Active) TestArguments.Log("coherence: the set asked for again on the new session");
        }
        tray.Text = Clip($"Sill — {s.MacName} (Coherence)");
    }

    static string Clip(string s) => s.Length > 120 ? s[..120] : s;

    void OnEnded(SessionEnd e)
    {
        if (!Active) return;
        if (e.Reconnect) tray.ShowBalloonTip(3000, "Sill", $"Lost the connection to {session?.MacName}. Reconnecting…", ToolTipIcon.None);
    }

    // MARK: entering and leaving

    /// <summary>Starts Coherence; false when the Mac cannot (the stream window stays).</summary>
    public bool Enter(CoherenceChoice choice)
    {
        if (session is not { CanCoherence: true } s || Active) return false;
        if (!controllerAttached) { controllerAttached = true; Controller.Attach(s); }
        s.Coherent = true;
        Controller.WindowInput = s.Windows?.WindowInput;
        UpdateScale(form.DeviceDpi / 96.0);
        var screen = Screen.FromControl(form);
        Controller.SetRates(SideStreamRequest.MaxFPS, Math.Clamp(Displays.RefreshRate(screen), 30, SideStreamRequest.MaxFocusFPS));
        var (sw, sh) = Displays.LargestInPoints();
        Controller.SetScreen(sw, sh);
        cascadeArea = TestArguments.Offscreen ? new Rectangle(-6000, 0, 5000, 3000) : screen.WorkingArea;
        var origin = form.WindowState == FormWindowState.Normal ? form.Location : form.RestoreBounds.Location;
        cascadeStart = TestArguments.Offscreen ? new Point(-6000, 80) : origin;
        created = 0;
        uint? focus = choice.Focus is { } f && f != 0 ? f : choice.Windows.Count > 0 ? choice.Windows[0] : null;
        activateFirst = focus;
        activateNext = focus == null;
        Controller.Enter(choice.Windows, choice.Apps, choice.All, focus);
        // Nothing is picked in Coherence: the windows stream as side streams.
        s.Select(Source.None);
        tray.Text = Clip($"Sill — {s.MacName} (Coherence)");
        tray.Visible = true;
        tick.Start();
        slow.Start();
        var apps = choice.Apps.Concat(choice.Windows.Select(id => s.Windows?.Windows.FirstOrDefault(w => w.Id == id)?.BundleID).OfType<string>())
                              .Where(a => a.Length > 0).Distinct().ToList();
        CoherenceStore.Current.LastApps[s.MacKey] = apps;
        CoherenceStore.Save();
        TestArguments.Log($"coherence: entered with windows [{string.Join(",", choice.Windows)}] apps [{string.Join(",", choice.Apps)}]{(choice.All ? " all" : "")}");
        ActiveChanged?.Invoke();
        return true;
    }

    /// <summary>Ends Coherence: every window closes, and the stream window gets the window that was in
    /// front (or the Desktop) when <paramref name="pickForStreamWindow"/>.</summary>
    public void Leave(bool pickForStreamWindow)
    {
        if (!Active) return;
        var focus = Controller.Plan.Focus;
        LetGoAll();
        Controller.Leave();
        foreach (var p in popups.Values.ToList()) p.ForceClose();
        foreach (var p in proxies.Values.Where(p => p.Owned).ToList()) p.ForceClose();
        foreach (var p in proxies.Values.ToList()) p.ForceClose();
        popups.Clear();
        proxies.Clear();
        cloaked.Clear();
        tray.Visible = false;
        tick.Stop();
        slow.Stop();
        if (session is { } s)
        {
            s.Coherent = false;
            if (pickForStreamWindow)
                s.Select(focus is { } f && s.Windows?.Windows.Any(w => w.Id == f) == true ? Source.Window(f) : Source.Desktop);
        }
        TestArguments.Log("coherence: left");
        ActiveChanged?.Invoke();
    }

    // MARK: kind 45 and 41

    void Apply(WindowState m)
    {
        var u = Controller.Apply(m);
        if (!u.Applied) return;
        if (u.Any) TestArguments.Log($"coherence: kind 45 gen {m.Gen}{(u.Full ? " full" : "")}: +{u.Added.Count} ~{u.Changed.Count} -{u.Removed.Count}{(u.Answering is { } a ? $" answering {a}" : "")}");
        Reconcile(u);
    }

    void TakenBack(TakenBack m)
    {
        var titles = m.Windows.Select(id => Controller.Set[id]?.DisplayTitle).OfType<string>().ToList();
        var u = Controller.TakenBack(m);
        if (!u.Applied) return;
        Reconcile(u);
        if (titles.Count > 0) tray.ShowBalloonTip(3000, "Sill", $"Taken back on the Mac: {string.Join(", ", titles)}", ToolTipIcon.None);
    }

    static int Depth(WindowSet set, MacWindow w)
    {
        int d = 0;
        var seen = new HashSet<uint>();
        for (var cur = w; cur.Owner is { } o && seen.Add(cur.Id) && set[o] is { } parent; cur = parent) d++;
        return d;
    }

    /// <summary>The windows here made to match the set: those gone closed, those new made (owners first),
    /// those changed told, children placed by their owners.</summary>
    void Reconcile(WindowSetUpdate u)
    {
        var set = Controller.Set;
        foreach (var (id, p) in proxies.ToList())
            if (set[id] is not { } w || set.PresentationOf(w) is not (Presentation.Window or Presentation.OwnedWindow)) { proxies.Remove(id); p.ForceClose(); }
        foreach (var (id, p) in popups.ToList())
            if (set[id] is not { } w || set.PresentationOf(w) != Presentation.Popup) { popups.Remove(id); p.ForceClose(); }
        foreach (var w in set.All.OrderBy(w => Depth(set, w)).ToList())
        {
            var presentation = set.PresentationOf(w);
            if (presentation is Presentation.Window or Presentation.OwnedWindow && !proxies.ContainsKey(w.Id)) CreateProxy(w, presentation);
            else if (presentation == Presentation.Popup && !popups.ContainsKey(w.Id)) CreatePopup(w);
        }
        foreach (var (w, change) in u.Changed)
        {
            if (proxies.TryGetValue(w.Id, out var p)) p.Update(w, change, u.Answering);
            else if (popups.TryGetValue(w.Id, out var pp)) pp.Update(w);
        }
        if (u.Answering is { } token && Controller.WindowOfToken(token) is { } answered && proxies.TryGetValue(answered, out var ap)
            && !u.Changed.Any(c => c.window.Id == answered))
            ap.SyncToMac(token);
        foreach (var p in proxies.Values.Where(p => !p.Owned).ToList()) LayoutChildren(p);
        if (u.Added.Count > 0) ApplyCursors();
    }

    void CreateProxy(MacWindow w, Presentation presentation)
    {
        var set = Controller.Set;
        ProxyWindow? owner = null;
        if (presentation == Presentation.OwnedWindow)
        {
            if (set.TopOwner(w) is not { } top || !proxies.TryGetValue(top.Id, out owner)) return;
        }
        bool ours = Form.ActiveForm is MacWindowForm;
        bool activate = activateFirst == w.Id || activateNext || (owner != null && ours) || (w.Focused && ours && w.Joined);
        if (activate) { activateFirst = null; activateNext = false; }
        var p = new ProxyWindow(this, w, owner, activate);
        if (owner == null)
        {
            var step = (int)Math.Round(32 * Scale);
            p.Location = ProxyGeometry.Cascade(created++, cascadeStart, p.Size, cascadeArea, step);
        }
        else p.Location = owner.Location + new Size(40, 60);
        proxies[w.Id] = p;
        TestArguments.Log($"coherence: window {w.Id} \"{w.DisplayTitle}\" ({w.Role}{(owner != null ? $", owned by {owner.Id}" : "")}) {(activate ? "shown in front" : "shown")}");
        p.Show();
        if (owner != null) LayoutChildren(owner);
    }

    void CreatePopup(MacWindow w)
    {
        if (Controller.Set.TopOwner(w) is not { } top || !proxies.TryGetValue(top.Id, out var owner)) return;
        var p = new PopupWindow(this, w, owner);
        popups[w.Id] = p;
        if (ChildBounds(w, owner) is { } r) p.Bounds = r;
        TestArguments.Log($"coherence: popup {w.Id} ({w.Role}) over window {owner.Id} at {p.Bounds}");
        p.Show();
    }

    /// <summary>Where a child goes: its `rect` from its direct owner's top-left on screen (a window's: its
    /// surface's less the cropped title bar; a popup's: its own), at its top owner's scale, kept on screen.</summary>
    Rectangle? ChildBounds(MacWindow child, ProxyWindow topOwner)
    {
        if (child.Rect is not { Length: >= 4 } rect || child.Owner is not { } ownerId) return null;
        Point origin;
        if (proxies.TryGetValue(ownerId, out var direct) && direct.IsHandleCreated)
            origin = ProxyGeometry.WindowOrigin(direct.Surface.PointToScreen(Point.Empty), direct.State.TitleBar, direct.Dpi);
        else if (popups.TryGetValue(ownerId, out var parent))
            origin = parent.Location;
        else return null;
        var r = ProxyGeometry.ChildBounds(origin, rect, topOwner.Dpi);
        return TestArguments.Offscreen ? r : ProxyGeometry.KeepWithin(r, Screen.FromRectangle(r).WorkingArea);
    }

    /// <summary>The children of a window go where its Mac window has them, as it moves and resizes:
    /// popups and the sheets and dialogs that spill out (each surface at its rect).</summary>
    public void LayoutChildren(ProxyWindow owner)
    {
        if (!Active) return;
        var set = Controller.Set;
        foreach (var w in set.All.Where(w => w.Owner != null).OrderBy(w => Depth(set, w)))
        {
            if (set.TopOwner(w)?.Id != owner.Id || ChildBounds(w, owner) is not { } r) continue;
            if (popups.TryGetValue(w.Id, out var popup)) popup.Place(r);
            else if (proxies.TryGetValue(w.Id, out var owned) && owned.IsHandleCreated && !owned.InSizeMove)
            {
                // The owned window's surface (its picture) goes at the rect; its frame around it.
                var surfaceAt = owned.Surface.PointToScreen(Point.Empty);
                owned.Location = new Point(r.X - (surfaceAt.X - owned.Location.X), r.Y - (surfaceAt.Y - owned.Location.Y));
            }
        }
    }

    /// <summary>A window or popup closed: forgotten here.</summary>
    public void Closed(MacWindowForm f)
    {
        if (f is ProxyWindow p && proxies.TryGetValue(p.Id, out var known) && ReferenceEquals(known, p)) proxies.Remove(p.Id);
        if (f is PopupWindow pp && popups.TryGetValue(pp.Id, out var kp) && ReferenceEquals(kp, pp)) popups.Remove(pp.Id);
        if (f.IsHandleCreated && keyboard.Window == f.Handle) keyboard.HasKeyboard = false;
        lock (buttonsDown) buttonsDown.Remove(f.Id);
    }

    // MARK: focus and the keyboard

    public void ProxyActivated(ProxyWindow p)
    {
        if (!Active || !proxies.ContainsKey(p.Id)) return;
        Controller.Activated(p.Id);
        UpdateScale(null);
        KeyboardChanged();
        form.CoherenceWindowActivated();
    }

    public void ProxyDeactivated(ProxyWindow p)
    {
        if (p.IsHandleCreated && keyboard.Window == p.Handle)
        {
            keyboard.HasKeyboard = false;
            keyboard.ReleaseAll();
        }
        LetGoButtons(p.Id);
    }

    /// <summary>The keyboard goes to the Mac while a window of Coherence is the foreground window (the
    /// hook checks it at each key) and its menu strip is not open; to the window in front's Mac window.</summary>
    public void KeyboardChanged()
    {
        if (Form.ActiveForm is ProxyWindow p && proxies.TryGetValue(p.Id, out var known) && ReferenceEquals(known, p))
        {
            keyboard.Window = p.Handle;
            keyboard.HasKeyboard = !p.MenuOpen;
            Interlocked.Exchange(ref keyWindow, p.Id);
            if (p.MenuOpen) keyboard.ReleaseAll();
        }
    }

    /// <summary>A key event from the hook's thread, aimed at the window in front.</summary>
    public byte[] AimKey(byte[] json)
    {
        long id = Interlocked.Read(ref keyWindow);
        return id < 0 ? json : CoherenceController.Aim(json, (uint)id, InputKind.Key, Controller.WindowInput, focused: true).payload;
    }

    /// <summary>Sill stopped being the active app (WM_ACTIVATEAPP): a Mac menu open would never close by
    /// itself, so the Mac gets Esc; nothing stays held on it.</summary>
    public void AppDeactivated()
    {
        if (!Active || session is not { } s) return;
        keyboard.ReleaseAll();
        if (!Controller.Set.HasOpenMenu || Controller.Set.OpenMenuOwner is not { } owner) return;
        foreach (var down in new[] { true, false })
            s.SendInput(CoherenceController.Aim(InputEvents.Key(EscUsage, down, 0), owner, InputKind.Key, Controller.WindowInput, focused: true).payload);
        TestArguments.Log($"coherence: Sill went to the background with a Mac menu open; Esc to window {owner}");
    }

    // MARK: the mouse

    public void Pointer(MacWindowForm src, PointerAction action, double x, double y)
    {
        if (session is not { } s || !Active) return;
        if (action == PointerAction.Move)
        {
            lock (moveGate)
            {
                long now = Stopwatch.GetTimestamp();
                if (now - lastMoveTicks >= Stopwatch.Frequency / 250 && pendingMove is null)
                {
                    lastMoveTicks = now;
                    SendAimed(s, src.Id, InputEvents.Pointer(PointerAction.Move, x, y), InputKind.Move);
                }
                else
                {
                    pendingMove = (src.Id, x, y);
                    moveTimer ??= new System.Threading.Timer(_ => FlushMove());
                    moveTimer.Change(5, Timeout.Infinite);
                }
            }
            return;
        }
        FlushMove();
        lock (buttonsDown)
        {
            var held = buttonsDown.GetValueOrDefault(src.Id);
            held = action switch
            {
                PointerAction.LeftDown => held | MouseButtons.Left, PointerAction.LeftUp => held & ~MouseButtons.Left,
                PointerAction.RightDown => held | MouseButtons.Right, PointerAction.RightUp => held & ~MouseButtons.Right, _ => held,
            };
            buttonsDown[src.Id] = held;
        }
        SendAimed(s, src.Id, InputEvents.Pointer(PointerAction.Move, x, y), InputKind.Move);
        SendAimed(s, src.Id, InputEvents.Pointer(action, x, y), InputKind.Button);
    }

    public void FlushMove()
    {
        lock (moveGate)
        {
            if (pendingMove is not { } p || session is not { } s) { pendingMove = null; return; }
            pendingMove = null;
            lastMoveTicks = Stopwatch.GetTimestamp();
            SendAimed(s, p.id, InputEvents.Pointer(PointerAction.Move, p.x, p.y), InputKind.Move);
        }
    }

    public void Wheel(MacWindowForm src, double x, double y, double notchesX, double notchesY)
    {
        if (session is not { } s || !Active) return;
        var (w, h) = src.MacPoints ?? (800, 600);
        double points = Net.Settings.Current.ScrollPoints;
        // Natural sign, as the stream window: a wheel turned away from the user moves the content down.
        double dy = notchesY * points / h, dx = -notchesX * points / w;
        FlushMove();
        SendAimed(s, src.Id, InputEvents.Scroll(x, y, dx, dy), InputKind.Scroll);
    }

    void SendAimed(Session s, uint id, byte[] json, InputKind kind)
    {
        var (payload, activateFirst) = CoherenceController.Aim(json, id, kind, Controller.WindowInput, Controller.Plan.Focus == id);
        if (activateFirst) s.WindowCommand(id, WindowActions.Activate);
        s.SendInput(payload);
    }

    void LetGoButtons(uint id)
    {
        MouseButtons held;
        lock (buttonsDown) { held = buttonsDown.GetValueOrDefault(id); buttonsDown.Remove(id); }
        if (session is not { } s || held == MouseButtons.None) return;
        if (held.HasFlag(MouseButtons.Left)) SendAimed(s, id, InputEvents.Pointer(PointerAction.LeftUp, 0.5, 0.5), InputKind.Button);
        if (held.HasFlag(MouseButtons.Right)) SendAimed(s, id, InputEvents.Pointer(PointerAction.RightUp, 0.5, 0.5), InputKind.Button);
    }

    void LetGoAll()
    {
        keyboard.HasKeyboard = false;
        keyboard.ReleaseAll();
        List<uint> ids;
        lock (buttonsDown) ids = buttonsDown.Keys.ToList();
        foreach (var id in ids) LetGoButtons(id);
    }

    // MARK: the cursor, the scale, hidden windows

    /// <summary>The Mac's cursor shape (kind 14) on every window, at each window's points to pixels.</summary>
    void ApplyCursors()
    {
        if (cursorShape is not { } c) return;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(c.png));
        foreach (var f in proxies.Values.Cast<MacWindowForm>().Concat(popups.Values))
        {
            double ppp = f.MacPoints is { } pt && pt.w > 0 && f.Surface.ClientSize.Width > 0 ? f.Surface.ClientSize.Width / pt.w : f.Dpi;
            var key = $"{hash}/{ppp:0.00}";
            if (!cursors.TryGetValue(key, out var cursor))
            {
                cursor = MacCursor.Make(c.png, c.hotX, c.hotY, c.width, c.height, (float)ppp) ?? Cursors.Default;
                if (cursors.Count > 64) cursors.Clear();
                cursors[key] = cursor;
            }
            f.Surface.MacCursor = cursor;
        }
    }

    /// <summary>The scale kind 29 asks the Mac to capture at: the highest DPI scale among the windows
    /// shown, so each is as sharp as its monitor can show it.</summary>
    void UpdateScale(double? fallback)
    {
        var shown = proxies.Values.Where(p => p.IsHandleCreated && p.WindowState != FormWindowState.Minimized).Select(p => p.Dpi).ToList();
        double scale = shown.Count > 0 ? shown.Max() : fallback ?? Scale;
        Controller.SetScale(scale);
    }

    public void DpiChanged(ProxyWindow p)
    {
        UpdateScale(null);
        ApplyCursors();
        TestArguments.Log($"coherence: window {p.Id} on a {p.DeviceDpi} DPI monitor; scale {Scale:0.##}");
    }

    public double ScaleAt(Point p) => Displays.ScaleAt(p);

    public void HiddenChanged(ProxyWindow p)
    {
        bool hidden = p.WindowState == FormWindowState.Minimized || cloaked.Contains(p.Id);
        Controller.SetMinimized(p.Id, hidden);
        UpdateScale(null);
        TestArguments.Log($"coherence: window {p.Id} {(hidden ? "hidden" : "shown again")}");
    }

    void SlowTick()
    {
        // A window on another virtual desktop is cloaked: hidden as a minimized one is.
        foreach (var p in proxies.Values.Where(p => p.IsHandleCreated).ToList())
        {
            bool isCloaked = DwmGetWindowAttribute(p.Handle, DWMWA_CLOAKED, out int c, 4) >= 0 && c != 0;
            if (isCloaked == cloaked.Contains(p.Id)) continue;
            if (isCloaked) cloaked.Add(p.Id); else cloaked.Remove(p.Id);
            HiddenChanged(p);
        }
        var (w, h) = Displays.LargestInPoints();
        Controller.SetScreen(w, h);
        if (TestArguments.LogFile != null)
        {
            var (counts, dropped) = Controller.TakeFrameCounts();
            if (counts.Count > 0 || dropped > 0)
                TestArguments.Log($"coherence: frames {string.Join(" ", counts.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"))}{(dropped > 0 ? $" (no window: {dropped})" : "")}");
        }
    }

    // MARK: icons and the taskbar

    string? IconPathFor(string? bundleID)
    {
        if (bundleID == null || session is not { } s) return null;
        byte[]? png;
        lock (s.Icons) s.Icons.TryGetValue(bundleID, out png);
        return png == null ? null : IconCache.IcoFor(bundleID, png);
    }

    public Icon? IconFor(string? bundleID) => IconPathFor(bundleID) is { } path ? IconCache.Load(path) : null;

    /// <summary>A window's AppUserModelID (`Sill.<macKey>.<bundleID>`) and relaunch properties, before it shows.</summary>
    public void SetTaskbarIdentity(ProxyWindow p)
    {
        if (session is not { } s) return;
        var bundle = p.State.App;
        var key = s.MacKey;
        var aumid = Aumid.For(key, bundle ?? "app");
        string? command = null, name = null;
        if (bundle != null && LaunchRequest.ValidBundleID(bundle) && Environment.ProcessPath is { } exe)
        {
            command = Aumid.RelaunchCommand(exe, key, bundle);
            name = Aumid.DisplayName(p.State.AppName ?? bundle, s.MacName);
        }
        bool ok = TaskbarIdentity.Set(p.Handle, aumid, command, name, IconPathFor(bundle));
        TestArguments.Log($"coherence: window {p.Id} AppUserModelID {aumid}{(ok ? "" : " (the shell gave no property store)")}");
    }

    void OnApps()
    {
        // Icons that came since: each window of that app takes its own, and its pinned button too.
        foreach (var p in proxies.Values)
        {
            if (p.State.App is not { } app || IconPathFor(app) is not { } path || IconCache.Load(path) is not { } icon || ReferenceEquals(p.Icon, icon)) continue;
            p.Icon = icon;
            if (p.IsHandleCreated) TaskbarIdentity.SetIcon(p.Handle, path);
        }
        foreach (var bundle in shortcutsWaitingForIcon.ToList())
        {
            if (IconPathFor(bundle) is not { } path) continue;
            shortcutsWaitingForIcon.Remove(bundle);
            if (CoherenceStore.Current.Recent.FirstOrDefault(r => r.BundleID == bundle && r.MacKey == session?.MacKey) is { } app) MakeShortcut(app, path);
        }
    }

    /// <summary>A Mac app launched from Sill: first in the jump list's recent apps, and a Start menu
    /// shortcut (once its icon has come).</summary>
    public void RememberLaunch(string bundleID, string appName)
    {
        if (session is not { } s || !LaunchRequest.ValidBundleID(bundleID)) return;
        var app = new RecentApp { MacKey = s.MacKey, MacName = s.MacName, BundleID = bundleID, AppName = appName, LastUsed = DateTimeOffset.Now };
        var store = CoherenceStore.Current;
        store.Recent = RecentApp.Touch(store.Recent, app);
        CoherenceStore.Save();
        JumpList.Update(store.Recent, r => r.MacKey == s.MacKey ? IconPathFor(r.BundleID) : null);
        if (IconPathFor(bundleID) is { } icon) MakeShortcut(app, icon);
        else shortcutsWaitingForIcon.Add(bundleID);
    }

    static void MakeShortcut(RecentApp app, string icon)
    {
        if (!CoherenceStore.Current.StartMenuShortcuts) return;
        if (StartMenu.Make(app, icon) is { } path) TestArguments.Log($"coherence: Start menu shortcut {path}");
    }

    /// <summary>Opens a Mac app and shows its windows (kind 7, and kind 29's `apps`).</summary>
    public void LaunchApp(string bundleID, string appName)
    {
        if (session is not { } s) return;
        s.Launch(bundleID);
        Controller.AddApp(bundleID);
        RememberLaunch(bundleID, appName);
    }

    // MARK: the notification area

    void BuildTrayMenu()
    {
        trayMenu.Items.Clear();
        var s = session;
        ToolStripMenuItem Item(string text, Action? act, bool enabled = true, bool check = false) =>
            new(MenuText(text), null, act == null ? null : (_, _) => act()) { Enabled = enabled, Checked = check, ForeColor = enabled ? Theme.Text : Theme.SecondaryText };
        trayMenu.Items.Add(Item($"{s?.MacName ?? "Mac"} — Coherence", null, enabled: false));
        trayMenu.Items.Add(new ToolStripSeparator());
        foreach (var p in proxies.Values.Where(p => !p.Owned).OrderBy(p => p.State.AppName).ThenBy(p => p.Text))
        {
            var win = p;
            var item = Item($"{p.State.AppName ?? p.State.App} — {p.State.DisplayTitle}", () => BringForward(win), check: Controller.Plan.Focus == p.Id);
            item.Image = p.Icon?.ToBitmap();
            trayMenu.Items.Add(item);
        }
        if (proxies.Count == 0) trayMenu.Items.Add(Item("No Mac windows shown yet", null, enabled: false));
        var others = s?.Windows?.Windows.Where(w => !Controller.Set.Contains(w.Id)).ToList() ?? new();
        var add = Item("Show Another Window", null, enabled: others.Count > 0);
        foreach (var w in others)
        {
            var id = w.Id;
            add.DropDownItems.Add(Item(string.IsNullOrWhiteSpace(w.Title) ? w.AppName : $"{w.AppName} — {w.Title}", () => Controller.Add(id)));
        }
        trayMenu.Items.Add(add);
        var appsItem = Item("Open an App", null, enabled: (s?.Apps.Count ?? 0) > 0);
        foreach (var a in (s?.Apps ?? new()).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var app = a;
            appsItem.DropDownItems.Add(Item(app.Name, () => LaunchApp(app.BundleID, app.Name)));
        }
        trayMenu.Items.Add(appsItem);
        var stop = Item("Stop Showing", null, enabled: proxies.Count > 0);
        foreach (var p in proxies.Values.Where(p => !p.Owned))
        {
            var id = p.Id;
            stop.DropDownItems.Add(Item(p.State.DisplayTitle, () => StopShowing(id)));
        }
        trayMenu.Items.Add(stop);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(Item("Leave Coherence", () => form.LeaveCoherence()));
        trayMenu.Items.Add(Item("Disconnect", () => form.Close()));
        foreach (var dd in trayMenu.Items.OfType<ToolStripMenuItem>())
            if (dd.DropDown is ToolStripDropDownMenu m) m.Renderer = new DarkRenderer();
    }

    static string MenuText(string s) => Sill.Windows.Menus.MenuShortcut.MenuText(SafeText.Label(s, 80));

    void StopShowing(uint id)
    {
        var u = Controller.StopShowing(id);
        Reconcile(u);
    }

    public void BringForward(ProxyWindow p)
    {
        if (p.WindowState == FormWindowState.Minimized) p.WindowState = FormWindowState.Normal;
        p.Activate();
    }

    public void BringAllForward()
    {
        foreach (var p in proxies.Values.Where(p => !p.Owned).ToList())
            if (p.WindowState == FormWindowState.Minimized) p.WindowState = FormWindowState.Normal;
        if (Controller.Plan.Focus is { } f && proxies.TryGetValue(f, out var front)) front.Activate();
        else proxies.Values.FirstOrDefault()?.Activate();
    }

    /// <summary>Sill's shortcuts in Coherence: Minimize and Always on Top act on the window in front,
    /// Windows' menu (Ctrl+Alt+Shift+W) is the notification area's; those of the stream window alone
    /// (full screen, the Desktop, a screenshot, stats) do nothing. False: the stream window's own.</summary>
    public bool Hotkey(Keys key)
    {
        if (!Active) return false;
        var front = Form.ActiveForm as ProxyWindow;
        switch (key)
        {
            case Keys.M: if (front != null) front.WindowState = FormWindowState.Minimized; return true;
            case Keys.T: if (front != null) front.TopMost = !front.TopMost; return true;
            case Keys.W: trayMenu.Show(Cursor.Position); return true;
            case Keys.F: case Keys.D: case Keys.P: case Keys.S: return true;
            default: return false;
        }
    }

    /// <summary>TEST (--actions): Coherence's actions, only with a loopback test host (as the others).</summary>
    public bool RunAction(string name)
    {
        var first = proxies.Values.Where(p => !p.Owned).OrderBy(p => p.Id).FirstOrDefault();
        switch (name)
        {
            case "leave": form.LeaveCoherence(); return true;
            case "resize" when first != null:
                first.Size += new Size(120, 80);
                return true;
            case "drag" when first != null:
                // A drag as Windows sends it: sizes inside WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE.
                first.BeginInvoke(async () =>
                {
                    SendMessage(first.Handle, 0x231, IntPtr.Zero, IntPtr.Zero);
                    for (int i = 1; i <= 10; i++) { first.Size += new Size(10, 6); await Task.Delay(16); }
                    SendMessage(first.Handle, 0x232, IntPtr.Zero, IntPtr.Zero);
                });
                return true;
            case "maximize" when first != null: first.WindowState = FormWindowState.Maximized; return true;
            case "restore" when first != null: first.WindowState = FormWindowState.Normal; return true;
            case "minimize" when first != null: first.WindowState = FormWindowState.Minimized; return true;
            case "close" when first != null: first.Close(); return true;
            case "activate":
                var next = proxies.Values.Where(p => !p.Owned).OrderBy(p => p.Id).SkipWhile(p => p.Id != (Controller.Plan.Focus ?? 0)).Skip(1).FirstOrDefault() ?? first;
                if (next != null) { next.Activate(); Controller.Activated(next.Id); }
                return true;
            case "popup":
                if (session is { } s && Controller.Plan.Focus is { } f)
                    foreach (var down in new[] { true, false })
                        s.SendInput(CoherenceController.Aim(InputEvents.Key(0x13, down, 0), f, InputKind.Key, Controller.WindowInput, true).payload);   // P
                return true;
            case "takeback":
                if (session is { } ts && Controller.Plan.Focus is { } tf)
                    foreach (var down in new[] { true, false })
                        ts.SendInput(CoherenceController.Aim(InputEvents.Key(0x17, down, 0), tf, InputKind.Key, Controller.WindowInput, true).payload);   // T
                return true;
            case "deactivate": AppDeactivated(); return true;
            case "menu" when first != null: MacMenuService.OpenFirst(first.Strip); return true;
            case "stop" when first != null: StopShowing(first.Id); return true;
            case "add":
                if (session?.Windows?.Windows.FirstOrDefault(w => !Controller.Set.Contains(w.Id)) is { } other) Controller.Add(other.Id);
                return true;
            case "launch": LaunchApp("com.apple.Safari", "Safari"); return true;
            case "proxies":
                foreach (var p in proxies.Values)
                    TestArguments.Log($"proxy {p.Id} \"{p.Text}\" bounds {p.Bounds} surface {p.SurfaceSize} picture {p.Picture.w}x{p.Picture.h} crop {p.Crop} dpi {p.DeviceDpi} state {p.WindowState} strip {p.Strip.Visible}:{p.Strip.Items.Count} owned {p.Owned} icon {(p.Icon == Theme.AppIcon ? "Sill's" : "the app's")}");
                foreach (var p in popups.Values) TestArguments.Log($"popup {p.Id} bounds {p.Bounds} picture {p.Picture.w}x{p.Picture.h}");
                TestArguments.Log($"coherence: kind 29 now {System.Text.Encoding.UTF8.GetString(Json.Encode(Controller.Request))}");
                return true;
        }
        return false;
    }

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    public void Dispose()
    {
        Leave(pickForStreamWindow: false);
        tick.Dispose();
        slow.Dispose();
        tray.Visible = false;
        tray.Dispose();
        trayMenu.Dispose();
        moveTimer?.Dispose();
    }
}
