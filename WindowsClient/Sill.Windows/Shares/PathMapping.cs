using System.Text;
using Sill.Windows.Data;

namespace Sill.Windows.Shares;

/// <summary>A folder of the Mac's this PC reaches over SMB (macOS's File Sharing): the share as Windows
/// names it (\\host\share, and the drive letter it is mapped to) and the folder it is on the Mac
/// (/Users/‹name› for an account's home folder, /Volumes/‹disk› for a disk). Saved in shares.json; the
/// password never is (Windows' Credential Manager keeps it).</summary>
public sealed class MacShare
{
    /// <summary>The Mac as Sill knows it, for the words and to pick its session.</summary>
    public string MacName { get; set; } = "";
    /// <summary>The saved Mac's ID (MacID.Make of its key), when the share was set up from its session.</summary>
    public string? MacID { get; set; }
    /// <summary>The Mac's host name (its Bonjour name, "Noahs-MacBook-Pro.local") or address.</summary>
    public string Host { get; set; } = "";
    /// <summary>The share's name: an account's home folder is shared under the account's short name.</summary>
    public string Share { get; set; } = "";
    /// <summary>"Z:", when mapped to a drive.</summary>
    public string? Drive { get; set; }
    /// <summary>The share's folder on the Mac, absolute ("/Users/noah").</summary>
    public string MacPath { get; set; } = "";

    public string Unc => $@"\\{Host}\{Share}";
}

/// <summary>A path on this PC inside a Mac's share, as the Mac's own path, and back (Open on Mac sends
/// kind 7's `documents` with it). Pure; WindowsClient/Sill.Checks checks it.
/// <list type="bullet">
/// <item>The share is found by its drive letter or its UNC name (\\host\share, the host with or without
/// ".local", the \\?\UNC\ and \\?\ forms, forward slashes), whole components only (\\mac\share never
/// matches \\mac\shared), case-insensitively: Windows paths are, and so is a Mac's disk as formatted by
/// default (APFS, case-insensitive), so a name typed in another case is the same file.</item>
/// <item>Each component after it maps back from Services for Mac's private-use characters (the Mac's SMB
/// server shows a ':' in a name as U+F022) and is decomposed (NFD), as HFS+ stores names and Finder
/// lists them; APFS finds a file under either form.</item>
/// <item>`.` and `..` are resolved, and a path that climbs out of the share maps to nothing.</item>
/// </list></summary>
public static class PathMapping
{
    /// <summary>The Mac's path for <paramref name="windowsPath"/>, and the share it is in; null when it
    /// is in none of <paramref name="shares"/>.</summary>
    public static string? ToMac(string windowsPath, IEnumerable<MacShare> shares, out MacShare? via)
    {
        via = null;
        if (Split(windowsPath) is not { } parts) return null;
        foreach (var share in shares)
        {
            if (Relative(parts, share) is not { } rest) continue;
            via = share;
            var root = share.MacPath.TrimEnd('/');
            var mac = rest.Select(c => WindowsNames.ToMac(c).Normalize(NormalizationForm.FormD));
            var joined = root + (rest.Count > 0 ? "/" + string.Join('/', mac) : "");
            return joined.Length == 0 ? "/" : joined;
        }
        return null;
    }

    /// <summary>The path on this PC for a Mac path inside one of the shares (by its drive letter when it
    /// has one); null when the path is in none.</summary>
    public static string? ToWindows(string macPath, IEnumerable<MacShare> shares)
    {
        var mac = Resolve(macPath.Normalize(NormalizationForm.FormC).Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (mac == null) return null;
        foreach (var share in shares)
        {
            var root = Resolve(share.MacPath.Normalize(NormalizationForm.FormC).Split('/', StringSplitOptions.RemoveEmptyEntries));
            if (root == null || mac.Count < root.Count) continue;
            if (!root.Select((c, i) => string.Equals(c, mac[i], StringComparison.OrdinalIgnoreCase)).All(x => x)) continue;
            var rest = mac.Skip(root.Count).Select(WindowsNames.FromMac);
            var top = share.Drive is { Length: > 0 } d ? d.TrimEnd('\\') : share.Unc;
            return string.Join('\\', new[] { top }.Concat(rest)) + (mac.Count == root.Count ? "\\" : "");
        }
        return null;
    }

    /// <summary>The Mac folder a share of this name most likely is: an account's home folder.</summary>
    public static string DefaultMacPath(string share) => "/Users/" + share.Trim().Trim('/');

    /// <summary>A Windows path as its root (a drive "Z:" or a UNC host and share) and components, `.` and
    /// `..` resolved; null when it is neither, or climbs above its root.</summary>
    internal static (string? drive, string? host, string? share, List<string> rest)? Split(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Trim().Replace('/', '\\');
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) p = @"\\" + p[8..];
        else if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal)) p = p[4..];
        if (p.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var unc = p[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (unc.Length < 2) return null;
            return Resolve(unc.Skip(2)) is { } r ? (null, unc[0], unc[1], r) : null;
        }
        if (p.Length >= 2 && char.IsAsciiLetter(p[0]) && p[1] == ':')
        {
            var rest = p[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return Resolve(rest) is { } r ? (char.ToUpperInvariant(p[0]) + ":", null, null, r) : null;
        }
        return null;
    }

    static List<string>? Resolve(IEnumerable<string> components)
    {
        var list = new List<string>();
        foreach (var c in components)
        {
            if (c == ".") continue;
            if (c == "..") { if (list.Count == 0) return null; list.RemoveAt(list.Count - 1); continue; }
            list.Add(c);
        }
        return list;
    }

    static List<string>? Relative((string? drive, string? host, string? share, List<string> rest) p, MacShare s)
    {
        if (p.drive != null)
            return s.Drive is { Length: >= 2 } d && string.Equals(d[..2], p.drive, StringComparison.OrdinalIgnoreCase) ? p.rest : null;
        if (p.host == null || p.share == null) return null;
        if (!SameHost(p.host, s.Host) || !string.Equals(p.share, s.Share, StringComparison.OrdinalIgnoreCase)) return null;
        return p.rest;
    }

    /// <summary>One host under its Bonjour name or its plain one ("mac.local" and "mac", as Explorer shows
    /// a Mac browsed through the network), case-insensitively.</summary>
    public static bool SameHost(string a, string b)
    {
        static string Bare(string h)
        {
            h = h.Trim().TrimEnd('.');
            return h.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ? h[..^6] : h;
        }
        return string.Equals(Bare(a), Bare(b), StringComparison.OrdinalIgnoreCase);
    }
}
