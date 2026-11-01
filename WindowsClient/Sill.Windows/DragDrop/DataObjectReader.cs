using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Sill.Windows.Data;
using IComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Sill.Windows.DragDrop;

/// <summary>Files out of another app's data object (a drag over a Sill window, the clipboard through
/// OleGetClipboard): paths (CF_HDROP: Explorer, the desktop, most apps), or virtual files
/// (CFSTR_FILEDESCRIPTORW with CFSTR_FILECONTENTS by index: an Outlook attachment, a file inside a zip
/// in Explorer, a browser's download), copied to staging so they can go as kind 34 parts like any
/// file. Called on the thread the data object came to (an STA).</summary>
public static class DataObjectReader
{
    static readonly short CfDescriptor = Ole.Format("FileGroupDescriptorW");
    static readonly short CfContents = Ole.Format("FileContents");

    public static bool HasPaths(IComDataObject d) => Has(d, Ole.CF_HDROP);
    public static bool HasVirtual(IComDataObject d) => Has(d, CfDescriptor);
    public static bool HasOrigin(IComDataObject d) => Has(d, MacFilesDataObject.CfOrigin);

    static bool Has(IComDataObject d, short cf)
    {
        var f = Ole.Etc(cf, TYMED.TYMED_HGLOBAL);
        try { return d.QueryGetData(ref f) == Ole.S_OK; }
        catch (COMException) { return false; }
    }

    /// <summary>CF_HDROP's paths; empty without it.</summary>
    public static List<string> Paths(IComDataObject d) =>
        Memory(d, Ole.CF_HDROP) is { } b ? ShellFormats.ReadDropFiles(b) : new List<string>();

    /// <summary>The virtual files' names and sizes; empty without them.</summary>
    public static List<ShellFormats.Descriptor> Virtual(IComDataObject d) =>
        Memory(d, CfDescriptor) is { } b ? ShellFormats.ReadFileGroupDescriptor(b) : new List<ShellFormats.Descriptor>();

    /// <summary>Virtual file <paramref name="index"/> copied to <paramref name="path"/> (at most
    /// <paramref name="cap"/> bytes); false when the app would not give it.</summary>
    public static bool CopyVirtual(IComDataObject d, int index, string path, ulong cap)
    {
        var f = Ole.Etc(CfContents, TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL, index);
        STGMEDIUM m;
        try { d.GetData(ref f, out m); }
        catch (COMException) { return false; }
        try
        {
            if (m.tymed == TYMED.TYMED_HGLOBAL)
            {
                if (Ole.FromHGlobal(m.unionmember, (long)cap) is not { } bytes) return false;
                File.WriteAllBytes(path, bytes);
                return true;
            }
            if (m.tymed == TYMED.TYMED_ISTREAM && m.unionmember != IntPtr.Zero)
            {
                var stream = (IStream)Marshal.GetObjectForIUnknown(m.unionmember);
                try
                {
                    using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
                    var buffer = new byte[256 * 1024];
                    var read = Marshal.AllocHGlobal(4);
                    try
                    {
                        ulong total = 0;
                        while (true)
                        {
                            stream.Read(buffer, buffer.Length, read);
                            int n = Marshal.ReadInt32(read);
                            if (n <= 0) break;
                            total += (ulong)n;
                            if (total > cap) return false;
                            fs.Write(buffer, 0, n);
                        }
                    }
                    finally { Marshal.FreeHGlobal(read); }
                    return true;
                }
                finally { Marshal.ReleaseComObject(stream); }
            }
            return false;
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException) { return false; }
        finally { Ole.ReleaseStgMedium(ref m); }
    }

    /// <summary>The paths an app's files stand for here: CF_HDROP's as they are, else its virtual files
    /// copied into staging (marked to be removed once sent). Null, with why, when there are none or they
    /// are over <paramref name="cap"/> in all.</summary>
    public static (List<string> paths, HashSet<string> temporary)? Files(IComDataObject d, ulong cap, out string? why)
    {
        why = null;
        var paths = Paths(d);
        if (paths.Count > 0) return (paths, new HashSet<string>());
        var virt = Virtual(d);
        if (virt.Count == 0) { why = "no files"; return null; }
        // Only top-level files: a virtual folder's own entries (names with a backslash) would need a
        // tree rebuilt here, which no app this was written against offers.
        var top = virt.Select((v, i) => (v, i)).Where(x => !x.v.Folder && !x.v.Name.Contains('\\')).ToList();
        ulong total = (ulong)top.Sum(x => (long)x.v.Size);
        if (total > cap) { why = $"over {cap >> 20} MB"; return null; }
        var temp = new HashSet<string>();
        var list = new List<string>();
        foreach (var (v, i) in top)
        {
            var p = Staging.Temp(v.Name);
            if (!CopyVirtual(d, i, p, cap)) { foreach (var t in temp) Staging.RemoveTemp(t); why = $"{v.Name} could not be read"; return null; }
            list.Add(p); temp.Add(p);
        }
        return (list, temp);
    }

    static byte[]? Memory(IComDataObject d, short cf)
    {
        var f = Ole.Etc(cf, TYMED.TYMED_HGLOBAL);
        STGMEDIUM m;
        try { d.GetData(ref f, out m); }
        catch (COMException) { return null; }
        try { return m.tymed == TYMED.TYMED_HGLOBAL ? Ole.FromHGlobal(m.unionmember) : null; }
        finally { Ole.ReleaseStgMedium(ref m); }
    }
}
