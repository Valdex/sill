using System.Runtime.InteropServices;

namespace Sill.Windows.UI;

/// <summary>What Coherence needs of this PC's displays: each one's DPI scale (per monitor, as Windows
/// renders a per-monitor-v2 window there), its refresh rate, and the largest screen in points (kind
/// 29's `screen`, the least room the Mac keeps for each window).</summary>
static class Displays
{
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

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

    /// <summary>The DPI scale (1.0 at 96 DPI) of the monitor nearest this point.</summary>
    public static double ScaleAt(Point p)
    {
        var monitor = MonitorFromPoint(new POINT { X = p.X, Y = p.Y }, 2);   // MONITOR_DEFAULTTONEAREST
        return monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var dpi, out _) >= 0 && dpi > 0 ? dpi / 96.0 : 1.0;   // MDT_EFFECTIVE_DPI
    }

    public static double ScaleOf(Screen s) => ScaleAt(new Point(s.Bounds.Left + s.Bounds.Width / 2, s.Bounds.Top + s.Bounds.Height / 2));

    /// <summary>The largest screen in points (its pixels over its scale), by area.</summary>
    public static (double w, double h) LargestInPoints()
    {
        (double w, double h) best = (1920, 1080);
        double area = 0;
        foreach (var s in Screen.AllScreens)
        {
            double k = ScaleOf(s), w = s.Bounds.Width / k, h = s.Bounds.Height / k;
            if (w * h > area) { area = w * h; best = (w, h); }
        }
        return best;
    }

    public static int RefreshRate(Screen s)
    {
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        return EnumDisplaySettings(s.DeviceName, -1, ref dm) && dm.dmDisplayFrequency > 1 ? dm.dmDisplayFrequency : 60;
    }
}
