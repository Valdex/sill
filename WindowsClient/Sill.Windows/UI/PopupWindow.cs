namespace Sill.Windows.UI;

/// <summary>A Mac menu, popover or tooltip that spills out of its owner's picture (kind 45's `inCrop`
/// false), streamed as a side stream of its own: an owned popup (WS_POPUP) placed at its owner's
/// position plus its `rect`, above its owner and moving with it, that never takes activation
/// (WS_EX_NOACTIVATE, MA_NOACTIVATE) or a taskbar button (WS_EX_TOOLWINDOW), so the owner keeps the
/// keyboard and Windows' focus while the Mac's menu is used with the mouse, and the Mac's keys (the
/// arrows, Return, Esc) still reach it through the owner.</summary>
sealed class PopupWindow : MacWindowForm
{
    const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
    const int WS_POPUP = unchecked((int)0x80000000), WS_CLIPSIBLINGS = 0x04000000, WS_CLIPCHILDREN = 0x02000000;
    const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80, CS_DROPSHADOW = 0x20000;

    public PopupWindow(CoherenceHost host, Coherence.MacWindow state, Form owner) : base(host, state)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        Owner = owner;
        Text = State.DisplayTitle;
        Controls.Add(surface);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style = WS_POPUP | WS_CLIPSIBLINGS | WS_CLIPCHILDREN;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        UpdateCrop();
        StartVideo();
    }

    /// <summary>A kind 45 changed it (its rect, its role).</summary>
    public void Update(Coherence.MacWindow state)
    {
        State = state.Clone();
        Text = State.DisplayTitle;
        UpdateCrop();
    }

    /// <summary>Where it goes on screen; the buffers follow a new size at once (a popup is rarely resized).</summary>
    public void Place(Rectangle bounds)
    {
        if (Bounds == bounds) return;
        bool resized = Size != bounds.Size;
        Bounds = bounds;
        if (resized) ResizeBuffers();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)MA_NOACTIVATE;
            return;
        }
        base.WndProc(ref m);
    }
}
