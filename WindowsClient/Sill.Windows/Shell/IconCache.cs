using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Sill.Windows.Coherence;
using Sill.Windows.Net;

namespace Sill.Windows.Shell;

/// <summary>The Mac apps' icons as Windows wants them: kind 4's PNG (256 px, Hello.iconSize) drawn at
/// each size of <see cref="IcoFile.Sizes"/> into a multi-size .ico under %LOCALAPPDATA%\Sill\Icons, its
/// name the bundle ID and the PNG's hash, so a new icon is a new file and nothing stale is shown. The
/// windows take it as their icon (Form.Icon: WM_SETICON big and small), the taskbar's relaunch
/// property and the Start menu's shortcuts name the file. UI thread.</summary>
public static class IconCache
{
    static readonly Dictionary<string, Icon> loaded = new();

    public static string Folder
    {
        get
        {
            var d = Path.Combine(AppData.Folder, "Icons");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>The .ico for this app's PNG, made the first time; null when the PNG does not decode
    /// or the file cannot be written.</summary>
    public static string? IcoFor(string bundleID, byte[] png)
    {
        try
        {
            var path = Path.Combine(Folder, IcoFile.CacheName(bundleID, png));
            if (File.Exists(path)) return path;
            using var src = new Bitmap(new MemoryStream(png));
            var images = IcoFile.Sizes.Select(size => (size, Draw(src, size))).ToList();
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, IcoFile.Write(images));
            File.Move(tmp, path, true);
            return path;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or ExternalException)
        {
            TestArguments.Log($"icon for {bundleID}: {e.Message}");
            return null;
        }
    }

    /// <summary>The icon of an .ico made here, loaded once.</summary>
    public static Icon? Load(string path)
    {
        if (loaded.TryGetValue(path, out var icon)) return icon;
        try { icon = new Icon(path); }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
        loaded[path] = icon;
        return icon;
    }

    static byte[] Draw(Bitmap src, int size)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(src, new Rectangle(0, 0, size, size));
        }
        var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}

/// <summary>coherence.json beside settings.json (<see cref="CoherenceSettings"/>).</summary>
public static class CoherenceStore
{
    const string File = "coherence.json";
    static CoherenceSettings? current;
    public static CoherenceSettings Current => current ??= AppData.Read<CoherenceSettings>(File);
    public static void Save() { if (current != null) AppData.Write(File, current); }
}
