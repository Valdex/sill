using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Sill.Windows.UI;

/// <summary>Sill's look on Windows: dark, like the stream around it, with Windows 11's dark title bar
/// and rounded corners, Segoe UI, and Apple's dark-mode blue for the one action that matters.</summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(28, 28, 30);
    public static readonly Color Surface = Color.FromArgb(44, 44, 46);
    public static readonly Color SurfaceHover = Color.FromArgb(58, 58, 60);
    public static readonly Color SurfaceSelected = Color.FromArgb(10, 72, 140);
    public static readonly Color Separator = Color.FromArgb(56, 56, 58);
    public static readonly Color Text = Color.FromArgb(245, 245, 247);
    public static readonly Color SecondaryText = Color.FromArgb(152, 152, 159);
    public static readonly Color Accent = Color.FromArgb(10, 132, 255);
    public static readonly Color AccentPressed = Color.FromArgb(0, 104, 214);
    public static readonly Color Warning = Color.FromArgb(255, 159, 10);
    public static readonly Color Good = Color.FromArgb(48, 209, 88);

    static readonly string Family = FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
    static readonly string DisplayFamily = FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";

    public static Font Body { get; } = new(Family, 10f);
    public static Font Small { get; } = new(Family, 9f);
    public static Font BodyBold { get; } = new(Family, 10.5f, FontStyle.Bold);
    public static Font Title { get; } = new(DisplayFamily, 20f, FontStyle.Bold);
    public static Font Heading { get; } = new(DisplayFamily, 13f, FontStyle.Bold);

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>A dark title bar and Windows 11's rounded corners for a form.</summary>
    public static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Text;
        form.Font = Body;
        form.HandleCreated += (_, _) =>
        {
            int on = 1;
            DwmSetWindowAttribute(form.Handle, 20, ref on, 4);          // DWMWA_USE_IMMERSIVE_DARK_MODE
            int round = 2;
            DwmSetWindowAttribute(form.Handle, 33, ref round, 4);       // DWMWA_WINDOW_CORNER_PREFERENCE: round
        };
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Icon? AppIcon { get; } = LoadIcon();

    static Icon? LoadIcon()
    {
        try
        {
            using var s = typeof(Theme).Assembly.GetManifestResourceStream("Sill.Windows.Sill.ico");
            return s == null ? null : new Icon(s);
        }
        catch (ArgumentException) { return null; }
    }

    public static Bitmap? AppImage(int size)
    {
        if (AppIcon == null) return null;
        using var icon = new Icon(AppIcon, size, size);
        return icon.ToBitmap();
    }
}

/// <summary>A flat, rounded button in the theme: the accent for the primary action, a surface otherwise.</summary>
public sealed class ThemedButton : Button
{
    // Set in code only: the designer never writes it (WFO1000 in .NET 10's analyzers).
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }
    bool hover, pressed;

    public ThemedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = Theme.Body;
        ForeColor = Theme.Text;
        Height = 34;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        var fill = !Enabled ? Theme.Surface : Primary ? (pressed ? Theme.AccentPressed : hover ? Color.FromArgb(40, 150, 255) : Theme.Accent)
                                            : pressed ? Theme.Separator : hover ? Theme.SurfaceHover : Theme.Surface;
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 7))
        using (var b = new SolidBrush(fill))
            g.FillPath(b, path);
        if (Focused && ShowFocusCues)
            using (var path = Theme.Rounded(new RectangleF(1.5f, 1.5f, Width - 3, Height - 3), 6))
            using (var pen = new Pen(Color.FromArgb(160, 255, 255, 255), 1.5f))
                g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? Theme.Text : Theme.SecondaryText,
                              TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
