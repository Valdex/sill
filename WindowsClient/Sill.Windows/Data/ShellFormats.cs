using System.Buffers.Binary;
using System.Text;

namespace Sill.Windows.Data;

/// <summary>The shell's clipboard and drag formats as bytes, written and read by hand (shlobj_core.h's
/// layouts, x64 and ARM64 alike): CF_HDROP's DROPFILES and the virtual files' FILEGROUPDESCRIPTORW.
/// Pure, so WindowsClient/Sill.Checks checks every offset; the OLE code only copies them into memory.</summary>
public static class ShellFormats
{
    /// <summary>DROPFILES: pFiles (the list's offset), pt, fNC, fWide; then the paths, each ending
    /// in a NUL, and one more NUL.</summary>
    public const int DropFilesHeader = 20;
    /// <summary>FILEDESCRIPTORW: dwFlags, clsid, sizel, pointl, dwFileAttributes, three FILETIMEs,
    /// nFileSizeHigh, nFileSizeLow, cFileName[260].</summary>
    public const int FileDescriptorSize = 592;
    public const int MaxPath = 260;

    public const uint FD_ATTRIBUTES = 0x4, FD_WRITESTIME = 0x20, FD_FILESIZE = 0x40, FD_PROGRESSUI = 0x4000, FD_UNICODE = 0x80000000;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

    public static byte[] DropFiles(IReadOnlyList<string> paths)
    {
        var list = string.Concat(paths.Select(p => p + "\0")) + "\0";
        var bytes = new byte[DropFilesHeader + Encoding.Unicode.GetByteCount(list)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, DropFilesHeader);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);   // fWide: UTF-16
        Encoding.Unicode.GetBytes(list, bytes.AsSpan(DropFilesHeader));
        return bytes;
    }

    /// <summary>The paths of a DROPFILES, wide or not; empty when it does not read.</summary>
    public static List<string> ReadDropFiles(ReadOnlySpan<byte> d)
    {
        var paths = new List<string>();
        if (d.Length < DropFilesHeader) return paths;
        int offset = BinaryPrimitives.ReadInt32LittleEndian(d);
        bool wide = BinaryPrimitives.ReadInt32LittleEndian(d[16..]) != 0;
        if (offset < DropFilesHeader || offset > d.Length) return paths;
        var text = wide ? Encoding.Unicode.GetString(d[offset..(offset + (d.Length - offset) / 2 * 2)]) : Encoding.Default.GetString(d[offset..]);
        foreach (var p in text.Split('\0'))
        {
            if (p.Length == 0) break;
            paths.Add(p);
        }
        return paths;
    }

    /// <summary>A virtual file: <paramref name="SizeKnown"/> false leaves FD_FILESIZE out (a folder's
    /// zip, whose size the Mac says only with its first part), so a target reads to the end instead.</summary>
    public sealed record Descriptor(string Name, ulong Size, bool Folder, DateTime? Written = null, bool SizeKnown = true);

    /// <summary>FILEGROUPDESCRIPTORW: cItems, then one FILEDESCRIPTORW each, with its size, attributes
    /// and progress UI flags. A name is a path relative to the drop (a folder's files carry its name
    /// and a backslash), at most 259 units.</summary>
    public static byte[] FileGroupDescriptor(IReadOnlyList<Descriptor> files)
    {
        var d = new byte[4 + FileDescriptorSize * files.Count];
        BinaryPrimitives.WriteInt32LittleEndian(d, files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            var f = files[i];
            var s = d.AsSpan(4 + i * FileDescriptorSize, FileDescriptorSize);
            uint flags = FD_ATTRIBUTES | FD_PROGRESSUI | FD_UNICODE | (f.Folder || !f.SizeKnown ? 0 : FD_FILESIZE) | (f.Written != null ? FD_WRITESTIME : 0);
            BinaryPrimitives.WriteUInt32LittleEndian(s, flags);
            BinaryPrimitives.WriteUInt32LittleEndian(s[36..], f.Folder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL);
            if (f.Written is { } w) BinaryPrimitives.WriteInt64LittleEndian(s[56..], w.ToFileTimeUtc());
            BinaryPrimitives.WriteUInt32LittleEndian(s[64..], (uint)(f.Size >> 32));
            BinaryPrimitives.WriteUInt32LittleEndian(s[68..], (uint)f.Size);
            var name = f.Name.Length >= MaxPath ? f.Name[..(MaxPath - 1)] : f.Name;
            Encoding.Unicode.GetBytes(name, s[72..]);
        }
        return d;
    }

    /// <summary>A FILEGROUPDESCRIPTORW's files (an app's virtual files: an Outlook attachment, a zip's
    /// contents in Explorer); empty when it does not read. A size the flags do not vouch for is 0.</summary>
    public static List<Descriptor> ReadFileGroupDescriptor(ReadOnlySpan<byte> d)
    {
        var files = new List<Descriptor>();
        if (d.Length < 4) return files;
        int count = BinaryPrimitives.ReadInt32LittleEndian(d);
        if (count < 0 || count > 100_000 || d.Length < 4 + (long)count * FileDescriptorSize) return files;
        for (int i = 0; i < count; i++)
        {
            var s = d.Slice(4 + i * FileDescriptorSize, FileDescriptorSize);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(s);
            uint attrs = BinaryPrimitives.ReadUInt32LittleEndian(s[36..]);
            ulong size = (flags & FD_FILESIZE) != 0 ? ((ulong)BinaryPrimitives.ReadUInt32LittleEndian(s[64..]) << 32) | BinaryPrimitives.ReadUInt32LittleEndian(s[68..]) : 0;
            var nameBytes = s[72..];
            var name = Encoding.Unicode.GetString(nameBytes);
            int nul = name.IndexOf('\0');
            if (nul >= 0) name = name[..nul];
            DateTime? written = (flags & FD_WRITESTIME) != 0 ? DateTime.FromFileTimeUtc(Math.Clamp(BinaryPrimitives.ReadInt64LittleEndian(s[56..]), 0, DateTime.MaxValue.ToFileTimeUtc())) : null;
            files.Add(new Descriptor(name, size, (flags & FD_ATTRIBUTES) != 0 && (attrs & FILE_ATTRIBUTE_DIRECTORY) != 0, written, (flags & FD_FILESIZE) != 0));
        }
        return files;
    }
}
