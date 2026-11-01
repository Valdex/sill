using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Sill.Windows.Coherence;

/// <summary>The names Windows' shell knows a Mac app's windows by (the taskbar, the Start menu, the
/// jump list): one AppUserModelID per Mac app per Mac, so a Mac app's windows share a taskbar button
/// of their own and the Mac's apps never mix with each other or with Sill's own windows. Pure.</summary>
public static class Aumid
{
    /// <summary>Windows' limit on an AppUserModelID; it takes no spaces either.</summary>
    public const int MaxLength = 128;

    /// <summary>`Sill.<macKey>.<bundleID>`, each part cleaned to letters, digits, '.', '-' and '_'
    /// (anything else '-'), cut so the whole fits 128 characters.</summary>
    public static string For(string macKey, string bundleID)
    {
        var mac = Clean(macKey, 32);
        var prefix = $"Sill.{(mac.Length > 0 ? mac : "Mac")}.";
        var bundle = Clean(bundleID, MaxLength - prefix.Length);
        return prefix + (bundle.Length > 0 ? bundle : "app");
    }

    /// <summary>A Mac's key in those names: its Mac ID (16 characters of its key's fingerprint), or for a
    /// plain door with no key, "A" and 12 hex digits of its address's hash.</summary>
    public static string MacKey(string? macID, string? address)
    {
        if (!string.IsNullOrEmpty(macID) && LaunchRequest.ValidMacKey(macID)) return macID;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(address ?? ""));
        return "A" + Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    static string Clean(string s, int max)
    {
        var sb = new StringBuilder(Math.Min(s.Length, max));
        foreach (var c in s)
        {
            if (sb.Length >= max) break;
            sb.Append(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_' ? c : '-');
        }
        return sb.ToString().Trim('.');
    }

    /// <summary>What the taskbar runs to bring a pinned Mac app back (System.AppUserModel.RelaunchCommand,
    /// and a Start menu shortcut's arguments): this Sill.exe with `--launch`.</summary>
    public static string RelaunchCommand(string exePath, string macKey, string bundleID) =>
        $"\"{exePath}\" {LaunchArguments(macKey, bundleID)}";

    public static string LaunchArguments(string macKey, string bundleID) => $"--launch {macKey} {bundleID}";

    /// <summary>The name a pinned button and a shortcut show: "Safari on Noah’s MacBook Pro".</summary>
    public static string DisplayName(string appName, string macName)
    {
        var app = Protocol.SafeText.Clean(appName, 64) ?? "Mac app";
        var mac = Protocol.SafeText.Clean(macName, 64);
        return mac == null ? app : $"{app} on {mac}";
    }

    /// <summary>A file name made of a display name: no characters Windows refuses, no trailing dots.</summary>
    public static string FileName(string name)
    {
        var bad = new HashSet<char>("<>:\"/\\|?*");
        var s = new string(name.Select(c => c < 32 || bad.Contains(c) ? '-' : c).ToArray()).Trim().TrimEnd('.');
        return s.Length == 0 ? "Mac app" : s.Length > 100 ? s[..100] : s;
    }
}

/// <summary>`Sill.exe --launch <macKey> <bundleID>` (a pinned Mac app, a Start menu shortcut, the jump
/// list): open that app on that Mac in Coherence. A second Sill.exe hands it to the one running over
/// a named pipe, one line: "launch\t<macKey>\t<bundleID>", or "show" for a Sill opened again with no
/// arguments. Pure.</summary>
public sealed record LaunchRequest(string MacKey, string BundleID)
{
    public const string ShowLine = "show";
    public const int MaxLine = 512;

    /// <summary>A Mac ID (16 of Crockford's base32) or a plain door's "A" and 12 hex digits.</summary>
    public static bool ValidMacKey(string? s) =>
        s is { Length: 16 } && s.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'Z' && c is not ('I' or 'L' or 'O' or 'U'))
        || s is { Length: 13 } && s[0] == 'A' && s.Skip(1).All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>A bundle ID as macOS has them: letters, digits, '.', '-' and '_', at most 255.</summary>
    public static bool ValidBundleID(string? s) =>
        s is { Length: >= 1 and <= 255 } && s[0] != '.' && s.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_');

    /// <summary>The request in a command line, or null (none, or not well formed).</summary>
    public static LaunchRequest? FromArgs(IReadOnlyList<string> args)
    {
        for (int i = 0; i + 2 < args.Count; i++)
            if (args[i] == "--launch")
                return ValidMacKey(args[i + 1]) && ValidBundleID(args[i + 2]) ? new LaunchRequest(args[i + 1], args[i + 2]) : null;
        return null;
    }

    public string Encode() => $"launch\t{MacKey}\t{BundleID}";

    /// <summary>A line from the pipe: (true, null) for "show", (false, request) for a launch, null for
    /// anything else (dropped).</summary>
    public static (bool show, LaunchRequest? launch)? Decode(string? line)
    {
        if (line == null || line.Length > MaxLine) return null;
        if (line == ShowLine) return (true, null);
        var p = line.Split('\t');
        if (p.Length == 3 && p[0] == "launch" && ValidMacKey(p[1]) && ValidBundleID(p[2])) return (false, new LaunchRequest(p[1], p[2]));
        return null;
    }

    /// <summary>Whether a Sill started with these arguments hands them to one already running and
    /// quits: with none (Sill opened again: the running one comes forward), or only `--launch`'s. A test
    /// run (`--connect`, `--log`, …) and `--diag` run on their own.</summary>
    public static bool HandsOff(IReadOnlyList<string> args) =>
        args.Count == 0 || args.Count == 3 && args[0] == "--launch";
}

/// <summary>An .ico file made of PNG images (Windows Vista on reads PNG-compressed icon images): the
/// Mac app's icon (kind 4, 256 px) at each size the shell asks for. ICONDIR (reserved 0, type 1,
/// count), then an ICONDIRENTRY per image (width and height, 0 for 256; no palette; one plane; 32
/// bits; the PNG's length and offset), then the PNGs. Little endian. Pure.</summary>
public static class IcoFile
{
    /// <summary>The sizes Sill writes: the taskbar's and Alt-Tab's at 100 % to 250 %, and the large one.</summary>
    public static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };

    public static byte[] Write(IReadOnlyList<(int size, byte[] png)> images)
    {
        int count = images.Count;
        int offset = 6 + 16 * count;
        var ms = new MemoryStream();
        var head = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(4), (ushort)count);
        ms.Write(head);
        foreach (var (size, png) in images)
        {
            if (size is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(images), "An icon is 1 to 256 pixels.");
            var e = new byte[16];
            e[0] = (byte)(size == 256 ? 0 : size);
            e[1] = (byte)(size == 256 ? 0 : size);
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(6), 32);
            BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(8), (uint)png.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(12), (uint)offset);
            ms.Write(e);
            offset += png.Length;
        }
        foreach (var (_, png) in images) ms.Write(png);
        return ms.ToArray();
    }

    /// <summary>The images an .ico holds (size, offset, length), or null when it is not one.</summary>
    public static List<(int size, int offset, int length)>? Entries(ReadOnlySpan<byte> ico)
    {
        if (ico.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(ico) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(ico[2..]) != 1) return null;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico[4..]);
        if (ico.Length < 6 + 16 * count) return null;
        var list = new List<(int, int, int)>();
        for (int i = 0; i < count; i++)
        {
            var e = ico.Slice(6 + 16 * i, 16);
            int size = e[0] == 0 ? 256 : e[0];
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(e[8..]);
            int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(e[12..]);
            if (offset < 0 || length < 0 || (long)offset + length > ico.Length) return null;
            list.Add((size, offset, length));
        }
        return list;
    }

    /// <summary>The cached file's name for an app's icon: its bundle ID and the PNG's hash, so a new
    /// icon (an app updated) is a new file and Windows' icon cache never shows the old one.</summary>
    public static string CacheName(string bundleID, ReadOnlySpan<byte> png)
    {
        var safe = new string(bundleID.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-').ToArray()).Trim('.');
        if (safe.Length == 0) safe = "app";
        if (safe.Length > 80) safe = safe[..80];
        return $"{safe}-{Convert.ToHexString(SHA256.HashData(png), 0, 4).ToLowerInvariant()}.ico";
    }
}

/// <summary>A Mac app launched from Sill: for the jump list's "Recent Mac apps" and the Start menu.</summary>
public sealed class RecentApp
{
    public string MacKey { get; set; } = "";
    public string MacName { get; set; } = "";
    public string BundleID { get; set; } = "";
    public string AppName { get; set; } = "";
    public DateTimeOffset LastUsed { get; set; }

    public static bool Same(RecentApp a, RecentApp b) => a.MacKey == b.MacKey && a.BundleID == b.BundleID;

    /// <summary>The list with <paramref name="app"/> first (a launch again moves it, its names
    /// refreshed), newest first, at most <paramref name="max"/>.</summary>
    public static List<RecentApp> Touch(IEnumerable<RecentApp> list, RecentApp app, int max = 10)
    {
        var rest = list.Where(x => !Same(x, app)).OrderByDescending(x => x.LastUsed);
        return new[] { app }.Concat(rest).Take(Math.Max(1, max)).ToList();
    }
}

/// <summary>What Coherence keeps between runs (coherence.json beside settings.json): whether to start
/// in it, the apps last shown per Mac, the Mac apps launched from Sill.</summary>
public sealed class CoherenceSettings
{
    /// <summary>View › Coherence: a Mac that can is shown in Coherence when Sill connects to it.</summary>
    public bool Mode { get; set; }
    /// <summary>Per Mac key, the apps whose windows were shown last.</summary>
    public Dictionary<string, List<string>> LastApps { get; set; } = new();
    public List<RecentApp> Recent { get; set; } = new();
    /// <summary>Start menu shortcuts for the Mac apps launched from Sill.</summary>
    public bool StartMenuShortcuts { get; set; } = true;
}
