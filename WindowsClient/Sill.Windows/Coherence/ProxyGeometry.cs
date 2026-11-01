using System.Drawing;
using Sill.Windows.Protocol;

namespace Sill.Windows.Coherence;

/// <summary>The arithmetic between a Mac window (points, kind 45), its picture (pixels, its own
/// stream) and its window on this PC (device pixels): the title bar cropped off the top of the
/// picture (`titleBar`, drawn by the Renderer's uv rect), the pointer as a fraction of the whole
/// picture, the sizes either way, the limits of a resize (`min`, `max`), and where a child that spills
/// out of its owner goes. "Surface" is the part of the window that shows the picture (the client area
/// under the menu strip). Pure: WindowsClient/Sill.Checks checks it.</summary>
public static class ProxyGeometry
{
    /// <summary>How far a picture's two pixel-per-point ratios may differ for them to be one scale
    /// (HEVC rounds a side to even, and a frame of the old size can meet a new size).</summary>
    public const double ScaleTolerance = 0.03;

    /// <summary>The picture's pixels per point, from its size and the window's in points, when the two
    /// sides agree (a picture of the old size against a new size does not, and keeps
    /// <paramref name="previous"/>).</summary>
    public static double? CaptureScale(int picW, int picH, double ptW, double ptH, double? previous)
    {
        if (picW <= 0 || picH <= 0 || ptW <= 0 || ptH <= 0) return previous;
        double sx = picW / ptW, sy = picH / ptH;
        if (Math.Abs(sx - sy) > ScaleTolerance * Math.Max(sx, sy)) return previous;
        return Math.Clamp((sx + sy) / 2, 0.25, 8);
    }

    /// <summary>The rows of the picture the title bar takes, never all of it.</summary>
    public static int CropPixels(double titleBarPt, double captureScale, int picH)
    {
        if (picH <= 1 || titleBarPt <= 0 || captureScale <= 0) return 0;
        return Math.Clamp((int)Math.Round(titleBarPt * captureScale), 0, picH - 1);
    }

    /// <summary>The crop as a fraction of the picture's height.</summary>
    public static double CropFraction(int cropPx, int picH) => picH > 0 ? Math.Clamp(cropPx / (double)picH, 0, 0.99) : 0;

    /// <summary>A title bar no shorter than the window it is on is no title bar to crop.</summary>
    static double Bar(double titleBarPt, double ptH) => titleBarPt > 0 && titleBarPt < ptH ? titleBarPt : 0;

    /// <summary>The surface, in device pixels, for a Mac window of this size: its width, and its
    /// height less the title bar, at this PC's pixels per point.</summary>
    public static Size SurfaceSize(double ptW, double ptH, double titleBarPt, double dpiScale)
    {
        double tb = Bar(titleBarPt, ptH);
        return new Size(Math.Max(1, (int)Math.Round(ptW * dpiScale)), Math.Max(1, (int)Math.Round((ptH - tb) * dpiScale)));
    }

    /// <summary>The Mac window's size in whole points for a surface of this size: what kind 46 asks.</summary>
    public static (double w, double h) MacSize(int surfW, int surfH, double titleBarPt, double dpiScale)
    {
        double s = dpiScale > 0 ? dpiScale : 1;
        double w = Math.Round(Math.Max(1, surfW) / s);
        double h = Math.Round(Math.Max(1, surfH) / s + Math.Max(0, titleBarPt));
        return (Math.Max(1, w), Math.Max(1, h));
    }

    /// <summary>A point of the surface (device pixels from its top-left) as fractions of the whole
    /// picture, title bar included, as kind 8 with `window` carries it: the surface shows the
    /// picture's rows below the crop, stretched to fill it. The pixel's centre, held to 0…1.</summary>
    public static (double x, double y) PointerFraction(double x, double y, int surfW, int surfH, double cropFraction)
    {
        double fx = surfW > 0 ? (x + 0.5) / surfW : 0.5;
        double fy = surfH > 0 ? (y + 0.5) / surfH : 0.5;
        double cf = Math.Clamp(cropFraction, 0, 0.99);
        return (Math.Clamp(fx, 0, 1), Math.Clamp(cf + Math.Clamp(fy, 0, 1) * (1 - cf), 0, 1));
    }

    /// <summary>The least and most a window may be dragged to (WM_GETMINMAXINFO's track sizes), in
    /// device pixels: the Mac's `min` and `max` as surfaces, plus <paramref name="chrome"/> (the frame,
    /// the caption and the menu strip). A window the Mac says does not resize keeps its size. Null
    /// where the Mac said nothing.</summary>
    public static (Size? min, Size? max) TrackLimits(MacWindow w, double dpiScale, Size chrome)
    {
        Size? Of(double[]? pt) => pt is { Length: >= 2 } p && p[0] > 0 && p[1] > 0
            ? SurfaceSize(p[0], p[1], w.TitleBar, dpiScale) + chrome : null;
        if (w.Resizable == false && w.SizePoints is { } now)
        {
            var fixedSize = SurfaceSize(now.w, now.h, w.TitleBar, dpiScale) + chrome;
            return (fixedSize, fixedSize);
        }
        var min = Of(w.Min);
        var max = Of(w.Max);
        if (min is { } a && max is { } b && (b.Width < a.Width || b.Height < a.Height))
            max = new Size(Math.Max(a.Width, b.Width), Math.Max(a.Height, b.Height));
        return (min, max);
    }

    /// <summary>A size held to the limits (each side on its own), as Windows holds a drag.</summary>
    public static Size Clamp(Size size, Size? min, Size? max)
    {
        int w = size.Width, h = size.Height;
        if (max is { } b) { w = Math.Min(w, b.Width); h = Math.Min(h, b.Height); }
        if (min is { } a) { w = Math.Max(w, a.Width); h = Math.Max(h, a.Height); }
        return new Size(w, h);
    }

    /// <summary>Where a child goes on screen: its `rect` (points from its owner's top-left) from the
    /// owner's top-left on screen (a proxy's: its surface's top-left less the cropped title bar; a
    /// popup's: its own).</summary>
    public static Rectangle ChildBounds(Point ownerOrigin, double[] rect, double dpiScale)
    {
        if (rect is not { Length: >= 4 }) return new Rectangle(ownerOrigin, new Size(1, 1));
        int x = ownerOrigin.X + (int)Math.Round(rect[0] * dpiScale);
        int y = ownerOrigin.Y + (int)Math.Round(rect[1] * dpiScale);
        int w = Math.Max(1, (int)Math.Round(rect[2] * dpiScale));
        int h = Math.Max(1, (int)Math.Round(rect[3] * dpiScale));
        return new Rectangle(x, y, w, h);
    }

    /// <summary>A proxy's Mac window's top-left on screen: its surface's top-left, less the title bar
    /// the surface leaves out.</summary>
    public static Point WindowOrigin(Point surfaceOrigin, double titleBarPt, double dpiScale) =>
        new(surfaceOrigin.X, surfaceOrigin.Y - (int)Math.Round(Math.Max(0, titleBarPt) * dpiScale));

    /// <summary>A rectangle moved (not shrunk, unless it is larger) to lie within an area: a Mac menu
    /// opened near the edge of the Mac's screen may hang past this PC's.</summary>
    public static Rectangle KeepWithin(Rectangle r, Rectangle area)
    {
        int w = Math.Min(r.Width, area.Width), h = Math.Min(r.Height, area.Height);
        int x = Math.Clamp(r.X, area.Left, area.Right - w);
        int y = Math.Clamp(r.Y, area.Top, area.Bottom - h);
        return new Rectangle(x, y, w, h);
    }

    /// <summary>Where the n-th new window opens: from <paramref name="start"/>, each a step down and
    /// right of the last, starting over at the area's top-left when the next would leave it.</summary>
    public static Point Cascade(int index, Point start, Size size, Rectangle area, int step)
    {
        int x = start.X, y = start.Y;
        for (int i = 0; i < index; i++)
        {
            x += step; y += step;
            if (x + size.Width > area.Right || y + size.Height > area.Bottom) { x = area.Left + step * ((i + 1) % 4); y = area.Top + step * ((i + 1) % 4); }
        }
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - size.Width));
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        return new Point(x, y);
    }
}

/// <summary>Kind 46 during a resize of one window (docs/coherence-protocol.md): moves are this PC's
/// alone; a size goes at most every <see cref="PlaceWindow.MinInterval"/> seconds while the frame is
/// dragged (the newest last), at once at the end of the drag (WM_EXITSIZEMOVE) or a change outside
/// one (a maximize, a snap), never the size last sent again (within a point: a DPI change's rounding),
/// in whole points. The Mac answers each with a kind 45 whose `answering` is its token and whose size
/// is the one the window took. While a resize goes on, or a size waits for its answer, the Mac's sizes
/// are not applied to the window here: the answer to the last one is, if it differs (the Mac refused
/// the size); one that never comes is given up on after <see cref="AnswerWait"/>. Pure.</summary>
public sealed class PlaceThrottle
{
    /// <summary>How long a size waits for its answer before the Mac's sizes apply again.</summary>
    public const double AnswerWait = 2;

    (double w, double h)? sent, pending;
    double sentAt = double.NegativeInfinity;

    /// <summary>The token of the last kind 46 sent, while its answer has not come.</summary>
    public int? Outstanding { get; private set; }
    public (double w, double h)? Pending => pending;
    public (double w, double h)? LastSent => sent;

    static (double w, double h) Whole(double w, double h) => (Math.Max(1, Math.Round(w)), Math.Max(1, Math.Round(h)));
    static bool Near((double w, double h) a, (double w, double h)? b) => b is { } x && Math.Abs(a.w - x.w) <= 1 && Math.Abs(a.h - x.h) <= 1;

    /// <summary>The window here was sized to (w, h) points: the size to send now, or null (held for
    /// <see cref="Tick"/>, or nothing to send). <paramref name="final"/>: the drag ended, or there was none.</summary>
    public (double w, double h)? Change(double w, double h, double now, bool final)
    {
        var size = Whole(w, h);
        if (Near(size, sent)) { pending = null; return null; }
        if (final || now >= sentAt + PlaceWindow.MinInterval)
        {
            pending = null;
            sent = size;
            sentAt = now;
            return size;
        }
        pending = size;
        return null;
    }

    /// <summary>A size held back by the interval, once the interval is over.</summary>
    public (double w, double h)? Tick(double now)
    {
        if (pending is not { } p || now < sentAt + PlaceWindow.MinInterval) return null;
        pending = null;
        sent = p;
        sentAt = now;
        return p;
    }

    /// <summary>The size just returned went with this token.</summary>
    public void Sent(int token) => Outstanding = token;

    /// <summary>Whether the Mac's size (a kind 45 answering <paramref name="answering"/>, or none)
    /// applies to the window now: not during a drag nor with a size held back; with a size out, only
    /// its own answer (or once it is overdue).</summary>
    public bool TakesMacSize(int? answering, bool resizing, double now)
    {
        if (resizing || pending != null) return false;
        if (Outstanding is not { } token) return true;
        if (answering == token || now >= sentAt + AnswerWait) { Outstanding = null; return true; }
        return false;
    }

    /// <summary>A new session: no answer will come to a size the last one sent.</summary>
    public void NewSession() => Outstanding = null;

    /// <summary>The window was given the Mac's size: that is the size known to the Mac now.</summary>
    public void Settled(double w, double h)
    {
        sent = Whole(w, h);
        pending = null;
    }
}
