using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sill.Windows.Protocol;

namespace Sill.TestHost;

// TEST ONLY: the stand-in Mac's Coherence (docs/coherence-protocol.md), so the Windows client's
// Coherence can be tried on a PC with no Mac. Not the host's design (no capture, no desk): each window
// is a test pattern of its own, drawn at the size the client's scale asks, its picture with a fake
// 28 pt title bar the client crops.
sealed partial class HostSession
{
    const double TitleBar = 28;

    /// <summary>A window of the stand-in Mac, in points.</summary>
    internal sealed class TestWindow
    {
        public uint Id;
        /// <summary>When it joined by itself (kind 29's apps): a request that crossed its kind 45 keeps it.</summary>
        public long JoinedAt;
        public string App = "", AppName = "", Title = "", Role = WindowState.Roles.Window;
        public double W, H;
        public Color Color;
        public uint? Owner;
        public double[]? Rect;
        public bool InCrop = true, Joined, Edited;
        public double[] Min = { 320, 200 }, Max = { 2400, 1600 };
        public bool IsChild => Owner != null;
        public double TitleBarPt => IsChild ? 0 : TitleBar;
    }

    Hello? deviceHello;
    readonly object coGate = new();
    readonly Dictionary<uint, TestWindow> coWindows = new();
    readonly Dictionary<uint, WindowStream> streams = new();
    readonly List<WindowInfo> launched = new();
    bool coActive;
    long coGen;
    uint coFocus;
    double coScale = 1;
    int fpsOthers = SideStreamRequest.DefaultFPS, fpsFocus = SideStreamRequest.DefaultFocusFPS;
    HashSet<uint> coHidden = new();
    List<string> coApps = new();
    bool coAll;
    uint nextId = 110, nextPopup = 190;
    // The Mac's menus: the app in front's, one version per app change.
    int menuVersion = 1;
    bool menusSubscribed;
    string menuApp = "";

    static readonly Color[] Palette = { Color.SteelBlue, Color.DarkOrange, Color.SeaGreen, Color.MediumPurple, Color.IndianRed, Color.Goldenrod, Color.Teal };

    IEnumerable<WindowInfo> LaunchedWindows() { lock (coGate) return launched.ToList(); }

    void AddCoherenceCapabilities(JsonObject list)
    {
        if (!Program.Coherence) return;
        list["coherence"] = Capabilities.Coherence;
        list["sideStreams"] = 6;
        list["windowInput"] = Capabilities.WindowInputEvery;
        list["iconSize"] = 512;
    }

    /// <summary>Kind 4 for each app, at the size the hello asks (64 before Hello.iconSize).</summary>
    async Task SendIcons()
    {
        int size = Math.Clamp(deviceHello?.IconSize ?? 64, 16, 512);
        foreach (var (bundle, name, color) in new[] { ("com.apple.TextEdit", "T", Color.SteelBlue), ("com.apple.finder", "F", Color.DarkOrange), ("com.apple.Safari", "S", Color.SeaGreen) })
        {
            var png = TestPictures.Icon(size, color, name);
            var id = Encoding.UTF8.GetBytes(bundle);
            var blob = new byte[2 + id.Length + png.Length];
            blob[0] = (byte)(id.Length >> 8); blob[1] = (byte)id.Length;
            id.CopyTo(blob, 2);
            png.CopyTo(blob, 2 + id.Length);
            await Send(Kind.AppIcon, blob);
        }
    }

    /// <summary>The kinds Coherence adds, and those it changes while it runs. True: handled here.</summary>
    bool HandleCoherence(Header h, byte[] p)
    {
        var text = Encoding.UTF8.GetString(p);
        switch ((Kind)h.Kind)
        {
            case Kind.SideStreams:
                Program.Say($"{who}: kind 29 {text}");
                OnSideStreams(p);
                return true;
            case Kind.PlaceWindow:
                Program.Say($"{who}: kind 46 {text}");
                OnPlaceWindow(p);
                return true;
            case Kind.FetchMenu:
                Program.Say($"{who}: kind 27 {text}");
                OnFetchMenu(p);
                return true;
            case Kind.PressMenuItem:
                Program.Say($"{who}: kind 25 {text}");
                OnPressMenu(p);
                return true;
            case Kind.WindowCommand when coActive:
                Program.Say($"{who}: kind 15 {text}");
                if (Json.Decode<WindowCommand>(p) is { } c) OnWindowCommand(c);
                return true;
            case Kind.LaunchApp when coActive:
                Program.Say($"{who}: kind 7 {text}");
                if (Json.Decode<LaunchApp>(p) is { } l) OnLaunch(l.BundleID);
                return true;
            case Kind.KeyframeRequest when p.Length > 0:
                if (KeyframeRequest.Parse(p)?.Window is { } kw)
                {
                    Program.Say($"{who}: keyframe request for window {kw}");
                    lock (coGate) if (streams.TryGetValue(kw, out var ks)) ks.Keyframe = true;
                    return true;
                }
                return false;
            case Kind.Input when coActive:
                OnWindowInput(p);
                return false;   // printed (and drawn on the Desktop's pattern) as before
            default:
                return false;
        }
    }

    // MARK: kind 29

    void OnSideStreams(byte[] p)
    {
        if (Json.Decode<SideStreamRequest>(p) is not { } req) return;
        lock (coGate)
        {
            if (req.Coherence != true)
            {
                if (!coActive) return;
                coActive = false;
                foreach (var s in streams.Values) s.Stop();
                streams.Clear();
                coWindows.Clear();
                Program.Say($"{who}: coherence ended");
                return;
            }
            bool starting = !coActive;
            coActive = true;
            double scale = Math.Clamp(req.Scale ?? 1, 0.5, 2);   // this stand-in's display is 2x
            bool rescaled = Math.Abs(scale - coScale) > 0.001;
            coScale = scale;
            fpsOthers = Math.Clamp(req.Fps ?? SideStreamRequest.DefaultFPS, 1, SideStreamRequest.MaxFPS);
            fpsFocus = Math.Clamp(req.FocusFPS ?? SideStreamRequest.DefaultFocusFPS, 1, SideStreamRequest.MaxFocusFPS);
            coApps = req.Apps ?? new();
            coAll = req.All == true;
            coHidden = new HashSet<uint>(req.Hidden ?? new());
            var asked = new HashSet<uint>(req.Windows);
            var added = new List<TestWindow>();
            foreach (var id in req.Windows)
            {
                if (coWindows.ContainsKey(id)) continue;
                var info = Windows.Concat(launched).FirstOrDefault(w => w.Id == id);
                if (info == null) continue;
                var w = new TestWindow
                {
                    Id = id, App = info.BundleID, AppName = info.AppName, Title = info.Title, W = info.Width, H = info.Height,
                    Color = Palette[(int)(id % Palette.Length)], Edited = id == 101,
                };
                coWindows[id] = w;
                added.Add(w);
            }
            // A window left out stops streaming: a top-level one goes back on the stand-in's screen (forgotten
            // here with its children; one that joined in the last 2 s stays, its kind 45 may not have been
            // read yet); a child (a menu) stays while its owner does, streamed only while asked for.
            foreach (var w in coWindows.Values.ToList())
            {
                if (asked.Contains(w.Id) || !coWindows.ContainsKey(w.Id)) continue;
                if (w.Joined && Environment.TickCount64 - w.JoinedAt < 2000) continue;
                StopStream(w.Id);
                if (w.IsChild) continue;
                foreach (var c in coWindows.Values.Where(c => c.Owner == w.Id).ToList()) { StopStream(c.Id); coWindows.Remove(c.Id); }
                coWindows.Remove(w.Id);
            }
            uint focus = req.Focus ?? coFocus;
            bool refocused = focus != coFocus && coWindows.ContainsKey(focus);
            if (refocused) coFocus = focus;
            if (coFocus == 0 || !coWindows.ContainsKey(coFocus)) coFocus = coWindows.Keys.FirstOrDefault();
            foreach (var w in coWindows.Values)
            {
                bool wanted = asked.Contains(w.Id) && (!w.IsChild || w.InCrop == false);
                if (wanted && !streams.ContainsKey(w.Id)) StartStream(w);
                if (streams.TryGetValue(w.Id, out var s))
                {
                    s.Fps = w.Id == coFocus ? fpsFocus : fpsOthers;
                    s.Hidden = coHidden.Contains(w.Id);
                    if (rescaled) s.Resize(Pixels(w.W), Pixels(w.H));
                }
            }
            if (starting) SendState(full: true, null, null);
            else if (refocused || added.Count > 0) SendState(full: false, added.Select(w => w.Id).Append(coFocus).Distinct(), null, focusChanged: refocused);
        }
        if (menuApp != FrontApp()) MenusChanged();
    }

    int Pixels(double points) => Math.Max(16, (int)Math.Round(points * coScale) & ~1);

    void StartStream(TestWindow w)
    {
        var s = new WindowStream(this, w, Pixels(w.W), Pixels(w.H)) { Fps = w.Id == coFocus ? fpsFocus : fpsOthers, Hidden = coHidden.Contains(w.Id) };
        streams[w.Id] = s;
        s.Start();
        Program.Say($"{who}: streaming window {w.Id} ({w.Title}) at {Pixels(w.W)}x{Pixels(w.H)}");
    }

    void StopStream(uint id)
    {
        if (streams.Remove(id, out var s)) s.Stop();
    }

    // MARK: kind 45

    WindowState.Window Entry(TestWindow w, bool whole) => new()
    {
        Id = w.Id,
        App = w.App, AppName = whole ? w.AppName : null, Title = w.Title, Role = w.Role,
        Size = w.IsChild ? null : new[] { w.W, w.H },
        Min = w.IsChild || !whole ? null : w.Min, Max = w.IsChild || !whole ? null : w.Max,
        Resizable = w.IsChild ? null : true,
        Focused = w.IsChild ? null : w.Id == coFocus,
        TitleBar = w.IsChild ? null : TitleBar,
        Joined = w.Joined ? true : null,
        Owner = w.Owner, Rect = w.Rect, InCrop = w.IsChild ? w.InCrop : null,
    };

    /// <summary>Kind 45: every window (`full`), or those named, with `removed` and `answering`.</summary>
    void SendState(bool full, IEnumerable<uint>? changed, int? answering, IEnumerable<uint>? removed = null, bool focusChanged = false)
    {
        var m = new WindowState { Gen = ++coGen, Full = full ? true : null, Answering = answering, Focus = coFocus == 0 ? null : coFocus };
        var ids = full ? coWindows.Keys.ToList() : (changed ?? Enumerable.Empty<uint>()).Where(coWindows.ContainsKey).Distinct().ToList();
        if (focusChanged) ids = ids.Concat(coWindows.Values.Where(w => !w.IsChild).Select(w => w.Id)).Distinct().ToList();
        m.Windows = ids.Select(id => Entry(coWindows[id], full)).ToList();
        if (removed != null) m.Removed = removed.ToList();
        if (m.Windows.Count == 0) m.Windows = null;
        var payload = Json.Encode(m);
        Program.Say($"{who}: kind 45 {Encoding.UTF8.GetString(payload)}");
        _ = Send(Kind.WindowState, payload);
    }

    // MARK: kinds 46, 15, 7

    void OnPlaceWindow(byte[] p)
    {
        if (Json.Decode<PlaceWindow>(p) is not { Size.Length: >= 2 } place) return;
        lock (coGate)
        {
            if (!coWindows.TryGetValue(place.Id, out var w) || w.IsChild) { SendState(false, null, place.Token); return; }
            // The size the window takes: within its limits, as AppKit holds a window.
            w.W = Math.Clamp(Math.Round(place.Size[0]), w.Min[0], w.Max[0]);
            w.H = Math.Clamp(Math.Round(place.Size[1]), w.Min[1], w.Max[1]);
            if (streams.TryGetValue(w.Id, out var s)) s.Resize(Pixels(w.W), Pixels(w.H));
            foreach (var c in coWindows.Values.Where(c => c.Owner == w.Id && c.Rect != null))
                if (c.Role == WindowState.Roles.Sheet) c.Rect = new[] { (w.W - 320) / 2, TitleBar, 320, 120 };
            SendState(false, new[] { w.Id }, place.Token);
        }
    }

    void OnWindowCommand(WindowCommand c)
    {
        lock (coGate)
        {
            if (!coWindows.TryGetValue(c.Id, out var w)) return;
            switch (c.Action)
            {
                case WindowActions.Activate:
                    if (coFocus == c.Id) break;
                    coFocus = c.Id;
                    foreach (var s in streams) s.Value.Fps = s.Key == coFocus ? fpsFocus : fpsOthers;
                    SendState(false, new[] { c.Id }, null, focusChanged: true);
                    break;
                case WindowActions.Close when w.Edited && !coWindows.Values.Any(x => x.Owner == w.Id && x.Role == WindowState.Roles.Sheet):
                    // As TextEdit asks: a sheet inside the window's picture; the window stays.
                    var sheet = new TestWindow
                    {
                        Id = nextPopup++, App = w.App, AppName = w.AppName, Title = "Save changes?", Role = WindowState.Roles.Sheet,
                        Owner = w.Id, Rect = new[] { (w.W - 320) / 2, TitleBar, 320, 120 }, InCrop = true, W = 320, H = 120,
                    };
                    coWindows[sheet.Id] = sheet;
                    w.Edited = false;
                    w.Title += " — Edited";
                    SendState(false, new[] { w.Id, sheet.Id }, null);
                    break;
                case WindowActions.Close:
                    var gone = coWindows.Values.Where(x => x.Owner == w.Id).Select(x => x.Id).Append(w.Id).ToList();
                    foreach (var id in gone) { StopStream(id); coWindows.Remove(id); }
                    lock (coGate) launched.RemoveAll(l => l.Id == w.Id);
                    if (coFocus == w.Id) coFocus = coWindows.Values.FirstOrDefault(x => !x.IsChild)?.Id ?? 0;
                    SendState(false, coFocus != 0 ? new[] { coFocus } : null, null, gone, focusChanged: true);
                    break;
                default:
                    Program.Say($"{who}: kind 15 {c.Action} ignored in coherence");
                    break;
            }
        }
        if (menuApp != FrontApp()) MenusChanged();
    }

    void OnLaunch(string bundle)
    {
        var app = new[] { ("com.apple.TextEdit", "TextEdit"), ("com.apple.finder", "Finder"), ("com.apple.Safari", "Safari") }.FirstOrDefault(a => a.Item1 == bundle);
        var name = app.Item2 ?? bundle.Split('.').Last();
        lock (coGate)
        {
            var info = new WindowInfo { Id = nextId++, Title = $"Untitled {nextId - 110}", AppName = name, BundleID = bundle, Width = 720, Height = 480 };
            launched.Add(info);
            if (!coAll && !coApps.Contains(bundle)) { Program.Say($"{who}: launched {bundle}: window {info.Id}, not shared (not in kind 29's apps)"); }
            else
            {
                var w = new TestWindow { Id = info.Id, App = bundle, AppName = name, Title = info.Title, W = info.Width, H = info.Height, Color = Palette[(int)(info.Id % Palette.Length)], Joined = true, JoinedAt = Environment.TickCount64 };
                coWindows[w.Id] = w;
                coFocus = w.Id;
                StartStream(w);
                SendState(false, new[] { w.Id }, null, focusChanged: true);
            }
        }
        _ = Send(Kind.WindowList, ListJson());
        if (menuApp != FrontApp()) MenusChanged();
    }

    // MARK: kind 8 in a window

    void OnWindowInput(byte[] p)
    {
        try
        {
            using var doc = JsonDocument.Parse(p);
            var r = doc.RootElement;
            uint? window = r.TryGetProperty("window", out var wv) && wv.TryGetUInt32(out var wid) ? wid : null;
            if (window is not { } id) return;
            lock (coGate)
            {
                if (r.TryGetProperty("pointer", out var ptr) && streams.TryGetValue(id, out var s))
                {
                    s.Px = ptr.GetProperty("x").GetDouble(); s.Py = ptr.GetProperty("y").GetDouble();
                    var a = ptr.GetProperty("_0").GetString();
                    if (a == "leftDown") s.Down = true; else if (a == "leftUp") s.Down = false;
                }
                if (r.TryGetProperty("key", out var k) && k.GetProperty("down").GetBoolean())
                {
                    int usage = k.GetProperty("hidUsage").GetInt32();
                    if (streams.TryGetValue(id, out var ks)) ks.Keys++;
                    if (usage == 0x13) TogglePopup(id);          // P: a menu that spills out
                    else if (usage == 0x29) ClosePopups();       // Esc: menus close
                    else if (usage == 0x17) TakeBack(id);        // T: the window taken back on the Mac
                }
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { }
    }

    void TogglePopup(uint owner)
    {
        if (!coWindows.TryGetValue(owner, out var w) || w.IsChild) return;
        var open = coWindows.Values.FirstOrDefault(x => x.Owner == owner && x.Role == WindowState.Roles.Menu);
        if (open != null) { ClosePopups(); return; }
        // A menu hanging past the window's right edge: outside its picture, so a stream of its own.
        var menu = new TestWindow
        {
            Id = nextPopup++, App = w.App, AppName = w.AppName, Title = "Menu", Role = WindowState.Roles.Menu, Owner = owner,
            Rect = new[] { w.W - 120, TitleBar + 40, 240, 200 }, InCrop = false, W = 240, H = 200, Color = Color.Gainsboro,
        };
        coWindows[menu.Id] = menu;
        Program.Say($"{who}: menu {menu.Id} opened beside window {owner} (inCrop false)");
        SendState(false, new[] { menu.Id }, null);
    }

    void ClosePopups()
    {
        var menus = coWindows.Values.Where(x => x.Role == WindowState.Roles.Menu).Select(x => x.Id).ToList();
        if (menus.Count == 0) return;
        foreach (var id in menus) { StopStream(id); coWindows.Remove(id); }
        Program.Say($"{who}: menus closed");
        SendState(false, null, null, menus);
    }

    void TakeBack(uint id)
    {
        if (!coWindows.TryGetValue(id, out var w) || w.IsChild) return;
        var gone = coWindows.Values.Where(x => x.Owner == id).Select(x => x.Id).Append(id).ToList();
        foreach (var g in gone) { StopStream(g); coWindows.Remove(g); }
        if (coFocus == id) coFocus = coWindows.Values.FirstOrDefault(x => !x.IsChild)?.Id ?? 0;
        Program.Say($"{who}: window {id} taken back");
        _ = Send(Kind.TakenBack, Json.Encode(new TakenBack { Windows = { id } }));
    }

    // MARK: the Mac's menus (kinds 24, 27, 25)

    string FrontApp()
    {
        lock (coGate)
        {
            if (coActive && coWindows.TryGetValue(coFocus, out var w)) return w.AppName;
            if (active.Kind == SourceKind.Window) return Windows.Concat(launched).FirstOrDefault(x => x.Id == active.WindowID)?.AppName ?? "Finder";
            return "Finder";
        }
    }

    static string BundleOf(string app) => app switch { "TextEdit" => "com.apple.TextEdit", "Safari" => "com.apple.Safari", "Finder" => "com.apple.finder", _ => "com.example." + app };

    void MenusChanged()
    {
        menuVersion++;
        if (menusSubscribed) SendTopLevel(null);
    }

    void SendTopLevel(int? answering)
    {
        menuApp = FrontApp();
        var m = new MacMenu
        {
            Version = menuVersion, App = menuApp, BundleID = BundleOf(menuApp), Answering = answering,
            Menus = new() { new() { Id = "1", Title = menuApp, Submenu = true }, new() { Id = "2", Title = "File", Submenu = true }, new() { Id = "3", Title = "Edit", Submenu = true }, new() { Id = "4", Title = "View", Submenu = true } },
        };
        _ = Send(Kind.MacMenu, Json.Encode(m));
    }

    static List<MacMenuItem> ItemsOf(string id, string app) => id switch
    {
        "1" => new() { new() { Id = "1.0", Title = $"About {app}" }, new() { Separator = true }, new() { Id = "1.2", Title = "Settings…", Key = "⌘," }, new() { Separator = true }, new() { Id = "1.4", Title = $"Quit {app}", Key = "⌘Q" } },
        "2" => new() { new() { Id = "2.0", Title = "New", Key = "⌘N" }, new() { Id = "2.1", Title = "Open…", Key = "⌘O" }, new() { Id = "2.2", Title = "Open Recent", Submenu = true },
                       new() { Separator = true }, new() { Id = "2.4", Title = "Close Window", Key = "⌘W" }, new() { Id = "2.5", Title = "Save As…", Key = "⇧⌘S", Enabled = false }, new() { Id = "2.6", Title = "Print…", Key = "⌘P" } },
        "2.2" => new() { new() { Id = "2.2.0", Title = "Readme.md" }, new() { Id = "2.2.1", Title = "Notes & Plans.txt" }, new() { Separator = true }, new() { Id = "2.2.3", Title = "Clear Menu" } },
        "3" => new() { new() { Id = "3.0", Title = "Undo", Key = "⌘Z" }, new() { Id = "3.1", Title = "Redo", Key = "⇧⌘Z" }, new() { Separator = true }, new() { Id = "3.3", Title = "Cut", Key = "⌘X" },
                       new() { Id = "3.4", Title = "Copy", Key = "⌘C" }, new() { Id = "3.5", Title = "Paste", Key = "⌘V" }, new() { Id = "3.6", Title = "Select All", Key = "⌘A" }, new() { Separator = true }, new() { Id = "3.8", Title = "Check Spelling While Typing", Mark = "✓" } },
        "4" => new() { new() { Id = "4.0", Title = "Show Toolbar", Key = "⌥⌘T", Mark = "✓" }, new() { Id = "4.1", Title = "Show Sidebar", Mark = "-" }, new() { Id = "4.2", Title = "Enter Full Screen", Key = "fn F" } },
        _ => new(),
    };

    void OnFetchMenu(byte[] p)
    {
        if (Json.Decode<FetchMenu>(p) is not { } f) return;
        if (f.Id == null) { menusSubscribed = true; SendTopLevel(f.Token); return; }
        // An id from another version is answered at once and never read (MacMenu.swift's rule).
        var answer = new MacMenu { Version = menuVersion, Answering = f.Token, Menu = f.Id };
        if (f.Version == menuVersion) answer.Items = ItemsOf(f.Id, menuApp);
        _ = Send(Kind.MacMenu, Json.Encode(answer));
    }

    void OnPressMenu(byte[] p)
    {
        if (Json.Decode<PressMenuItem>(p) is not { } press) return;
        bool ok = press.Version == menuVersion && press.Id != "2.5";
        Program.Say($"{who}: menu item {press.Id} \"{press.Title}\" {(ok ? "pressed" : "refused")}");
        _ = Send(Kind.MacMenu, Json.Encode(new MacMenu { Version = menuVersion, Answering = press.Token, Pressed = ok, Note = ok ? null : "The menus changed. Open the menu again." }));
        if (ok && press.Id == "2.0") OnLaunchFromMenu();
    }

    void OnLaunchFromMenu()
    {
        string bundle;
        lock (coGate) bundle = coActive && coWindows.TryGetValue(coFocus, out var w) ? w.App : "com.apple.TextEdit";
        if (coActive) OnLaunch(bundle);
    }

    // MARK: a window's stream

    /// <summary>One window's side stream: its own encoder at its size, its pattern, kinds 30 and 31
    /// with its ID; the focused window at the focused rate, a hidden one a frame every 3 s.</summary>
    sealed class WindowStream
    {
        readonly HostSession session;
        readonly TestWindow window;
        readonly CancellationTokenSource stop = new();
        volatile int width, height;
        volatile bool resized;
        public volatile bool Keyframe, Hidden, Down;
        public volatile int Fps = 15, Keys;
        public double Px = -1, Py = -1;

        public WindowStream(HostSession session, TestWindow window, int width, int height)
        {
            this.session = session; this.window = window; this.width = width; this.height = height;
        }

        public void Resize(int w, int h)
        {
            if (w == width && h == height) return;
            width = w; height = h; resized = true;
        }

        public void Stop() => stop.Cancel();

        public void Start() => _ = Task.Run(async () =>
        {
            try { await Loop(); }
            catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException or Sill.Windows.Interop.ComException)
            {
                Program.Say($"window {window.Id}: stream ended: {e.Message}");
            }
        });

        async Task Loop()
        {
            int w = width, h = height;
            var encoder = new HevcEncoder(w, h, 60, Math.Max(2_000_000, Program.Bitrate / 3));
            try
            {
                var nv12 = new byte[w * h * 3 / 2];
                ParameterSets? sent = null;
                var clock = Stopwatch.StartNew();
                long frame = 0;
                double next = 0;
                while (!stop.IsCancellationRequested && !session.stop.IsCancellationRequested)
                {
                    if (resized || Keyframe)
                    {
                        // A new size, or kind 44: an encoder made again starts with a keyframe.
                        resized = false; Keyframe = false;
                        encoder.Dispose();
                        w = width; h = height;
                        nv12 = new byte[w * h * 3 / 2];
                        encoder = new HevcEncoder(w, h, 60, Math.Max(2_000_000, Program.Bitrate / 3));
                        sent = null;
                    }
                    TestPictures.Window(nv12, w, h, window, frame, Px, Py, Down, Keys, h / Math.Max(1.0, window.H));
                    foreach (var au in encoder.Encode(nv12, 60))
                    {
                        var nals = HevcEncoder.Nals(au);
                        var sets = nals.Where(n => n.Length > 2 && ((n[0] >> 1) & 0x3F) is 32 or 33 or 34).ToList();
                        var vcl = nals.Where(n => n.Length > 2 && ((n[0] >> 1) & 0x3F) is not (32 or 33 or 34)).ToList();
                        bool key = vcl.Any(n => ((n[0] >> 1) & 0x3F) is >= 16 and <= 21);
                        if (sets.Count == 3)
                        {
                            var ps = new ParameterSets(4, sets.OrderBy(n => (n[0] >> 1) & 0x3F).ToList());
                            if (key || !ps.SameAs(sent)) { await session.Send(Kind.SideParameterSets, SideStream.Pack(window.Id, ps.Encode())); sent = ps; }
                        }
                        if (vcl.Count == 0 || sent == null) continue;
                        var ms = new MemoryStream();
                        foreach (var n in vcl) { ms.WriteByte((byte)(n.Length >> 24)); ms.WriteByte((byte)(n.Length >> 16)); ms.WriteByte((byte)(n.Length >> 8)); ms.WriteByte((byte)n.Length); ms.Write(n); }
                        await session.Send(Kind.SideFrame, SideStream.Pack(window.Id, ms.ToArray()), key);
                    }
                    frame++;
                    // A hidden window's frames are dropped but one every few seconds (the contract).
                    next += Hidden ? 3 : 1.0 / Math.Max(1, Fps);
                    var due = TimeSpan.FromSeconds(next) - clock.Elapsed;
                    if (due > TimeSpan.Zero) await Task.Delay(due, stop.Token);
                    else if (due < TimeSpan.FromSeconds(-0.5)) next = clock.Elapsed.TotalSeconds;
                }
            }
            finally { encoder.Dispose(); }
        }
    }
}

/// <summary>TEST ONLY: the Coherence windows' patterns and the apps' icons.</summary>
static class TestPictures
{
    static readonly Dictionary<(string, int), (byte[] mask, int w, int h)> masks = new();

    /// <summary>A line of text as a luma mask, for drawing a window's label into NV12.</summary>
    static (byte[] mask, int w, int h) Mask(string text, int height)
    {
        lock (masks)
        {
            if (masks.TryGetValue((text, height), out var m)) return m;
            using var font = new Font("Segoe UI", Math.Max(6, height * 0.7f), FontStyle.Bold, GraphicsUnit.Pixel);
            var size = TextRenderer.MeasureText(text, font);
            int w = Math.Max(1, size.Width), h = Math.Max(1, size.Height);
            using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Black); TextRenderer.DrawText(g, text, font, new Point(0, 0), Color.White); }
            var mask = new byte[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) mask[y * w + x] = bmp.GetPixel(x, y).R;
            if (masks.Count > 64) masks.Clear();
            return masks[(text, height)] = (mask, w, h);
        }
    }

    static (byte y, byte u, byte v) Yuv(Color c)
    {
        double y = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
        return ((byte)Math.Clamp(y, 0, 255), (byte)Math.Clamp((c.B - y) / 1.8556 + 128, 0, 255), (byte)Math.Clamp((c.R - y) / 1.5748 + 128, 0, 255));
    }

    static void Fill(byte[] nv12, int w, int h, int x0, int y0, int rw, int rh, Color c)
    {
        var (yy, u, v) = Yuv(c);
        int x1 = Math.Min(w, x0 + rw), y1 = Math.Min(h, y0 + rh);
        x0 = Math.Max(0, x0); y0 = Math.Max(0, y0);
        for (int r = y0; r < y1; r++) nv12.AsSpan(r * w + x0, Math.Max(0, x1 - x0)).Fill(yy);
        for (int r = y0 / 2; r < (y1 + 1) / 2; r++) for (int x = x0 / 2; x < (x1 + 1) / 2; x++) { nv12[w * h + r * w + x * 2] = u; nv12[w * h + r * w + x * 2 + 1] = v; }
    }

    static void Disc(byte[] nv12, int w, int h, int cx, int cy, int radius, Color c)
    {
        var (yy, u, v) = Yuv(c);
        for (int r = -radius; r <= radius; r++)
            for (int x = -radius; x <= radius; x++)
            {
                if (r * r + x * x > radius * radius) continue;
                int py = cy + r, px = cx + x;
                if (py < 0 || py >= h || px < 0 || px >= w) continue;
                nv12[py * w + px] = yy;
                nv12[w * h + (py / 2) * w + (px / 2) * 2] = u;
                nv12[w * h + (py / 2) * w + (px / 2) * 2 + 1] = v;
            }
    }

    static void Text(byte[] nv12, int w, int h, string text, int x0, int y0, int height, byte luma)
    {
        var (mask, mw, mh) = Mask(text, Math.Max(6, height));
        for (int y = 0; y < mh && y0 + y < h; y++)
        {
            if (y0 + y < 0) continue;
            for (int x = 0; x < mw && x0 + x < w; x++)
                if (x0 + x >= 0 && mask[y * mw + x] > 128) nv12[(y0 + y) * w + x0 + x] = luma;
        }
    }

    /// <summary>A window: its colour, a fake Mac title bar with its traffic lights (the client crops it:
    /// a white line right under it shows where the crop must fall), its label and size, a white border
    /// at its other edges, a moving bar, a square whose colour counts the keys typed into it, and the
    /// pointer when it is over it. A menu: light rows under a label.</summary>
    public static void Window(byte[] nv12, int w, int h, HostSession.TestWindow win, long frame, double px, double py, bool down, int keys, double scale)
    {
        int s1 = Math.Max(1, (int)Math.Round(scale));
        if (win.Role == WindowState.Roles.Menu)
        {
            Fill(nv12, w, h, 0, 0, w, h, Color.FromArgb(236, 236, 236));
            int row = Math.Max(8, (int)Math.Round(22 * scale));
            for (int y = row, i = 0; y < h; y += row, i++)
            {
                Fill(nv12, w, h, (int)(12 * scale), y - row / 2, (int)(w * (0.4 + 0.1 * (i % 4))), Math.Max(2, row / 5), Color.FromArgb(90, 90, 90));
            }
            Fill(nv12, w, h, 0, 0, w, s1, Color.Gray); Fill(nv12, w, h, 0, h - s1, w, s1, Color.Gray);
            Fill(nv12, w, h, 0, 0, s1, h, Color.Gray); Fill(nv12, w, h, w - s1, 0, s1, h, Color.Gray);
            Text(nv12, w, h, $"Menu {win.Id}", (int)(12 * scale), 2, row - 4, 30);
        }
        else
        {
            Fill(nv12, w, h, 0, 0, w, h, win.Color);
            int tb = (int)Math.Round(win.TitleBarPt * scale);
            if (tb > 0)
            {
                Fill(nv12, w, h, 0, 0, w, tb, Color.FromArgb(232, 232, 232));
                int r = Math.Max(2, (int)Math.Round(6 * scale));
                Disc(nv12, w, h, (int)(14 * scale), tb / 2, r, Color.FromArgb(255, 95, 86));
                Disc(nv12, w, h, (int)(34 * scale), tb / 2, r, Color.FromArgb(255, 189, 46));
                Disc(nv12, w, h, (int)(54 * scale), tb / 2, r, Color.FromArgb(39, 201, 63));
                Text(nv12, w, h, win.Title, (int)(80 * scale), (int)(4 * scale), (int)(tb * 0.7), 50);
                Fill(nv12, w, h, 0, tb, w, Math.Max(2, s1), Color.White);
            }
            Fill(nv12, w, h, 0, h - 2 * s1, w, 2 * s1, Color.White);
            Fill(nv12, w, h, 0, tb, 2 * s1, h - tb, Color.White);
            Fill(nv12, w, h, w - 2 * s1, tb, 2 * s1, h - tb, Color.White);
            int line = (int)Math.Round(22 * scale);
            Text(nv12, w, h, $"{win.AppName} — {win.Title} ({win.Id})", (int)(20 * scale), tb + (int)(16 * scale), line, 245);
            Text(nv12, w, h, $"{win.W:0}×{win.H:0} pt, {w}×{h} px", (int)(20 * scale), tb + (int)(16 * scale) + line + 6, (int)(line * 0.8), 235);
            int bx = (int)(frame * 6 * scale % Math.Max(1, w - 40 * scale));
            Fill(nv12, w, h, bx, h - (int)(40 * scale), (int)(40 * scale), (int)(20 * scale), Color.White);
            var keyColor = Color.FromArgb(128 + (int)(100 * Math.Cos(keys * 0.9)), 128, 128 + (int)(100 * Math.Sin(keys * 0.9)));
            int sq = (int)(60 * scale);
            Fill(nv12, w, h, w - sq - (int)(20 * scale), tb + (int)(20 * scale), sq, sq, keyColor);
        }
        if (px >= 0 && py >= 0)
        {
            int cx = (int)Math.Clamp(px * w, 0, w - 1), cy = (int)Math.Clamp(py * h, 0, h - 1);
            if (down) Disc(nv12, w, h, cx, cy, (int)(14 * scale), Color.FromArgb(40, 40, 200));
            for (int d = -20; d <= 20; d++)
            {
                if (cx + d >= 0 && cx + d < w) nv12[cy * w + cx + d] = 0;
                if (cy + d >= 0 && cy + d < h) nv12[(cy + d) * w + cx] = 0;
            }
        }
    }

    /// <summary>An app's icon: a rounded square of its colour with its letter, as a PNG.</summary>
    public static byte[] Icon(int size, Color color, string letter)
    {
        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float m = size * 0.06f, r = size * 0.22f, d = r * 2, e = size - 2 * m;
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(m, m, d, d, 180, 90); path.AddArc(m + e - d, m, d, d, 270, 90);
            path.AddArc(m + e - d, m + e - d, d, d, 0, 90); path.AddArc(m, m + e - d, d, d, 90, 90);
            path.CloseFigure();
            using var b = new SolidBrush(color);
            g.FillPath(b, path);
            using var font = new Font("Segoe UI", size * 0.5f, FontStyle.Bold, GraphicsUnit.Pixel);
            var sz = g.MeasureString(letter, font);
            g.DrawString(letter, font, Brushes.White, (size - sz.Width) / 2, (size - sz.Height) / 2);
        }
        var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }
}
