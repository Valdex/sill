using System.IO.Compression;
using System.Text;

namespace Sill.Windows.Data;

/// <summary>A folder as one file for kinds 34–37 (docs/coherence-protocol.md: a folder travels zipped).
/// The Mac zips with `ditto -c -k --sequesterRsrc --keepParent` (#66's FolderZip): a standard zip with
/// the folder itself at its top and the resource forks and extended attributes as AppleDouble files
/// under `__MACOSX/`. System.IO.Compression reads that; here the top folder is kept the same way, the
/// `__MACOSX/` part and `._` files are left out (they mean nothing to Windows), and every entry's path
/// is checked before anything is written: no entry lands outside the folder it unpacks into (a `..`, a
/// rooted path, a drive), each name is made a Windows one (WindowsNames), and the whole is held to a
/// size and a count, so a zip from the network can neither escape nor fill the disk. Pure .NET (no
/// Win32): WindowsClient/Sill.Checks zips and unpacks real folders with it.</summary>
public static class FolderZip
{
    /// <summary>The most an unpacked folder may hold here.</summary>
    public const long MaxUnpacked = 8L << 30;
    public const int MaxEntries = 200_000;

    /// <summary>Zips <paramref name="folder"/> into <paramref name="zipPath"/> with the folder itself
    /// at its top (ditto's --keepParent, which the Mac's unpack expects: the transfer's name is the
    /// folder's with ".zip", and the zip holds that folder). Names in UTF-8 (the zip's language flag).</summary>
    public static void Zip(string folder, string zipPath, CancellationToken ct = default)
    {
        var root = new DirectoryInfo(folder);
        if (!root.Exists) throw new DirectoryNotFoundException(folder);
        using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: Encoding.UTF8);
        // The folder's own entry first, so an empty folder still arrives as a folder.
        string top = WindowsNames.ToMac(root.Name);
        zip.CreateEntry(top + "/");
        foreach (var info in root.EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(root.FullName, info.FullName).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = top + "/" + string.Join('/', rel.Select(WindowsNames.ToMac));
            if (info is DirectoryInfo) { zip.CreateEntry(name + "/"); continue; }
            var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
            entry.LastWriteTime = info.LastWriteTime;
            using var src = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = entry.Open();
            src.CopyTo(dst);
        }
    }

    /// <summary>The size a folder zips from (its files' bytes), and its file count, for the caps a drop
    /// is held to before anything is zipped.</summary>
    public static (long bytes, int files) Measure(string folder)
    {
        long bytes = 0; int files = 0;
        foreach (var f in new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }))
        {
            bytes += f.Length; files++;
        }
        return (bytes, files);
    }

    /// <summary>Why an unpack stopped.</summary>
    public sealed class UnpackException : Exception { public UnpackException(string m) : base(m) { } }

    /// <summary>Unpacks <paramref name="zipPath"/> into <paramref name="directory"/> and returns the
    /// folder it held: its one top folder when it has one (ditto's --keepParent), else a folder named
    /// <paramref name="name"/> made around its entries. Throws UnpackException for an entry that would
    /// land outside, or a zip past the caps; what was written by then is left for the caller to remove.</summary>
    public static string Unpack(string zipPath, string directory, string name, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entries = new List<(ZipArchiveEntry entry, string[] parts, bool folder)>();
        long total = 0;
        foreach (var e in zip.Entries)
        {
            if (entries.Count >= MaxEntries) throw new UnpackException($"more than {MaxEntries:N0} entries");
            if (EntryPath(e.FullName) is not { } parts) throw new UnpackException($"an entry outside the folder ({e.FullName})");
            if (parts.Length == 0) continue;
            if (parts[0] == "__MACOSX" || parts[^1].StartsWith("._", StringComparison.Ordinal) || parts[^1] == ".DS_Store") continue;
            bool folder = e.FullName.EndsWith('/') || e.FullName.EndsWith('\\');
            total += e.Length;
            if (total > MaxUnpacked) throw new UnpackException($"more than {MaxUnpacked >> 30} GB unpacked");
            entries.Add((e, parts.Select(WindowsNames.FromMac).ToArray(), folder));
        }
        // One top folder holding everything: it is the folder. Else the entries go into one named for the transfer.
        var tops = entries.Select(x => x.parts[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool kept = tops.Count == 1 && entries.All(x => x.folder || x.parts.Length > 1);
        string root = Path.GetFullPath(kept ? Path.Combine(directory, tops[0]) : Path.Combine(directory, WindowsNames.FromMac(name)));
        string baseDir = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        foreach (var (entry, parts, folder) in entries)
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(kept ? baseDir : root, Path.Combine(parts)));
            // Belt and braces: the parts were checked, the joined path is checked too.
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
                throw new UnpackException($"an entry outside the folder ({entry.FullName})");
            if (folder) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var src = entry.Open())
            using (var dst = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                src.CopyTo(dst);
            try { File.SetLastWriteTime(target, entry.LastWriteTime.DateTime); } catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
        }
        return root;
    }

    /// <summary>An entry's path as its components, or null when it would leave the folder: rooted,
    /// a drive, or a `..` anywhere. `.` and empty components (a doubled slash) are dropped.</summary>
    public static string[]? EntryPath(string fullName)
    {
        var n = fullName.Replace('\\', '/');
        if (n.StartsWith('/') || (n.Length >= 2 && n[1] == ':')) return null;
        var parts = n.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(p => p != ".").ToArray();
        if (parts.Any(p => p == "..")) return null;
        return parts;
    }
}
