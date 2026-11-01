using Sill.Windows.Net;

namespace Sill.Windows.Data;

/// <summary>Where files from the Mac wait: %LOCALAPPDATA%\Sill\Incoming\‹time›-‹transfer›, one folder
/// a transfer (removed after a day, at the next start), and Downloads\From ‹Mac› for what is handed to
/// an app by its path (CF_HDROP), which stays, as #66's host keeps a device's drops in the Mac's
/// ~/Downloads/From ‹device›.</summary>
public static class Staging
{
    public static string Incoming => Path.Combine(AppData.Folder, "Incoming");

    public static string NewFolder(uint transfer)
    {
        var d = Path.Combine(Incoming, $"{DateTime.Now:yyyyMMdd-HHmmss}-{transfer}");
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>A temporary file in the staging folder (a folder zipped to go, a virtual file copied).</summary>
    public static string Temp(string name)
    {
        var d = Path.Combine(Incoming, "out-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(d);
        return Path.Combine(d, WindowsNames.FromMac(name));
    }

    /// <summary>Removes a temporary file and its folder (Temp's).</summary>
    public static void RemoveTemp(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            var dir = Path.GetDirectoryName(path);
            if (dir != null && Path.GetFileName(dir).StartsWith("out-", StringComparison.Ordinal) && Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Downloads\From ‹Mac›.</summary>
    public static string FromMac(string macName)
    {
        var downloads = KnownDownloads() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Path.Combine(downloads, WindowsNames.FromMac($"From {macName}"));
    }

    static string? KnownDownloads()
    {
        // FOLDERID_Downloads, which a user may have moved to another drive.
        try
        {
            int hr = SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var p);
            if (hr < 0) return null;
            try { return System.Runtime.InteropServices.Marshal.PtrToStringUni(p); }
            finally { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(p); }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    /// <summary>The path, or "name 2", "name 3"… beside it, whichever is not taken (#66's unique).</summary>
    public static string Unique(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        bool folder = Directory.Exists(path);
        var stem = folder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        var ext = folder ? "" : Path.GetExtension(path);
        for (int n = 2; ; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} {n}{ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    static int cleaned;

    /// <summary>Once a run: transfers older than a day go (an app given a staged path had a day).</summary>
    public static void CleanOnce()
    {
        if (Interlocked.Exchange(ref cleaned, 1) != 0) return;
        _ = Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(Incoming)) return;
                foreach (var d in new DirectoryInfo(Incoming).EnumerateDirectories())
                {
                    if (DateTime.Now - d.LastWriteTime < TimeSpan.FromDays(1)) continue;
                    try { d.Delete(true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        });
    }
}
