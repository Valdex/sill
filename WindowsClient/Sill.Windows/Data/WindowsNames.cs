using System.Text;

namespace Sill.Windows.Data;

/// <summary>A Mac's file name made a Windows one, and back. A Mac name may hold what Windows refuses
/// (`&lt;&gt;:"/\|?*`, control characters, a trailing dot or space) or be a name Windows reserves (CON,
/// PRN, AUX, NUL, COM1–9, LPT1–9, with any extension). The characters map into the private-use range
/// Services for Macintosh defined and that macOS's own SMB server and client, and Samba's `fruit`,
/// still use (U+F001–U+F029), so a name looks the same here as through the Mac's share and maps back
/// exactly; a reserved name gets an underscore after its stem. Mac names come decomposed (NFD, as HFS+
/// stored them); Windows' apps expect composed (NFC), so names are composed here. Pure; checked.</summary>
public static class WindowsNames
{
    /// <summary>The most UTF-16 units a name may have on NTFS.</summary>
    public const int MaxLength = 255;

    // Services for Macintosh's table: 0x01–0x1F → U+F001–U+F01F, then these.
    static readonly (char ascii, char mapped)[] Table =
    {
        ('"', ''), ('*', ''), (':', ''), ('<', ''), ('>', ''),
        ('?', ''), ('\\', ''), ('|', ''),
    };
    const char TrailingSpace = '', TrailingDot = '';

    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>A name from the Mac (kind 34's, an offer's, a zip entry's component) as a name Windows
    /// takes: composed, its forbidden characters mapped, its trailing dots and spaces mapped, a reserved
    /// stem given an underscore, at most 255 units with its extension kept. A '/' (never in a Mac file
    /// name: Finder shows ':' as '/') maps as ':' does. Never empty.</summary>
    public static string FromMac(string name)
    {
        var s = (name ?? "").Normalize(NormalizationForm.FormC);
        var b = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '\0') continue;
            if (c < 0x20) { b.Append((char)(0xF000 + c)); continue; }
            if (c == '/') { b.Append(''); continue; }
            int t = Array.FindIndex(Table, e => e.ascii == c);
            b.Append(t >= 0 ? Table[t].mapped : c);
        }
        // Windows drops trailing dots and spaces from a name: each one is mapped instead.
        for (int i = b.Length - 1; i >= 0 && (b[i] == '.' || b[i] == ' '); i--)
            b[i] = b[i] == '.' ? TrailingDot : TrailingSpace;
        var r = b.ToString();
        if (r.Length == 0) return "_";
        int dot = r.IndexOf('.');
        var stem = dot < 0 ? r : r[..dot];
        if (Reserved.Contains(stem.TrimEnd(' '))) r = stem + "_" + (dot < 0 ? "" : r[dot..]);
        return Shorten(r);
    }

    /// <summary>A Windows name as the Mac's: the private-use characters back to what they stand for
    /// (a name seen through the Mac's share, or one this PC made with <see cref="FromMac"/>).</summary>
    public static string ToMac(string name)
    {
        var b = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c >= '' && c <= '') { b.Append((char)(c - 0xF000)); continue; }
            if (c == TrailingSpace) { b.Append(' '); continue; }
            if (c == TrailingDot) { b.Append('.'); continue; }
            int t = Array.FindIndex(Table, e => e.mapped == c);
            b.Append(t >= 0 ? Table[t].ascii : c);
        }
        return b.ToString();
    }

    /// <summary>Whether a name is a Windows name as it stands (what FromMac leaves alone).</summary>
    public static bool IsValid(string name) => name.Length > 0 && FromMac(name) == name;

    static string Shorten(string r)
    {
        if (r.Length <= MaxLength) return r;
        int dot = r.LastIndexOf('.');
        var ext = dot > 0 && r.Length - dot <= 16 ? r[dot..] : "";
        int keep = MaxLength - ext.Length;
        if (char.IsHighSurrogate(r[keep - 1])) keep--;   // never half a character
        return r[..keep] + ext;
    }
}
