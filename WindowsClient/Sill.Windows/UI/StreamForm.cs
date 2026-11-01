using System.Diagnostics;
using System.Runtime.InteropServices;
using Sill.Windows.Input;
using Sill.Windows.Net;
using Sill.Windows.Protocol;
using Sill.Windows.Video;

namespace Sill.Windows.UI;

/// <summary>The stream window: the Mac's picture, and a toolbar for what to stream (the Desktop, a
/// window, an app to open), the Mac's quality, full screen and Disconnect. The mouse over the picture
/// and the keyboard while it has focus go to the Mac; the clipboard follows when it comes forward.</summary>
public sealed class StreamForm : Form
{
    // Laid out by hand (OnLayout): under the toolbar in a window, all of the screen in full screen with
    // the toolbar over it. No control here changes its Dock at run time (WinForms' anchor layout threw on it under .NET 8).
    readonly VideoPanel panel = new();
    readonly ToolStrip bar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(8, 4, 8, 4), Renderer = new DarkRenderer(), ImageScalingSize = new Size(20, 20) };
    readonly ToolStripButton desktop = new("Desktop") { ToolTipText = $"Stream the Mac’s whole screen ({KeyboardForwarder.HotkeyText(Keys.D)})" };
    readonly ToolStripDropDownButton windows = new("Windows") { ToolTipText = $"Stream one of the Mac’s windows ({KeyboardForwarder.HotkeyText(Keys.W)})" };
    readonly ToolStripDropDownButton apps = new("Apps") { ToolTipText = "Open an app on the Mac" };
    readonly ToolStripDropDownButton quality = new("Quality") { ToolTipText = "The Mac’s stream quality" };
    readonly ToolStripDropDownButton view = new("View") { ToolTipText = "This window, screenshots and Sill’s shortcuts" };
    readonly ToolStripLabel stats = new("") { Alignment = ToolStripItemAlignment.Right, ForeColor = Theme.SecondaryText };
    readonly ToolStripButton disconnect = new("Disconnect") { Alignment = ToolStripItemAlignment.Right, ToolTipText = $"Disconnect ({KeyboardForwarder.HotkeyText(Keys.Q)})" };
    readonly ToolStripButton fullScreen = new("Full Screen") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Full screen (Ctrl+Alt+Enter)" };
    readonly ToolStripButton settingsButton = new("Settings") { Alignment = ToolStripItemAlignment.Right };
    readonly Label message = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Text, BackColor = Color.FromArgb(28, 28, 30), Font = Theme.Heading, Visible = false };
    readonly Label statsOverlay = new() { AutoSize = true, ForeColor = Theme.Text, BackColor = Color.FromArgb(28, 28, 30), Font = Theme.Small, Padding = new Padding(8, 4, 8, 4), Visible = false };
    readonly System.Windows.Forms.Timer viewportTimer = new() { Interval = 300 };
    readonly System.Windows.Forms.Timer hintTimer = new() { Interval = 2500 };
    readonly System.Windows.Forms.Timer edgeTimer = new() { Interval = 100 };
    readonly KeyboardForwarder keyboard;
    readonly Dictionary<string, Cursor> cursors = new();
    // Menus open over the picture (the toolbar's, the windows popup): the keyboard is theirs meanwhile.
    int menusOpen;
    // Full screen: since when the pointer has rested at the top edge's middle, to bring the toolbar down.
    long edgeSince;
    // A hint that names a screenshot: a click on it shows the file.
    string? hintPath;

    VideoPipeline? video;
    Session? session;

    bool full;
    Rectangle normalBounds;
    FormWindowState normalState;
    (float hotX, float hotY, float width, float height, byte[] png)? cursorShape;
    double? fitScale;
    MouseButtons buttonsDown;
    uint lastClipboardSequence;
    string? lastFromMac;

    // Pointer moves, at most one every 4 ms; the newest goes out last.
    readonly object moveGate = new();
    (double x, double y)? pendingMove;
    long lastMoveTicks;
    System.Threading.Timer? moveTimer;

    /// <summary>The user closed the window (or a session ended for good): the connect screen comes back.</summary>
    public event Action? Disconnected;

    public VideoPipeline? Video => video;
    public Session? Session => session;

    public StreamForm()
    {
        Theme.Apply(this);
        Icon = Theme.AppIcon;
        Text = "Sill";
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = false;
        var s = Settings.Current;
        var size = s.WindowWidth > 300 && s.WindowHeight > 200 ? new Size(s.WindowWidth, s.WindowHeight) : new Size(1280, 840);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        StartPosition = FormStartPosition.CenterScreen;
        // Where it was closed, while a display still shows its title bar (one unplugged since: the middle).
        if (s.WindowX is int x && s.WindowY is int y)
        {
            var titleBar = new Rectangle(x + 40, y, Math.Max(1, size.Width - 80), 32);
            var screen = Screen.AllScreens.FirstOrDefault(sc => sc.WorkingArea.IntersectsWith(titleBar));
            if (screen != null)
            {
                StartPosition = FormStartPosition.Manual;
                area = screen.WorkingArea;
                Location = new Point(Math.Clamp(x, area.Left - size.Width + 120, area.Right - 120), Math.Clamp(y, area.Top, area.Bottom - 60));
            }
        }
        Size = new Size(Math.Min(size.Width, area.Width), Math.Min(size.Height, area.Height));
        TestArguments.Place(this);
        if (s.WindowMaximized) WindowState = FormWindowState.Maximized;
        TopMost = s.AlwaysOnTop;
        MinimumSize = new Size(480, 320);
        bar.BackColor = Theme.Background;
        bar.ForeColor = Theme.Text;
        bar.Font = Theme.Body;
        bar.Items.AddRange(new ToolStripItem[] { desktop, windows, apps, new ToolStripSeparator(), quality, view, disconnect, fullScreen, settingsButton, stats });
        foreach (ToolStripItem i in bar.Items) i.ForeColor = Theme.Text;
        stats.ForeColor = Theme.SecondaryText;
        Controls.Add(panel);
        Controls.Add(message);
        Controls.Add(statsOverlay);
        Controls.Add(bar);
        message.BringToFront();

        keyboard = new KeyboardForwarder(json => session?.SendInput(json))
        {
            HasKeyboard = () => ContainsFocus && panel.Focused && ActiveForm == this && menusOpen == 0,
            IsFullScreen = () => full,
            CtrlIsCommand = () => Settings.Current.CtrlIsCommand,
            WindowsKeyToMac = () => Settings.Current.WindowsKeyToMac,
        };
        keyboard.ToggleFullScreen += () => BeginInvoke(ToggleFullScreen);
        // Off the hook's own call: a hook that takes long is dropped by Windows.
        keyboard.Hotkey += k => BeginInvoke(() => OnHotkey(k));

        desktop.Click += (_, _) => session?.Select(Source.Desktop);
        windows.DropDownOpening += (_, _) => BuildWindowsMenu();
        apps.DropDownOpening += (_, _) => BuildAppsMenu();
        quality.DropDownOpening += (_, _) => BuildQualityMenu();
        view.DropDownOpening += (_, _) => BuildViewMenu();
        disconnect.Click += (_, _) => Close();
        fullScreen.Click += (_, _) => ToggleFullScreen();
        settingsButton.Click += (_, _) =>
        {
            using var d = new SettingsDialog();
            d.ShowDialog(this);
            session?.ShareClipboard(Settings.Current.ShareClipboard);
            UpdateStats();
            panel.Focus();
        };
        // An open menu has the keyboard (its arrows and Enter are not the Mac's), and the picture gets it back after.
        foreach (var dd in new ToolStripDropDownItem[] { windows, apps, quality, view })
        {
            dd.DropDownOpened += (_, _) => MenuOpened();
            dd.DropDownClosed += (_, _) => MenuClosed();
        }

        panel.Pointer += OnPointer;
        panel.Wheel += OnWheel;
        panel.SideButton += forward =>
        {
            // Back and forward as the Mac's apps take them: Safari, Finder, Chrome, Xcode, Mail.
            if (session != null && Settings.Current.MouseBackForward) keyboard.SendShortcut(forward ? KeyMap.RightBracket : KeyMap.LeftBracket, MacModifiers.Command);
        };
        edgeTimer.Tick += (_, _) => EdgeTick();
        statsOverlay.Click += (_, _) => panel.Focus();
        panel.Resize += (_, _) =>
        {
            video?.Resize(panel.ClientSize.Width, panel.ClientSize.Height);
            ApplyCursor();
            viewportTimer.Stop();
            viewportTimer.Start();
        };
        panel.MouseLeave += (_, _) => FlushMove();
        viewportTimer.Tick += (_, _) => { viewportTimer.Stop(); SendViewport(); };
        hintTimer.Tick += (_, _) => { hintTimer.Stop(); if (message.Tag as string == "hint") ShowMessage(null); };
        message.Click += (_, _) =>
        {
            if (message.Tag as string == "hint" && hintPath is { } shot && File.Exists(shot))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{shot}\"") { UseShellExecute = true });
            panel.Focus();
        };
        statsOverlay.SizeChanged += (_, _) => PerformLayout();
        Shown += (_, _) => { if (s.WindowFullScreen && !full) ToggleFullScreen(); };
        Activated += (_, _) => { panel.Focus(); SyncClipboardToMac(); };
        Deactivate += (_, _) => LetGo();
        Load += (_, _) =>
        {
            video = new VideoPipeline(panel.Handle, Math.Max(1, panel.ClientSize.Width), Math.Max(1, panel.ClientSize.Height));
            video.PictureSizeChanged += (w, h) => BeginInvoke(() =>
            {
                TestArguments.Log($"picture {w}x{h}, decoder {video?.DecoderName}");
                panel.PictureWidth = w; panel.PictureHeight = h;
                ShowMessage(null);
                ApplyCursor();
                if (TestArguments.InputScript && session != null && !scripted) { scripted = true; _ = RunInputScript(session); }
                if (TestArguments.Actions != null && session != null && !acted) { acted = true; _ = RunActions(session); }
            });
            video.Problem += p => BeginInvoke(() => { TestArguments.Log($"video problem: {p}"); ShowMessage(p); });
            video.PictureShown += () => BeginInvoke(() =>
            {
                TestArguments.Log("picture shown");
                if (message.Visible && message.Tag as string != "hint" && !message.Text.StartsWith("Nothing", StringComparison.Ordinal)) ShowMessage(null);
            });
            keyboard.Install();
        };
        ShowMessage("Connecting…");
    }

    /// <summary>A session for this window: the first, or one that replaces a lost one (a reconnect).</summary>
    public void Attach(Session next)
    {
        var old = session;
        session = next;
        if (old != null) old.Dispose();
        next.WindowsChanged += () => BeginInvoke(OnWindowsChanged);
        next.SettingsChanged += () => BeginInvoke(() => { UpdateStats(); ApplyCursor(); });
        next.FiguresChanged += () => BeginInvoke(UpdateStats);
        next.CursorChanged += c => BeginInvoke(() => { cursorShape = c; ApplyCursor(); });
        next.ClipboardReceived += t => BeginInvoke(() => { TestArguments.Log($"clipboard from the Mac: {t}"); ReceiveClipboard(t); });
        next.SettingsChanged += () => TestArguments.Log($"settings: bitrate {next.Settings?.Settings?.Bitrate} answering {next.Settings?.Answering}");
        next.WindowsChanged += () => TestArguments.Log($"windows: {next.Windows?.Windows.Count}, active {next.Windows?.Active}");
        next.Ended += e => TestArguments.Log($"session ended: {e.Message} ({e.Reason})");
        ShowMessage("Waiting for the picture…");
        video?.Clear();
        next.Start(CurrentViewport());
        Text = $"Sill — {next.MacName}";
        lastClipboardSequence = 0;
    }

    /// <summary>The toolbar docks itself; the picture, the message and the figures are placed here.</summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int top = bar.Visible && !full ? bar.Height : 0;
        int h = Math.Max(1, ClientSize.Height - top);
        panel.SetBounds(0, top, ClientSize.Width, h);
        if (message.Tag as string != "hint") message.SetBounds(0, top, ClientSize.Width, h);
        int margin = (int)(12 * DeviceDpi / 96.0);
        statsOverlay.Location = new Point(ClientSize.Width - statsOverlay.Width - margin, ClientSize.Height - statsOverlay.Height - margin);
    }

    /// <summary>A line over the picture: a state (until replaced), or a hint near the top for a few
    /// seconds; a hint with a <paramref name="path"/> shows that file when clicked.</summary>
    public void ShowMessage(string? text, bool hint = false, string? path = null)
    {
        message.Text = text ?? "";
        message.Tag = hint ? "hint" : null;
        message.Visible = text != null;
        hintPath = hint ? path : null;
        if (hint)
        {
            message.Font = Theme.Body;
            int width = Math.Clamp(TextRenderer.MeasureText(message.Text, message.Font).Width + 48, 300, Math.Max(300, ClientSize.Width - 40));
            int top = (bar.Visible ? bar.Height : 0) + 20;
            message.SetBounds((ClientSize.Width - width) / 2, top, width, (int)(44 * DeviceDpi / 96.0));
            message.Cursor = path != null ? Cursors.Hand : Cursors.Default;
            hintTimer.Stop();
            hintTimer.Interval = path != null ? 5000 : 2500;
            hintTimer.Start();
        }
        else
        {
            message.Font = Theme.Heading;
            message.Cursor = Cursors.Default;
            PerformLayout();
        }
        if (text != null) message.BringToFront();
    }

    // MARK: the catalog

    void OnWindowsChanged()
    {
        var list = session?.Windows;
        if (list == null) return;
        desktop.Checked = list.Active.Kind == SourceKind.Desktop;
        var active = list.Active.Kind == SourceKind.Window ? list.Windows.FirstOrDefault(w => w.Id == list.Active.WindowID) : null;
        Text = active != null ? $"{active.AppName} — {(string.IsNullOrWhiteSpace(active.Title) ? active.AppName : active.Title)} — {session!.MacName}"
                              : list.Active.Kind == SourceKind.Desktop ? $"Desktop — {session!.MacName}" : $"Sill — {session!.MacName}";
        if (list.Active.Kind == SourceKind.None) ShowMessage("Nothing is streaming. Pick the Desktop, a window or an app above.");
        else if (message.Visible && message.Text.StartsWith("Nothing", StringComparison.Ordinal)) ShowMessage(panel.PictureWidth > 0 ? null : "Waiting for the picture…");
    }

    void BuildWindowsMenu()
    {
        windows.DropDownItems.Clear();
        windows.DropDownItems.AddRange(WindowItems(withDesktop: false).ToArray());
    }

    /// <summary>The Mac's windows, each with its thumbnail, and what can be done to the one streaming:
    /// the toolbar's Windows menu, and (with the Desktop first) the popup of Ctrl+Alt+Shift+W.</summary>
    List<ToolStripItem> WindowItems(bool withDesktop)
    {
        var items = new List<ToolStripItem>();
        var list = session?.Windows;
        if (withDesktop)
        {
            items.Add(new ToolStripMenuItem("Desktop", null, (_, _) => session?.Select(Source.Desktop))
                { Checked = list?.Active.Kind == SourceKind.Desktop, ShortcutKeyDisplayString = KeyboardForwarder.HotkeyText(Keys.D) });
            items.Add(new ToolStripSeparator());
        }
        if (list == null || list.Windows.Count == 0)
        {
            items.Add(new ToolStripMenuItem("No windows open on the Mac") { Enabled = false });
            return items;
        }
        foreach (var group in list.Windows.GroupBy(w => w.AppName))
        {
            foreach (var w in group)
            {
                var title = string.IsNullOrWhiteSpace(w.Title) ? w.AppName : $"{w.AppName} — {w.Title}";
                var item = new ToolStripMenuItem(title.Length > 80 ? title[..80] + "…" : title)
                {
                    Checked = list.Active.Kind == SourceKind.Window && list.Active.WindowID == w.Id,
                    Image = Thumbnail(w.Id),
                    ImageScaling = ToolStripItemImageScaling.None,
                };
                var id = w.Id;
                item.Click += (_, _) => session?.Select(Source.Window(id));
                items.Add(item);
            }
        }
        if (list.Active.Kind == SourceKind.Window)
        {
            var id = list.Active.WindowID;
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("Minimize This Window", null, (_, _) => session?.WindowCommand(id, "minimize")));
            items.Add(new ToolStripMenuItem("Full Screen on the Mac", null, (_, _) => session?.WindowCommand(id, "fullScreen")));
            items.Add(new ToolStripMenuItem("Close This Window", null, (_, _) => session?.WindowCommand(id, "close")));
        }
        foreach (var i in items) i.ForeColor = i.Enabled ? Theme.Text : Theme.SecondaryText;
        return items;
    }

    /// <summary>The windows as a menu over the picture (the toolbar may be away, in full screen).</summary>
    void ShowWindowsPopup()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), ImageScalingSize = bar.ImageScalingSize, ShowCheckMargin = true, ShowImageMargin = true };
        menu.Items.AddRange(WindowItems(withDesktop: true).ToArray());
        menu.Opened += (_, _) => MenuOpened();
        menu.Closed += (_, _) => { MenuClosed(); BeginInvoke(menu.Dispose); };
        var size = menu.GetPreferredSize(Size.Empty);
        menu.Show(panel, new Point(Math.Max(0, (panel.Width - size.Width) / 2), (int)(48 * DeviceDpi / 96.0)));
        // Arrows and Enter choose a window at once.
        if (menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Checked) is { } current) current.Select();
    }

    void MenuOpened()
    {
        menusOpen++;
        LetGo();
    }

    void MenuClosed()
    {
        menusOpen = Math.Max(0, menusOpen - 1);
        if (menusOpen == 0) panel.Focus();
    }

    Image? Thumbnail(uint id)
    {
        byte[]? jpeg;
        lock (session!.Thumbnails) session.Thumbnails.TryGetValue(id, out jpeg);
        if (jpeg == null) return null;
        try
        {
            using var src = Image.FromStream(new MemoryStream(jpeg));
            int h = (int)(40 * DeviceDpi / 96.0), w = Math.Max(1, src.Width * h / Math.Max(1, src.Height));
            if (w > h * 2) { w = h * 2; h = Math.Max(1, src.Height * w / Math.Max(1, src.Width)); }
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, w, h);
            return bmp;
        }
        catch (ArgumentException) { return null; }
    }

    void BuildAppsMenu()
    {
        apps.DropDownItems.Clear();
        var list = session?.Apps ?? new();
        if (list.Count == 0)
        {
            apps.DropDownItems.Add(new ToolStripMenuItem("The Mac hasn’t sent its apps yet") { Enabled = false });
            return;
        }
        // Open apps first (they have windows), then the rest.
        var open = session?.Windows?.Windows.Select(w => w.BundleID).ToHashSet() ?? new HashSet<string>();
        var items = new List<ToolStripItem>();
        foreach (var app in list.OrderBy(a => open.Contains(a.BundleID) ? 0 : 1).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new ToolStripMenuItem(app.Name) { Image = AppIconImage(app.BundleID), ForeColor = Theme.Text };
            var bundle = app.BundleID;
            item.Click += (_, _) => session?.Launch(bundle);
            items.Add(item);
        }
        apps.DropDown.MaximumSize = new Size(420, Math.Max(300, Screen.FromControl(this).WorkingArea.Height - 120));
        apps.DropDownItems.AddRange(items.ToArray());
    }

    Image? AppIconImage(string bundleID)
    {
        byte[]? png;
        lock (session!.Icons) session.Icons.TryGetValue(bundleID, out png);
        if (png == null) return null;
        try
        {
            using var src = Image.FromStream(new MemoryStream(png));
            int s = (int)(20 * DeviceDpi / 96.0);
            var bmp = new Bitmap(s, s);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, s, s);
            return bmp;
        }
        catch (ArgumentException) { return null; }
    }

    void BuildQualityMenu()
    {
        quality.DropDownItems.Clear();
        var state = session?.Settings;
        var s = state?.Settings;
        if (s == null)
        {
            quality.DropDownItems.Add(new ToolStripMenuItem("The Mac hasn’t said its settings (update Sill on the Mac)") { Enabled = false });
        }
        else
        {
            var header = state!.Away?.ThisConnectionAway == true ? "Quality (away from home)" : "Quality";
            quality.DropDownItems.Add(new ToolStripMenuItem(header) { Enabled = false });
            foreach (var (name, bitrate) in QualityPresets.All)
            {
                var b = bitrate;
                quality.DropDownItems.Add(new ToolStripMenuItem($"{name} — {bitrate / 1_000_000} Mbps", null, (_, _) => session?.ChangeSettings(new HostSettingsChange { Bitrate = b })) { Checked = s.Bitrate == bitrate });
            }
            if (QualityPresets.All.All(p => p.Bitrate != s.Bitrate))
                quality.DropDownItems.Add(new ToolStripMenuItem(QualityPresets.Title(s.Bitrate)) { Checked = true, Enabled = false });
            quality.DropDownItems.Add(new ToolStripSeparator());
            quality.DropDownItems.Add(new ToolStripMenuItem("Resolution") { Enabled = false });
            quality.DropDownItems.Add(new ToolStripMenuItem("Retina (sharpest)", null, (_, _) => session?.ChangeSettings(new HostSettingsChange { CaptureScale = 2 })) { Checked = s.CaptureScale >= 1.5 });
            quality.DropDownItems.Add(new ToolStripMenuItem("Standard", null, (_, _) => session?.ChangeSettings(new HostSettingsChange { CaptureScale = 1 })) { Checked = s.CaptureScale < 1.5 });
            quality.DropDownItems.Add(new ToolStripSeparator());
            quality.DropDownItems.Add(new ToolStripMenuItem("Frame Rate Limit") { Enabled = false });
            quality.DropDownItems.Add(new ToolStripMenuItem("60 fps", null, (_, _) => session?.ChangeSettings(new HostSettingsChange { MaxFPS = 60 })) { Checked = s.MaxFPS == 60 });
            quality.DropDownItems.Add(new ToolStripMenuItem("120 fps", null, (_, _) => session?.ChangeSettings(new HostSettingsChange { MaxFPS = 120 })) { Checked = s.MaxFPS == 120 });
            quality.DropDownItems.Add(new ToolStripSeparator());
            quality.DropDownItems.Add(new ToolStripMenuItem("Prioritize Encoding Speed", null, (_, _) => session?.ChangeSettings(new HostSettingsChange { PrioritizeSpeed = !s.PrioritizeSpeed })) { Checked = s.PrioritizeSpeed });
            if (state.VirtualDisplayAvailable)
                quality.DropDownItems.Add(new ToolStripMenuItem("Virtual Display", null, (_, _) => session?.ChangeSettings(new HostSettingsChange { VirtualDisplay = !s.VirtualDisplay })) { Checked = s.VirtualDisplay, ToolTipText = state.VirtualDisplayNote });
            if (state.SoftwareEncoder)
                quality.DropDownItems.Add(new ToolStripMenuItem("The Mac’s hardware encoder is busy: up to 60 fps at Standard for now") { Enabled = false });
            if (state.Link?.State == "behind")
                quality.DropDownItems.Add(new ToolStripMenuItem($"The link can’t carry {QualityPresets.Name(state.Link.Bitrate)}") { Enabled = false, ForeColor = Theme.Warning });
        }
        quality.DropDownItems.Add(new ToolStripSeparator());
        var fit = new ToolStripMenuItem("Size the Mac’s Window to This Window");
        foreach (var (label, scale) in new (string, double?)[] { ("Off", null), ("75%", 0.75), ("100%", 1.0), ("125%", 1.25), ("150%", 1.5) })
        {
            var sc = scale;
            fit.DropDownItems.Add(new ToolStripMenuItem(label, null, (_, _) => { fitScale = sc; SendViewport(); }) { Checked = fitScale == scale, ForeColor = Theme.Text });
        }
        quality.DropDownItems.Add(fit);
        foreach (ToolStripItem i in quality.DropDownItems) if (i.Enabled && i.ForeColor != Theme.Warning) i.ForeColor = Theme.Text; else if (!i.Enabled) i.ForeColor = Theme.SecondaryText;
    }

    // MARK: the View menu and Sill's shortcuts

    void BuildViewMenu()
    {
        view.DropDownItems.Clear();
        var s = Settings.Current;
        bool picture = panel.PictureWidth > 0;
        ToolStripMenuItem Item(string text, Keys? key, Action act, bool check = false, bool enabled = true, string? shortcut = null) =>
            new(text, null, (_, _) => act())
            {
                Checked = check, Enabled = enabled, ForeColor = enabled ? Theme.Text : Theme.SecondaryText,
                ShortcutKeyDisplayString = shortcut ?? (key is { } k ? KeyboardForwarder.HotkeyText(k) : null),
            };
        view.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("Full Screen", null, ToggleFullScreen, check: full, shortcut: "Ctrl+Alt+Enter"),
            Item("Always on Top", Keys.T, () => SetAlwaysOnTop(!s.AlwaysOnTop), check: s.AlwaysOnTop),
            Item("Fit Window to Picture", null, () => FitWindowToPicture(actualSize: false), enabled: picture && !full),
            Item("Actual Size", null, () => FitWindowToPicture(actualSize: true), enabled: picture && !full),
            Item("Show Stats", Keys.S, ToggleStats, check: s.ShowStats),
            new ToolStripSeparator(),
            Item("Take Screenshot", Keys.P, TakeScreenshot, enabled: picture),
            Item("Type the Clipboard’s Text on the Mac", Keys.V, TypeClipboardText, enabled: session != null && SafeContainsText()),
            new ToolStripSeparator(),
            Item("Minimize", Keys.M, MinimizeWindow),
        });
    }

    void OnHotkey(Keys key)
    {
        TestArguments.Log($"hotkey {key}");
        switch (key)
        {
            case Keys.F: ToggleFullScreen(); break;
            case Keys.D: session?.Select(Source.Desktop); break;
            case Keys.W: ShowWindowsPopup(); break;
            case Keys.P: TakeScreenshot(); break;
            case Keys.V: TypeClipboardText(); break;
            case Keys.S: ToggleStats(); break;
            case Keys.T: SetAlwaysOnTop(!Settings.Current.AlwaysOnTop); break;
            case Keys.M: MinimizeWindow(); break;
            case Keys.Q: Close(); break;
        }
    }

    void MinimizeWindow()
    {
        LetGo();
        WindowState = FormWindowState.Minimized;
    }

    void SetAlwaysOnTop(bool on)
    {
        Settings.Current.AlwaysOnTop = on;
        Settings.Save();
        if (!full) TopMost = on;
        ShowMessage(on ? "Sill stays in front of other windows" : "Sill no longer stays in front", hint: true);
    }

    void ToggleStats()
    {
        Settings.Current.ShowStats = !Settings.Current.ShowStats;
        Settings.Save();
        UpdateStats();
    }

    /// <summary>The window's shape made the picture's, so no black bars are left: the same width (or,
    /// with <paramref name="actualSize"/>, a picture pixel to a screen pixel), as far as the screen allows.</summary>
    void FitWindowToPicture(bool actualSize)
    {
        if (full || panel.PictureWidth <= 0 || panel.PictureHeight <= 0) return;
        if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
        var area = Screen.FromControl(this).WorkingArea;
        int chromeW = Width - panel.ClientSize.Width, chromeH = Height - panel.ClientSize.Height;
        double pw = panel.PictureWidth, ph = panel.PictureHeight;
        double w = actualSize ? pw : panel.ClientSize.Width, h = w * ph / pw;
        double k = Math.Min(1, Math.Min((area.Width - chromeW) / w, (area.Height - chromeH) / h));
        var size = new Size((int)Math.Round(w * k) + chromeW, (int)Math.Round(h * k) + chromeH);
        // The centre stays where it was, the whole window on its screen.
        int x = Math.Clamp(Left + (Width - size.Width) / 2, area.Left, Math.Max(area.Left, area.Right - size.Width));
        int y = Math.Clamp(Top + (Height - size.Height) / 2, area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        if (TestArguments.Offscreen) (x, y) = (Left, Top);
        Bounds = new Rectangle(x, y, size.Width, size.Height);
        TestArguments.Log($"fit: picture {panel.PictureWidth}x{panel.PictureHeight}, panel {panel.ClientSize.Width}x{panel.ClientSize.Height}{(k < 1 ? $" (the screen allows {k:0.00})" : "")}");
        if (actualSize && k < 1) ShowMessage($"The Mac’s picture is larger than this screen: shown at {k:P0}", hint: true);
    }

    // MARK: screenshots

    /// <summary>Where screenshots go: Pictures\Sill (a test run's own data folder under SILL_TEST_DATA).</summary>
    static string ScreenshotFolder => TestArguments.IsolatedData
        ? Path.Combine(AppData.Folder, "Screenshots")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Sill");

    /// <summary>The Mac's picture at its own size (not the window's), to a PNG and the clipboard.</summary>
    void TakeScreenshot()
    {
        if (video == null || panel.PictureWidth <= 0) { ShowMessage("There’s no picture to take yet.", hint: true); return; }
        var mac = session?.MacName ?? "Mac";
        video.Capture(bmp => BeginInvoke(() => SaveScreenshot(bmp, mac)));
    }

    void SaveScreenshot(Bitmap? bmp, string mac)
    {
        if (bmp == null) { ShowMessage("Couldn’t take a screenshot.", hint: true); return; }
        using (bmp)
        {
            try
            {
                var folder = ScreenshotFolder;
                Directory.CreateDirectory(folder);
                var safe = string.Concat(mac.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
                var stem = $"{(safe.Length > 0 ? safe : "Mac")} {DateTime.Now:yyyy-MM-dd} at {DateTime.Now:HH.mm.ss}";
                var path = Path.Combine(folder, stem + ".png");
                for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{stem} ({n}).png");
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                bool copied = false;
                if (!TestArguments.IsolatedData)
                {
                    try { Clipboard.SetImage(bmp); copied = true; }
                    catch (ExternalException) { }
                }
                TestArguments.Log($"screenshot {bmp.Width}x{bmp.Height} {path}");
                var where = folder.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), StringComparison.OrdinalIgnoreCase) ? @"Pictures\Sill" : folder;
                ShowMessage($"Screenshot saved to {where}{(copied ? " and copied" : "")}. Click to show it.", hint: true, path: path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ExternalException)
            {
                ShowMessage($"Couldn’t save the screenshot: {e.Message}", hint: true);
            }
        }
    }

    // MARK: typing the clipboard

    static bool SafeContainsText()
    {
        try { return Clipboard.ContainsText(); }
        catch (ExternalException) { return false; }
    }

    /// <summary>This PC's clipboard typed on the Mac, character by character, for a field that takes no
    /// paste (a login window, a password field, a VM's console) or a Mac that does not share its clipboard.</summary>
    void TypeClipboardText()
    {
        if (session == null) return;
        string? text = TestArguments.TypeText;
        if (text == null)
        {
            try { text = Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch (ExternalException) { }
        }
        if (string.IsNullOrEmpty(text)) { ShowMessage("The clipboard has no text to type.", hint: true); return; }
        _ = TypeText(session, text);
    }

    const int TypeLimit = 10_000;

    async Task TypeText(Session s, string text)
    {
        // The Mac types "\n" as Return; a Windows line end would be one character it cannot type.
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var elements = new List<string>();
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (elements.Count < TypeLimit && e.MoveNext()) elements.Add(e.GetTextElement());
        bool cut = e.MoveNext();
        // The characters carry no modifiers: whatever this PC still holds on the Mac comes up first.
        keyboard.ReleaseAll();
        ShowMessage($"Typing {elements.Count:N0} characters on the Mac…{(cut ? $" (the first {TypeLimit:N0})" : "")}", hint: true);
        TestArguments.Log($"typing {elements.Count} characters");
        // In small pieces, a little apart, so the Mac's apps keep up and no message is large.
        for (int i = 0; i < elements.Count; i += 32)
        {
            if (!ReferenceEquals(session, s) || IsDisposed) return;
            s.SendInput(InputEvents.Text(string.Concat(elements.Skip(i).Take(32))));
            await Task.Delay(20);
        }
    }

    bool scripted, acted;

    /// <summary>TEST (--actions): View menu actions and shortcuts at their seconds after the first picture,
    /// only with a test host on this PC's loopback, as the input script.</summary>
    async Task RunActions(Session s)
    {
        if (!System.Net.IPEndPoint.TryParse(s.Address, out var ep) || !System.Net.IPAddress.IsLoopback(ep.Address))
        {
            TestArguments.Log("actions: refused (not a loopback test host)");
            return;
        }
        var start = DateTime.UtcNow;
        foreach (var (at, name) in TestArguments.Actions!)
        {
            var wait = start + TimeSpan.FromSeconds(at) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            if (IsDisposed) return;
            TestArguments.Log($"action {name}");
            switch (name)
            {
                case "fit": FitWindowToPicture(actualSize: false); break;
                case "actual": FitWindowToPicture(actualSize: true); break;
                case "bar": ShowFullScreenBar(true); break;
                case "nobar": ShowFullScreenBar(false); break;
                case "back": keyboard.SendShortcut(KeyMap.LeftBracket, MacModifiers.Command); break;
                case "forward": keyboard.SendShortcut(KeyMap.RightBracket, MacModifiers.Command); break;
                case "bounds": TestArguments.Log($"bounds {Bounds} panel {panel.Bounds} full {full} top {TopMost} bar {bar.Visible} {bar.Bounds} stats '{statsOverlay.Text}' {statsOverlay.Visible}"); break;
                default:
                    if (name.Length == 1 && Enum.TryParse<Keys>(name, out var k) && KeyboardForwarder.Hotkeys.ContainsKey(k)) OnHotkey(k);
                    break;
            }
        }
    }

    /// <summary>TEST (--input-script): pointer, clicks, a scroll, keys and text, only to a test host on
    /// this PC's loopback, which draws and prints what it gets.</summary>
    async Task RunInputScript(Session s)
    {
        if (!System.Net.IPEndPoint.TryParse(s.Address, out var ep) || !System.Net.IPAddress.IsLoopback(ep.Address))
        {
            TestArguments.Log("input script: refused (not a loopback test host)");
            return;
        }
        TestArguments.Log("input script: start");
        await Task.Delay(500);
        for (int i = 0; i <= 20; i++) { s.SendInput(InputEvents.Pointer(PointerAction.Move, 0.2 + i * 0.02, 0.3)); await Task.Delay(16); }
        s.SendInput(InputEvents.Pointer(PointerAction.LeftDown, 0.6, 0.3)); await Task.Delay(300);
        s.SendInput(InputEvents.Pointer(PointerAction.LeftUp, 0.6, 0.3)); await Task.Delay(200);
        s.SendInput(InputEvents.Pointer(PointerAction.RightDown, 0.5, 0.6)); s.SendInput(InputEvents.Pointer(PointerAction.RightUp, 0.5, 0.6));
        OnWheel(0.5, 0.5, 0, -3);
        foreach (var usage in new ushort[] { 0x04, 0x05, 0x06 })
        {
            s.SendInput(InputEvents.Key(usage, true, 0)); s.SendInput(InputEvents.Key(usage, false, 0));
            await Task.Delay(150);
        }
        s.SendInput(InputEvents.Key(KeyMap.LeftCommand, true, (ulong)MacModifiers.Command));
        s.SendInput(InputEvents.Key(0x06, true, (ulong)MacModifiers.Command));
        s.SendInput(InputEvents.Key(0x06, false, (ulong)MacModifiers.Command));
        s.SendInput(InputEvents.Key(KeyMap.LeftCommand, false, 0));
        s.SendInput(InputEvents.Text("Привет, Mac"));
        s.SendClipboard("from Windows");
        s.ChangeSettings(new HostSettingsChange { Bitrate = 25_000_000 });
        TestArguments.Log("input script: done");
    }

    void UpdateStats()
    {
        var sess = session;
        if (sess != null && TestArguments.LogFile != null)
            TestArguments.Log($"figures fps {sess.Figures.fps} rtt {sess.Figures.rttMs:0.0} decoded {video?.Decoded} dropped {video?.Dropped} latency {video?.LastLatencyMs:0.0} ms");
        if (sess == null || !Settings.Current.ShowStats)
        {
            stats.Text = statsOverlay.Text = "";
            statsOverlay.Visible = false;
            return;
        }
        var (fps, rtt, _) = sess.Figures;
        var st = sess.Settings?.Stream;
        var parts = new List<string> { $"{fps} fps" };
        if (rtt >= 0) parts.Add($"{rtt:0} ms");
        if (st != null) parts.Add($"{st.Width}×{st.Height} · {st.Mbps} Mbps");
        if (video?.DecoderName is { Length: > 0 } dn) parts.Add(dn.Contains("GPU") ? "GPU" : "CPU");
        stats.Text = statsOverlay.Text = string.Join("  ·  ", parts);
        // In full screen the toolbar is away: the figures sit in the corner, out of the Mac's menu bar.
        statsOverlay.Visible = full && !bar.Visible;
        if (statsOverlay.Visible) statsOverlay.BringToFront();
    }

    // MARK: the viewport

    Viewport CurrentViewport()
    {
        double dips = DeviceDpi / 96.0;
        return new Viewport
        {
            Width = Math.Round(panel.ClientSize.Width / dips),
            Height = Math.Round(panel.ClientSize.Height / dips),
            Scale = fitScale,
            Fps = Math.Clamp(RefreshRate(), 30, 120),
        };
    }

    void SendViewport() => session?.SendViewport(CurrentViewport());

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);

    int RefreshRate()
    {
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        return EnumDisplaySettings(Screen.FromControl(this).DeviceName, -1, ref dm) && dm.dmDisplayFrequency > 1 ? dm.dmDisplayFrequency : 60;
    }

    // MARK: input

    void OnPointer(PointerAction action, double x, double y)
    {
        if (session == null) return;
        if (action == PointerAction.Move)
        {
            lock (moveGate)
            {
                long now = Stopwatch.GetTimestamp();
                if (now - lastMoveTicks >= Stopwatch.Frequency / 250)
                {
                    lastMoveTicks = now;
                    pendingMove = null;
                    session.SendInput(InputEvents.Pointer(PointerAction.Move, x, y));
                }
                else
                {
                    pendingMove = (x, y);
                    moveTimer ??= new System.Threading.Timer(_ => FlushMove());
                    moveTimer.Change(5, Timeout.Infinite);
                }
            }
            return;
        }
        FlushMove();
        if (action == PointerAction.LeftDown) buttonsDown |= MouseButtons.Left;
        else if (action == PointerAction.RightDown) buttonsDown |= MouseButtons.Right;
        else if (action == PointerAction.LeftUp) buttonsDown &= ~MouseButtons.Left;
        else if (action == PointerAction.RightUp) buttonsDown &= ~MouseButtons.Right;
        session.SendInput(InputEvents.Pointer(PointerAction.Move, x, y));
        session.SendInput(InputEvents.Pointer(action, x, y));
    }

    void FlushMove()
    {
        lock (moveGate)
        {
            if (pendingMove is not { } p) return;
            pendingMove = null;
            lastMoveTicks = Stopwatch.GetTimestamp();
            session?.SendInput(InputEvents.Pointer(PointerAction.Move, p.x, p.y));
        }
    }

    void OnWheel(double x, double y, double notchesX, double notchesY)
    {
        if (session == null || panel.PictureHeight <= 0) return;
        // A notch is ScrollPoints of the Mac's points; the frame is captureScale pixels a point.
        double scale = session.Settings?.Settings?.CaptureScale is double c && c > 0 ? c : panel.PictureHeight > 1300 ? 2 : 1;
        double heightPoints = panel.PictureHeight / scale, widthPoints = panel.PictureWidth / scale;
        double points = Settings.Current.ScrollPoints;
        // Natural sign (Input.swift): positive dy moves the content down, which a wheel turned away
        // from the user (Windows' positive delta) asks for; tilted right, the content moves left.
        double dy = notchesY * points / heightPoints, dx = -notchesX * points / widthPoints;
        FlushMove();
        session.SendInput(InputEvents.Scroll(x, y, dx, dy));
    }

    /// <summary>The window lost the keyboard and mouse: nothing stays held on the Mac.</summary>
    void LetGo()
    {
        keyboard.ReleaseAll();
        if (session != null && buttonsDown != MouseButtons.None)
        {
            if (buttonsDown.HasFlag(MouseButtons.Left)) session.SendInput(InputEvents.Pointer(PointerAction.LeftUp, 0.5, 0.5));
            if (buttonsDown.HasFlag(MouseButtons.Right)) session.SendInput(InputEvents.Pointer(PointerAction.RightUp, 0.5, 0.5));
            buttonsDown = MouseButtons.None;
        }
    }

    // MARK: the cursor

    void ApplyCursor()
    {
        if (cursorShape is not { } c || panel.PictureWidth <= 0) return;
        double scale = session?.Settings?.Settings?.CaptureScale is double s && s > 0 ? s : panel.PictureHeight > 1300 ? 2 : 1;
        float pixelsPerPoint = (float)(scale * panel.PictureRect.Width / panel.PictureWidth);
        var key = $"{Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(c.png))}/{pixelsPerPoint:0.00}";
        if (!cursors.TryGetValue(key, out var cursor))
        {
            cursor = MacCursor.Make(c.png, c.hotX, c.hotY, c.width, c.height, pixelsPerPoint) ?? Cursors.Default;
            if (cursors.Count > 64) cursors.Clear();
            cursors[key] = cursor;
        }
        panel.Cursor = cursor;
    }

    // MARK: the clipboard

    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);

    /// <summary>Text copied on this PC since Sill last looked goes to the Mac's clipboard when the
    /// stream comes forward, as a device's Paste sends it; never what a password manager marks.</summary>
    void SyncClipboardToMac()
    {
        if (session == null || !Settings.Current.ShareClipboard) return;
        uint seq = GetClipboardSequenceNumber();
        if (seq == lastClipboardSequence) return;
        bool first = lastClipboardSequence == 0;
        lastClipboardSequence = seq;
        if (first) return;   // what was there when the session began stays this PC's
        try
        {
            if (IsClipboardFormatAvailable(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing")) ||
                IsClipboardFormatAvailable(RegisterClipboardFormat("Clipboard Viewer Ignore"))) return;
            if (!Clipboard.ContainsText()) return;
            var text = Clipboard.GetText();
            if (string.IsNullOrEmpty(text) || text == lastFromMac) return;
            session.SendClipboard(text);
        }
        catch (ExternalException) { }
    }

    void ReceiveClipboard(string text)
    {
        if (!Settings.Current.ShareClipboard) return;
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetText(text);
                lastFromMac = text;
                lastClipboardSequence = GetClipboardSequenceNumber();
                return;
            }
            catch (ExternalException) { Thread.Sleep(20); }
        }
    }

    // MARK: full screen

    void ToggleFullScreen()
    {
        if (!full)
        {
            normalState = WindowState;
            if (WindowState is FormWindowState.Maximized or FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            normalBounds = Bounds;
            full = true;
            bar.Visible = false;
            FormBorderStyle = FormBorderStyle.None;
            // TEST: an --offscreen run's full screen stays off every display.
            Bounds = TestArguments.Offscreen ? new Rectangle(-6000, 80, 1440, 900) : Screen.FromControl(this).Bounds;
            TopMost = false;
            edgeSince = 0;
            edgeTimer.Start();
            ShowMessage("Ctrl+Alt+Enter leaves full screen · the toolbar is at the top edge’s middle", hint: true);
        }
        else
        {
            full = false;
            edgeTimer.Stop();
            FormBorderStyle = FormBorderStyle.Sizable;
            bar.Visible = true;
            Bounds = normalBounds;
            WindowState = normalState;
            TopMost = Settings.Current.AlwaysOnTop;
            if (message.Tag as string == "hint") ShowMessage(null);
        }
        fullScreen.Checked = full;
        UpdateStats();
        panel.Focus();
    }

    /// <summary>Full screen: the toolbar over the top of the picture, or away. Over it (OnLayout keeps
    /// the picture at the whole screen), so the picture and the Mac's viewport never change size for it.</summary>
    void ShowFullScreenBar(bool on)
    {
        if (!full) return;
        edgeSince = 0;
        bar.Visible = on;
        if (on) bar.BringToFront();
        else if (menusOpen == 0) panel.Focus();
        UpdateStats();
    }

    /// <summary>Full screen, ten times a second: the pointer resting a moment at the middle of the top
    /// edge (clear of the Mac's menus on the left and its status items on the right) brings the toolbar
    /// down; it goes once the pointer is well below it and no menu of it is open.</summary>
    void EdgeTick()
    {
        if (!full) { edgeTimer.Stop(); return; }
        if (ActiveForm != this) { edgeSince = 0; return; }
        var p = PointToClient(Cursor.Position);
        if (!bar.Visible)
        {
            bool atEdge = p.Y is >= 0 and <= 1 && p.X >= ClientSize.Width * 0.3 && p.X <= ClientSize.Width * 0.7;
            if (!atEdge) { edgeSince = 0; return; }
            if (edgeSince == 0) edgeSince = Environment.TickCount64;
            else if (Environment.TickCount64 - edgeSince >= 300) ShowFullScreenBar(true);
        }
        else if (menusOpen == 0 && (p.Y > bar.Height + (int)(48 * DeviceDpi / 96.0) || p.Y < 0 || p.X < 0 || p.X > ClientSize.Width))
        {
            ShowFullScreenBar(false);
        }
    }

    // MARK: closing

    /// <summary>Closes for a session that ended for good (the connect screen shows why).</summary>
    public void CloseForEnd()
    {

        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // As it is now, for the next session: its normal bounds, maximized, full screen.
        var s = Settings.Current;
        var b = full ? normalBounds : WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        s.WindowMaximized = full ? normalState == FormWindowState.Maximized : WindowState == FormWindowState.Maximized;
        s.WindowFullScreen = full;
        s.WindowWidth = b.Width; s.WindowHeight = b.Height;
        s.WindowX = b.X; s.WindowY = b.Y;
        Settings.Save();
        edgeTimer.Stop();
        LetGo();
        keyboard.Dispose();
        var sess = session;
        session = null;
        sess?.Dispose();
        video?.Dispose();
        video = null;
        moveTimer?.Dispose();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        Disconnected?.Invoke();
    }
}

/// <summary>The toolbar and menus in the dark theme.</summary>
public sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? (e.Item.ForeColor == Theme.Warning ? Theme.Warning : Theme.Text) : Theme.SecondaryText;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.SecondaryText;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        using var b = new SolidBrush(Theme.Accent);
        using var pen = new Pen(Theme.Text, 2f);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if (e.Item is ToolStripMenuItem { Image: not null })
        {
            using var p = new Pen(Theme.Accent, 2f);
            e.Graphics.DrawRectangle(p, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
            return;
        }
        e.Graphics.DrawLines(pen, new[] { new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.55f), new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.75f), new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.28f) });
    }
}

sealed class DarkColors : ProfessionalColorTable
{
    public override Color ToolStripGradientBegin => Theme.Background;
    public override Color ToolStripGradientMiddle => Theme.Background;
    public override Color ToolStripGradientEnd => Theme.Background;
    public override Color ToolStripBorder => Theme.Background;
    public override Color ToolStripDropDownBackground => Theme.Surface;
    public override Color MenuBorder => Theme.Separator;
    public override Color MenuItemBorder => Theme.Accent;
    public override Color MenuItemSelected => Theme.SurfaceSelected;
    public override Color MenuItemSelectedGradientBegin => Theme.SurfaceSelected;
    public override Color MenuItemSelectedGradientEnd => Theme.SurfaceSelected;
    public override Color MenuItemPressedGradientBegin => Theme.Surface;
    public override Color MenuItemPressedGradientEnd => Theme.Surface;
    public override Color ImageMarginGradientBegin => Theme.Surface;
    public override Color ImageMarginGradientMiddle => Theme.Surface;
    public override Color ImageMarginGradientEnd => Theme.Surface;
    public override Color SeparatorDark => Theme.Separator;
    public override Color SeparatorLight => Theme.Separator;
    public override Color ButtonSelectedHighlight => Theme.SurfaceHover;
    public override Color ButtonSelectedGradientBegin => Theme.SurfaceHover;
    public override Color ButtonSelectedGradientMiddle => Theme.SurfaceHover;
    public override Color ButtonSelectedGradientEnd => Theme.SurfaceHover;
    public override Color ButtonSelectedBorder => Theme.SurfaceHover;
    public override Color ButtonPressedGradientBegin => Theme.Separator;
    public override Color ButtonPressedGradientMiddle => Theme.Separator;
    public override Color ButtonPressedGradientEnd => Theme.Separator;
    public override Color ButtonPressedBorder => Theme.Separator;
    public override Color ButtonCheckedGradientBegin => Theme.SurfaceSelected;
    public override Color ButtonCheckedGradientMiddle => Theme.SurfaceSelected;
    public override Color ButtonCheckedGradientEnd => Theme.SurfaceSelected;
    public override Color ButtonCheckedHighlight => Theme.SurfaceSelected;
    public override Color CheckBackground => Theme.SurfaceSelected;
    public override Color CheckSelectedBackground => Theme.SurfaceSelected;
    public override Color CheckPressedBackground => Theme.SurfaceSelected;
}
