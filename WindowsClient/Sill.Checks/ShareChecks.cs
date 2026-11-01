using System.Text;
using Sill.Windows.Shares;

namespace Sill.Checks;

// A path on this PC inside a Mac's SMB share as the Mac's path (Open on Mac's kind 7 `documents`), and
// back: drive letters and UNC names, whole components, case, ".local", Services for Mac's characters,
// NFD, "." and "..", the long-path prefixes.
static class ShareChecks
{
    public static void Paths()
    {
        var home = new MacShare { MacName = "Studio", Host = "Noahs-Studio.local", Share = "noah", Drive = "Z:", MacPath = "/Users/noah" };
        var disk = new MacShare { MacName = "Studio", Host = "Noahs-Studio.local", Share = "Photos", MacPath = "/Volumes/Photos/" };
        var shares = new[] { home, disk };
        string? Mac(string p) => PathMapping.ToMac(p, shares, out _);

        Check.Equal("/Users/noah/Documents/a.txt", Mac(@"Z:\Documents\a.txt"), "the mapped drive: Z:\\Documents\\a.txt");
        Check.Equal("/Users/noah/Documents/a.txt", Mac(@"z:/Documents/a.txt"), "a lower-case drive and forward slashes");
        Check.Equal("/Users/noah", Mac(@"Z:\"), "the drive's root is the share's folder");
        Check.Equal("/Users/noah", Mac("Z:"), "a bare drive too");
        Check.Equal("/Users/noah/Desktop/b.key", Mac(@"\\Noahs-Studio.local\noah\Desktop\b.key"), "the UNC name");
        Check.Equal("/Users/noah/Desktop/b.key", Mac(@"\\NOAHS-STUDIO\NOAH\Desktop\b.key"), "the host without .local, another case (as Explorer's network view shows it)");
        Check.Equal("/Users/noah/Desktop/b.key", Mac(@"\\?\UNC\Noahs-Studio.local\noah\Desktop\b.key"), "\\\\?\\UNC\\ (a long path)");
        Check.Equal("/Users/noah/x", Mac(@"\\?\Z:\x"), "\\\\?\\Z:\\");
        Check.Equal("/Volumes/Photos/2026/IMG 1.heic", Mac(@"\\noahs-studio.local\photos\2026\IMG 1.heic"), "a disk's share: /Volumes, its trailing slash not doubled");
        Check.Equal(null, Mac(@"\\Noahs-Studio.local\noahs\x.txt"), "\\\\mac\\noahs is not \\\\mac\\noah (whole components only)");
        Check.Equal(null, Mac(@"\\Other-Mac.local\noah\x.txt"), "another host: not mapped");
        Check.Equal(null, Mac(@"C:\Users\noah\x.txt"), "a file on this PC: not mapped (copy it to the share first)");
        Check.Equal(null, Mac(@"Y:\x.txt"), "another drive: not mapped");
        Check.Equal(null, Mac(@"relative\x.txt"), "a relative path: not mapped");
        Check.Equal(null, Mac(""), "nothing: not mapped");
        Check.Equal("/Users/noah/b.txt", Mac(@"Z:\a\..\b.txt"), "`..` resolved");
        Check.Equal("/Users/noah/a/b.txt", Mac(@"Z:\a\.\b.txt"), "`.` dropped");
        Check.Equal(null, Mac(@"Z:\..\secret.txt"), "a path climbing out of the share maps to nothing");
        Check.Equal(null, Mac(@"\\Noahs-Studio.local\noah\..\..\etc\passwd"), "nor through the UNC name");
        Check.Equal("/Users/noah/Notes: 2026.txt", Mac(@"Z:\Notes" + "\uF022" + " 2026.txt"), "a ':' the Mac's SMB server showed as U+F022 maps back");
        Check.Equal("/Users/noah/what?.txt", Mac(@"Z:\what" + "\uF025" + ".txt"), "and a '?'");
        var nfd = Mac(@"Z:\Café\Résumé.pdf");
        Check.That(nfd == "/Users/noah/Cafe\u0301/Re\u0301sume\u0301.pdf" && nfd!.IsNormalized(NormalizationForm.FormD), "names decomposed (NFD), as the Mac stores and lists them");
        PathMapping.ToMac(@"\\noahs-studio\photos\x", shares, out var via);
        Check.That(via == disk, "the share it went through is given");

        // Back: a Mac path to this PC's.
        Check.Equal(@"Z:\Documents\a.txt", PathMapping.ToWindows("/Users/noah/Documents/a.txt", shares), "back: by the drive letter");
        Check.Equal(@"\\Noahs-Studio.local\Photos\2026\x.jpg", PathMapping.ToWindows("/Volumes/Photos/2026/x.jpg", shares), "back: a share without a drive, by its UNC name");
        Check.Equal(@"Z:\", PathMapping.ToWindows("/Users/noah", shares), "back: the share's folder is its root");
        Check.Equal(@"Z:\R" + "\u00e9" + "sum" + "\u00e9" + ".pdf", PathMapping.ToWindows("/Users/noah/Re\u0301sume\u0301.pdf", shares), "back: composed for Windows");
        Check.Equal(@"Z:\a" + "\uF022" + "b", PathMapping.ToWindows("/Users/noah/a:b", shares), "back: a ':' to U+F022");
        Check.Equal(@"Z:\x", PathMapping.ToWindows("/users/NOAH/x", shares), "back: the Mac's folder in another case (a case-insensitive disk)");
        Check.Equal(null, PathMapping.ToWindows("/Users/noahs/x", shares), "back: /Users/noahs is not /Users/noah");
        Check.Equal(null, PathMapping.ToWindows("/Applications/Safari.app", shares), "back: outside every share");
        Check.Equal(null, PathMapping.ToWindows("/Users/noah/../alice/x", shares), "back: `..` out of the share");

        // Random names round-trip both ways.
        var rng = new Random(17);
        int ok = 0;
        const string alphabet = "abcXYZ :?*<>|\"é\u0301ü日本 .-_";
        for (int i = 0; i < 1000; i++)
        {
            var comps = Enumerable.Range(0, rng.Next(1, 4)).Select(_ => new string(Enumerable.Range(0, rng.Next(1, 10)).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray()).Trim())
                                  .Where(c => c.Length > 0 && c != "." && c != ".." && !c.All(ch => ch == '.')).ToList();
            if (comps.Count == 0) { ok++; continue; }
            var macPath = "/Users/noah/" + string.Join('/', comps);
            var win = PathMapping.ToWindows(macPath, shares);
            var back = win == null ? null : PathMapping.ToMac(win, shares, out _);
            if (back != null && back.Normalize(NormalizationForm.FormC) == macPath.Normalize(NormalizationForm.FormC)) ok++;
        }
        Check.Equal(1000, ok, "1,000 random Mac paths to this PC's and back: the same file (in NFC terms)");

        Check.Equal("/Users/noah", PathMapping.DefaultMacPath("noah"), "a share's default folder: the account's home");
        Check.That(PathMapping.SameHost("Mac.local.", "MAC") && !PathMapping.SameHost("mac2.local", "mac"), "a host with and without .local (and its trailing dot) is one host; another is not");
    }
}
