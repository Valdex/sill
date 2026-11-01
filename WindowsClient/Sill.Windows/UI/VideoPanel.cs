using System.Runtime.InteropServices;
using Sill.Windows.Protocol;

namespace Sill.Windows.UI;

/// <summary>The picture's window: Direct3D draws into its handle (WinForms paints nothing), and the
/// mouse over it becomes the Mac's pointer. Positions go out as fractions of the picture, as every
/// Sill device sends them; the black bars around it clamp to its edge.</summary>
public sealed class VideoPanel : Control
{
    const int WM_MOUSEHWHEEL = 0x020E, WM_MOUSEWHEEL = 0x020A, WM_ERASEBKGND = 0x14;

    public int PictureWidth { get; set; }
    public int PictureHeight { get; set; }

    /// <summary>A pointer event at fractions of the picture.</summary>
    public event Action<PointerAction, double, double>? Pointer;
    /// <summary>A wheel: the position, then the notches across and down (Windows' sign: down is toward the user).</summary>
    public event Action<double, double, double, double>? Wheel;
    /// <summary>A mouse's side button went down: true for forward (XButton2), false for back.</summary>
    public event Action<bool>? SideButton;

    public VideoPanel()
    {
        SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Selectable, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
        BackColor = Color.Black;
        TabStop = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20;   // CS_OWNDC: Direct3D's swap chain keeps this window
            return cp;
        }
    }

    /// <summary>Where the picture is in the panel: the renderer's fit, whole pixels.</summary>
    public RectangleF PictureRect
    {
        get
        {
            if (PictureWidth <= 0 || PictureHeight <= 0) return new RectangleF(0, 0, Width, Height);
            float scale = Math.Min((float)Width / PictureWidth, (float)Height / PictureHeight);
            float dw = MathF.Round(PictureWidth * scale), dh = MathF.Round(PictureHeight * scale);
            return new RectangleF(MathF.Floor((Width - dw) / 2), MathF.Floor((Height - dh) / 2), dw, dh);
        }
    }

    (double x, double y) Fraction(Point p)
    {
        var r = PictureRect;
        double x = r.Width > 0 ? (p.X + 0.5 - r.X) / r.Width : 0.5;
        double y = r.Height > 0 ? (p.Y + 0.5 - r.Y) / r.Height : 0.5;
        return (Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
    }

    public bool IsOverPicture(Point p) => PictureRect.Contains(p);

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
        Focus();
        var (x, y) = Fraction(e.Location);
        if (e.Button == MouseButtons.Left) Pointer?.Invoke(PointerAction.LeftDown, x, y);
        else if (e.Button == MouseButtons.Right) Pointer?.Invoke(PointerAction.RightDown, x, y);
        else if (e.Button is MouseButtons.XButton1 or MouseButtons.XButton2)
        {
            Pointer?.Invoke(PointerAction.Move, x, y);
            SideButton?.Invoke(e.Button == MouseButtons.XButton2);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        var (x, y) = Fraction(e.Location);
        if (e.Button == MouseButtons.Left) Pointer?.Invoke(PointerAction.LeftUp, x, y);
        else if (e.Button == MouseButtons.Right) Pointer?.Invoke(PointerAction.RightUp, x, y);
        base.OnMouseUp(e);
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_ERASEBKGND:
                m.Result = (IntPtr)1;
                return;
            case WM_MOUSEWHEEL:
            case WM_MOUSEHWHEEL:
            {
                int delta = (short)((long)m.WParam >> 16);
                // lParam is in screen coordinates.
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

/// <summary>The Mac's cursor shape (kind 14) as a Windows cursor with its hot spot.</summary>
public static class MacCursor
{
    [StructLayout(LayoutKind.Sequential)]
    struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

    [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("user32.dll")] static extern IntPtr CreateIconIndirect(ref ICONINFO info);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

    /// <summary>The PNG drawn at <paramref name="pixelsPerPoint"/> screen pixels per Mac point.</summary>
    public static Cursor? Make(byte[] png, float hotX, float hotY, float widthPoints, float heightPoints, float pixelsPerPoint)
    {
        try
        {
            using var src = new Bitmap(new MemoryStream(png));
            float ppp = Math.Clamp(pixelsPerPoint, 0.5f, 4f);
            int w = Math.Clamp((int)MathF.Round(widthPoints * ppp), 8, 128), h = Math.Clamp((int)MathF.Round(heightPoints * ppp), 8, 128);
            using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, w, h);
            }
            var icon = bmp.GetHicon();
            GetIconInfo(icon, out var info);
            DestroyIcon(icon);
            info.fIcon = false;
            info.xHotspot = Math.Clamp((int)MathF.Round(hotX * w / Math.Max(widthPoints, 1)), 0, w - 1);
            info.yHotspot = Math.Clamp((int)MathF.Round(hotY * h / Math.Max(heightPoints, 1)), 0, h - 1);
            var cursor = CreateIconIndirect(ref info);
            DeleteObject(info.hbmMask);
            DeleteObject(info.hbmColor);
            return cursor == IntPtr.Zero ? null : new Cursor(cursor);
        }
        catch (ArgumentException) { return null; }
    }
}
