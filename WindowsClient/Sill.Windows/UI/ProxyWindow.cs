using System.Runtime.InteropServices;
using Sill.Windows.Coherence;
using Sill.Windows.Protocol;
using Sill.Windows.Shell;
using Sill.Windows.Video;

namespace Sill.Windows.UI;

/// <summary>The part of a Coherence window that shows its Mac window's picture: Direct3D draws into its
/// handle (its own swap chain, the pipeline's), and the mouse over it goes to that Mac window as
/// fractions of the whole picture (<see cref="ProxyGeometry.PointerFraction"/>: the Mac's title bar,
/// cropped off the top, still counts). The local pointer takes the Mac's cursor shape (kind 14) in
/// the client area, at WM_SETCURSOR, so it never lags.</summary>
sealed class ProxySurface : Control
{
    const int WM_MOUSEHWHEEL = 0x020E, WM_MOUSEWHEEL = 0x020A, WM_ERASEBKGND = 0x14, WM_SETCURSOR = 0x20, HTCLIENT = 1;

    [DllImport("user32.dll")] static extern IntPtr SetCursor(IntPtr cursor);

    /// <summary>The share of the picture's height the title bar's crop takes.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double CropFraction { get; set; }
    /// <summary>The Mac's cursor at this window's scale; null: the arrow.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Cursor? MacCursor { get; set; }

    public event Action<PointerAction, double, double>? Pointer;
    public event Action<double, double, double, double>? Wheel;

    public ProxySurface()
    {
        SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, false);
        BackColor = Color.Black;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20;   // CS_OWNDC: the swap chain keeps this window
            return cp;
        }
    }

    (double x, double y) Fraction(Point p) => ProxyGeometry.PointerFraction(p.X, p.Y, ClientSize.Width, ClientSize.Height, CropFraction);

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var (x, y) = Fraction(e.Location);
        Pointer?.Invoke(PointerAction.Move, x, y);
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var (x, y) = Fraction(e.Location);
        if (e.Button == MouseButtons.Left) Pointer?.Invoke(PointerAction.LeftDown, x, y);
        else if (e.Button == MouseButtons.Right) Pointer?.Invoke(PointerAction.RightDown, x, y);
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        var (x, y) = Fraction(e.Location);
        if (e.Button == MouseButtons.Left) Pointer?.Invoke(PointerAction.LeftUp, x, y);
        else if (e.Button == MouseButtons.Right) Pointer?.Invoke(PointerAction.RightUp, x, y);
        base.OnMouseUp(e);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_ERASEBKGND:
                m.Result = (IntPtr)1;
                return;
            case WM_SETCURSOR when ((long)m.LParam & 0xFFFF) == HTCLIENT && MacCursor != null:
                SetCursor(MacCursor.Handle);
                m.Result = (IntPtr)1;
                return;
            case WM_MOUSEWHEEL:
            case WM_MOUSEHWHEEL:
            {
                int delta = (short)((long)m.WParam >> 16);
                var screen = new Point((short)(long)m.LParam, (short)((long)m.LParam >> 16));
                var (x, y) = Fraction(PointToClient(screen));
                if (m.Msg == WM_MOUSEWHEEL) Wheel?.Invoke(x, y, 0, delta / 120.0);
                else Wheel?.Invoke(x, y, delta / 120.0, 0);
                m.Result = IntPtr.Zero;
                return;
            }
        }
        base.WndProc(ref m);
    }
}

/// <summary>What a Coherence window and a popup share: its Mac window's state, a surface and a video
/// pipeline of its own (one HEVC stream per Mac window, its picture filling the surface, the Mac's
/// title bar cropped off the top), and the routing of its stream and its mouse through the host.</summary>
abstract class MacWindowForm : Form
{
    protected readonly CoherenceHost host;
    protected readonly ProxySurface surface = new() { Dock = DockStyle.Fill };
    protected VideoPipeline? video;
    protected int pictureW, pictureH, crop;
    protected bool closingForGood;
    double? captureScale;

    public uint Id { get; }
    // Set in code only: the designer never writes it (WFO1000).
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public MacWindow State { get; protected set; }
    public ProxySurface Surface => surface;
    /// <summary>This window's pixels per point: its monitor's DPI scale.</summary>
    public double Dpi => DeviceDpi / 96.0;
    public (int w, int h) Picture => (pictureW, pictureH);
    public int Crop => crop;
    public VideoPipeline? Video => video;
    /// <summary>The Mac window's size in points: a top-level window's `size`, a child's `rect`.</summary>
    public (double w, double h)? MacPoints => State.SizePoints ?? (State.Rect is { Length: >= 4 } r && r[2] > 0 && r[3] > 0 ? (r[2], r[3]) : null);

    protected MacWindowForm(CoherenceHost host, MacWindow state)
    {
        this.host = host;
        Id = state.Id;
        State = state.Clone();
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Font = Theme.Body;
        surface.Pointer += (a, x, y) => host.Pointer(this, a, x, y);
        surface.Wheel += (x, y, nx, ny) => host.Wheel(this, x, y, nx, ny);
        surface.MouseLeave += (_, _) => host.FlushMove();
    }

    sealed class Sink : IWindowVideo
    {
        readonly VideoPipeline v;
        public Sink(VideoPipeline v) { this.v = v; }
        public void Submit(ParameterSets sets) => v.Submit(sets);
        public void Submit(byte[] frame, bool key, double timestamp) => v.Submit(frame, key, timestamp);
    }

    /// <summary>The pipeline, once the surface has its handle and size; then the stream is routed to it.</summary>
    protected void StartVideo()
    {
        if (video != null) return;
        var v = new VideoPipeline(surface.Handle, Math.Max(1, surface.ClientSize.Width), Math.Max(1, surface.ClientSize.Height), fill: true, $"Sill video {Id}");
        video = v;
        v.PictureSizeChanged += (w, h) => Post(() => OnPictureSize(w, h));
        v.Interrupted += _ => host.Controller.Interrupted(Id);
        v.PictureBack += () => host.Controller.PictureBack(Id);
        v.Problem += p => TestArguments.Log($"window {Id}: {p}");
        v.PictureShown += () => TestArguments.Log($"window {Id}: picture shown");
        if (crop > 0) v.SetCrop(crop);
        host.Controller.AttachVideo(Id, new Sink(v));
    }

    protected void Post(Action a)
    {
        if (!IsHandleCreated || IsDisposed) return;
        try { BeginInvoke(a); } catch (InvalidOperationException) { }
    }

    /// <summary>A frame of a new size came: the crop follows it, and the swap chain's buffers take the
    /// surface's size now (until then the last frame was stretched to it).</summary>
    protected virtual void OnPictureSize(int w, int h)
    {
        pictureW = w; pictureH = h;
        TestArguments.Log($"window {Id}: picture {w}x{h}");
        UpdateCrop();
        ResizeBuffers();
    }

    protected void ResizeBuffers() => video?.Resize(Math.Max(1, surface.ClientSize.Width), Math.Max(1, surface.ClientSize.Height));

    /// <summary>The title bar's rows, at the picture's own pixels per point (learned from a picture
    /// whose two sides agree with the Mac's size), else this PC's scale.</summary>
    protected void UpdateCrop()
    {
        if (MacPoints is { } pt) captureScale = ProxyGeometry.CaptureScale(pictureW, pictureH, pt.w, pt.h, captureScale);
        int c = ProxyGeometry.CropPixels(State.TitleBar, captureScale ?? host.Scale, pictureH);
        surface.CropFraction = ProxyGeometry.CropFraction(c, pictureH);
        if (c == crop) return;
        crop = c;
        video?.SetCrop(c);
    }

    /// <summary>The Mac dropped the window, or Coherence ends: close for good (a close box only asks).</summary>
    public void ForceClose()
    {
        closingForGood = true;
        if (!IsDisposed) Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        host.Controller.DetachVideo(Id);
        video?.Dispose();
        video = null;
        host.Closed(this);
        base.OnFormClosed(e);
    }
}

/// <summary>One top-level Mac window (or a sheet or dialog that spills out of its owner) as a native
/// overlapped Windows window: the Mac's plain title bar cropped and the Mac's title as this window's
/// (so Snap, Alt-Tab, the taskbar, rounded corners and the shadow are Windows' own), a dark DWM frame,
/// the Mac app's icon, and its AppUserModelID so the Mac app's windows group on the taskbar.
/// <list type="bullet">
/// <item>Activation: the Mac makes the window key (kind 15 `activate`) and it becomes the focus.</item>
/// <item>A move is this PC's alone.</item>
/// <item>A resize stretches the last frame (the swap chain's buffers are left alone at each WM_SIZE),
///   sends kind 46 at most every 50 ms and at WM_EXITSIZEMOVE, and the buffers take the new size when a
///   frame of it comes (or 400 ms after the last change, if the Mac keeps the old size).</item>
/// <item>WM_GETMINMAXINFO follows the Mac's `min` and `max`; a maximize is a kind 46 to the work area;
///   a minimize hides the window's stream (kind 29's `hidden`).</item>
/// <item>The close box asks the Mac (kind 15 `close`); the window stays until the Mac drops it, as a
///   "Save changes?" sheet may come first.</item>
/// </list></summary>
sealed class ProxyWindow : MacWindowForm
{
    const int WM_GETMINMAXINFO = 0x24, WM_ENTERSIZEMOVE = 0x231, WM_EXITSIZEMOVE = 0x232, WM_ACTIVATEAPP = 0x1C, WM_DESTROY = 0x2;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    readonly MenuStrip strip;
    readonly System.Windows.Forms.Timer settle = new() { Interval = 400 };
    readonly bool owned;
    bool inSizeMove, applying, wasHidden, activateOnShow, stripStale;
    int menusOpen;

    public bool InSizeMove => inSizeMove;
    public bool MenuOpen => menusOpen > 0;
    public bool Owned => owned;
    public MenuStrip Strip => strip;

    public ProxyWindow(CoherenceHost host, MacWindow state, ProxyWindow? owner, bool activate) : base(host, state)
    {
        owned = owner != null;
        activateOnShow = activate;
        strip = new MenuStrip
        {
            Dock = DockStyle.Top, Renderer = new DarkRenderer(), BackColor = Theme.Background, ForeColor = Theme.Text, Font = Theme.Body,
            GripStyle = ToolStripGripStyle.Hidden, Visible = false, Padding = new Padding(4, 2, 0, 2),
        };
        // Docked in reverse order of the controls: the strip (added last) takes the top, the surface the rest.
        Controls.Add(surface);
        Controls.Add(strip);
        if (owner != null)
        {
            Owner = owner;
            ShowInTaskbar = false;
        }
        Text = State.DisplayTitle;
        Icon = host.IconFor(State.App) ?? Theme.AppIcon;
        ApplyStyle();
        strip.MenuActivate += (_, _) => { menusOpen++; host.KeyboardChanged(); };
        strip.MenuDeactivate += (_, _) =>
        {
            menusOpen = Math.Max(0, menusOpen - 1);
            host.KeyboardChanged();
            if (stripStale) RefreshMenus();
        };
        settle.Tick += (_, _) => { settle.Stop(); ResizeBuffers(); };
        // The size before the handle exists, at the scale of the monitor it opens on; made exact at Load.
        if (State.SizePoints is { } sz)
        {
            var s = ProxyGeometry.SurfaceSize(sz.w, sz.h, State.TitleBar, host.Scale);
            ClientSize = s;
        }
        else ClientSize = new Size(800, 560);
    }

    protected override bool ShowWithoutActivation => !activateOnShow;

    /// <summary>The window's height that is not the surface: the menu strip when it shows.</summary>
    int StripHeight => strip.Visible ? strip.Height : 0;
    /// <summary>The surface's size, from the client area (the dock's layout may not have run yet).</summary>
    public Size SurfaceSize => new(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height - StripHeight));

    void ApplyStyle()
    {
        bool resizable = State.Resizable != false;
        bool sheet = owned && State.Role == Protocol.WindowState.Roles.Sheet;
        FormBorderStyle = sheet ? FormBorderStyle.FixedDialog : resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedSingle;
        MaximizeBox = resizable && !owned;
        MinimizeBox = !owned;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 11's dark frame, its caption the stream's dark grey, its corners round.
        int on = 1, round = 2, caption = ColorRef(Theme.Background), text = ColorRef(Theme.Text);
        DwmSetWindowAttribute(Handle, 20, ref on, 4);        // DWMWA_USE_IMMERSIVE_DARK_MODE
        DwmSetWindowAttribute(Handle, 33, ref round, 4);     // DWMWA_WINDOW_CORNER_PREFERENCE
        DwmSetWindowAttribute(Handle, 35, ref caption, 4);   // DWMWA_CAPTION_COLOR
        DwmSetWindowAttribute(Handle, 36, ref text, 4);      // DWMWA_TEXT_COLOR
        // Before the window is first shown, so its taskbar button is the Mac app's from the start.
        host.SetTaskbarIdentity(this);
    }

    static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Now at the DPI of the monitor it is on.
        if (State.SizePoints is { } sz) ApplyMacSize(sz.w, sz.h);
        UpdateCrop();
        StartVideo();
        RefreshMenus();
        host.LayoutChildren(this);
    }

    /// <summary>The window takes the Mac window's size (its surface: the width, the height less the title bar).</summary>
    void ApplyMacSize(double w, double h)
    {
        if (WindowState != FormWindowState.Normal) return;
        var s = ProxyGeometry.SurfaceSize(w, h, State.TitleBar, Dpi);
        applying = true;
        try { ClientSize = new Size(s.Width, s.Height + StripHeight); }
        finally { applying = false; }
        host.Controller.Settled(Id, w, h);
        ResizeBuffers();
        host.LayoutChildren(this);
    }

    /// <summary>A kind 45 changed this window.</summary>
    public void Update(MacWindow state, WindowChange change, int? answering)
    {
        State = state.Clone();
        if (change.HasFlag(WindowChange.Title) || change.HasFlag(WindowChange.App)) Text = State.DisplayTitle;
        if (change.HasFlag(WindowChange.Limits)) ApplyStyle();
        if (change.HasFlag(WindowChange.App)) { Icon = host.IconFor(State.App) ?? Theme.AppIcon; RefreshMenus(); }
        if (change.HasFlag(WindowChange.TitleBar) || change.HasFlag(WindowChange.Size)) UpdateCrop();
        if (change.HasFlag(WindowChange.Size) || change.HasFlag(WindowChange.TitleBar)) SyncToMac(answering);
    }

    /// <summary>The Mac's size applies when the throttle lets it (no drag, no size of ours waiting but
    /// for this answer) and differs from this window's by more than a point.</summary>
    public void SyncToMac(int? answering)
    {
        if (State.SizePoints is not { } sz) return;
        if (!host.Controller.TakesMacSize(Id, answering, inSizeMove)) return;
        var now = ProxyGeometry.MacSize(SurfaceSize.Width, SurfaceSize.Height, State.TitleBar, Dpi);
        if (Math.Abs(now.w - sz.w) > 1 || Math.Abs(now.h - sz.h) > 1) ApplyMacSize(sz.w, sz.h);
        else host.Controller.Settled(Id, sz.w, sz.h);
    }

    /// <summary>The strip shows the Mac app's menus when the Mac has sent this app's top level.</summary>
    public void RefreshMenus()
    {
        if (menusOpen > 0) { stripStale = true; return; }
        stripStale = false;
        var items = host.Menus.TopItems(State.App);
        strip.Items.Clear();
        strip.Items.AddRange(items.ToArray());
        SetStripVisible(items.Count > 0);
    }

    void SetStripVisible(bool on)
    {
        if (strip.Visible == on) return;
        var surf = SurfaceSize;
        applying = true;
        try
        {
            strip.Visible = on;
            if (WindowState == FormWindowState.Normal) ClientSize = new Size(surf.Width, surf.Height + (on ? strip.Height : 0));
        }
        finally { applying = false; }
        if (WindowState == FormWindowState.Maximized) SendSize(final: true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!IsHandleCreated || applying) return;
        bool hidden = WindowState == FormWindowState.Minimized;
        if (hidden != wasHidden)
        {
            wasHidden = hidden;
            host.HiddenChanged(this);
        }
        if (hidden) return;
        SendSize(final: !inSizeMove);
        settle.Stop();
        settle.Start();
        host.LayoutChildren(this);
    }

    void SendSize(bool final)
    {
        var mac = ProxyGeometry.MacSize(SurfaceSize.Width, SurfaceSize.Height, State.TitleBar, Dpi);
        host.Controller.Resized(Id, mac.w, mac.h, final);
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        if (IsHandleCreated) host.LayoutChildren(this);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // WinForms takes the suggested rectangle and scales the strip; the Mac's size in points stays.
        applying = true;
        try { base.OnDpiChanged(e); }
        finally { applying = false; }
        UpdateCrop();
        ResizeBuffers();
        host.DpiChanged(this);
        host.LayoutChildren(this);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        host.ProxyActivated(this);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        host.ProxyDeactivated(this);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The close box asks the Mac; the window goes when the Mac drops it.
        if (!closingForGood && e.CloseReason is CloseReason.UserClosing or CloseReason.None)
        {
            e.Cancel = true;
            host.Controller.Close(Id);
            TestArguments.Log($"window {Id}: close asked of the Mac");
            return;
        }
        settle.Stop();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        settle.Dispose();
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_GETMINMAXINFO:
                base.WndProc(ref m);
                ApplyLimits(m.LParam);
                return;
            case WM_ENTERSIZEMOVE:
                inSizeMove = true;
                break;
            case WM_EXITSIZEMOVE:
                inSizeMove = false;
                base.WndProc(ref m);
                if (WindowState == FormWindowState.Normal) SendSize(final: true);
                return;
            case WM_ACTIVATEAPP when m.WParam == IntPtr.Zero:
                host.AppDeactivated();
                break;
            case WM_DESTROY:
                TaskbarIdentity.Clear(Handle);
                break;
        }
        base.WndProc(ref m);
    }

    /// <summary>MINMAXINFO: ptMaxSize at 8, ptMinTrackSize at 24, ptMaxTrackSize at 32 (POINTs).</summary>
    void ApplyLimits(IntPtr info)
    {
        var chrome = new Size(Width - SurfaceSize.Width, Height - SurfaceSize.Height);
        var (min, max) = ProxyGeometry.TrackLimits(State, Dpi, chrome);
        if (min is { } a)
        {
            Marshal.WriteInt32(info, 24, a.Width);
            Marshal.WriteInt32(info, 28, a.Height);
        }
        if (max is { } b)
        {
            Marshal.WriteInt32(info, 32, b.Width);
            Marshal.WriteInt32(info, 36, b.Height);
            Marshal.WriteInt32(info, 8, Math.Min(Marshal.ReadInt32(info, 8), b.Width));
            Marshal.WriteInt32(info, 12, Math.Min(Marshal.ReadInt32(info, 12), b.Height));
        }
    }
}
