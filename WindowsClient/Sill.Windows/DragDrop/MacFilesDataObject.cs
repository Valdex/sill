using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Sill.Windows.Data;
using Sill.Windows.Protocol;
using IComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using STATSTG = System.Runtime.InteropServices.ComTypes.STATSTG;

namespace Sill.Windows.DragDrop;

/// <summary>The Mac's files as Windows takes files it does not have yet: a lazy IDataObject, for the
/// clipboard (kind 32's `files`, set with OleSetClipboard) and for a drag out of a Mac window
/// (kind 36, DoDragDrop). Nothing is fetched until a paste or a drop asks:
/// <list type="bullet">
/// <item>CFSTR_FILEDESCRIPTORW names the files and their sizes (a folder as its zip, "‹name›.zip");</item>
/// <item>CFSTR_FILECONTENTS, by index, is an IStream (<see cref="DownloadStream"/>) that asks the Mac
/// for that file with kind 37 and reads its parts as they come: Explorer copies straight from it;</item>
/// <item>CF_HDROP, for apps that take only paths, waits for every file whole, moves them to
/// Downloads\From ‹Mac› and names them there (a folder unpacked);</item>
/// <item>IDataObjectAsyncCapability, so a target may do all of that on a thread of its own after the
/// drop or paste, with its own progress, rather than holding the drag until the last byte;</item>
/// <item>"Preferred DropEffect" copy, the text the Mac's item had, and "Sill.Origin", so the
/// clipboard's watcher never sends the Mac's own files back to it.</item>
/// </list>
/// Formats a target sets (Explorer's "Performed DropEffect", "Paste Succeeded", a drop description)
/// are kept and given back. COM calls come on the thread that owns the clipboard or the drag (an STA);
/// a wait inside one never pumps (Download.WaitFor).</summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class MacFilesDataObject : IComDataObject, IDataObjectAsyncCapability
{
    static readonly short CfDescriptor = Ole.Format("FileGroupDescriptorW");
    static readonly short CfContents = Ole.Format("FileContents");
    static readonly short CfPreferredEffect = Ole.Format("Preferred DropEffect");
    public static readonly short CfOrigin = Ole.Format("Sill.Origin");
    /// <summary>How long a paste or drop waits with nothing arriving before it gives up.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(60);

    readonly FileService service;
    readonly uint offer;
    readonly IReadOnlyList<OfferedFile> files;
    readonly string? text;
    readonly Dictionary<short, byte[]> set = new();
    int asyncMode = 1, inOperation;

    /// <summary>A target took the files (a drop's Drop, a paste's GetData of contents or paths).</summary>
    public event Action? Taken;

    public uint OfferId => offer;
    public FileService Service => service;

    public MacFilesDataObject(FileService service, uint offer, IReadOnlyList<OfferedFile> files, string? text = null)
    {
        this.service = service; this.offer = offer; this.files = files; this.text = text;
    }

    /// <summary>The name a file has here: a folder travels zipped.</summary>
    string WindowsName(int i) => WindowsNames.FromMac(files[i].Name) + (files[i].Folder == true ? ".zip" : "");

    FORMATETC[] Offered()
    {
        var list = new List<FORMATETC>
        {
            Ole.Etc(CfDescriptor, TYMED.TYMED_HGLOBAL),
            Ole.Etc(CfContents, TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL),
            Ole.Etc(Ole.CF_HDROP, TYMED.TYMED_HGLOBAL),
            Ole.Etc(CfPreferredEffect, TYMED.TYMED_HGLOBAL),
            Ole.Etc(CfOrigin, TYMED.TYMED_HGLOBAL),
        };
        if (text != null) list.Add(Ole.Etc(Ole.CF_UNICODETEXT, TYMED.TYMED_HGLOBAL));
        return list.ToArray();
    }

    // MARK: IDataObject

    public void GetData(ref FORMATETC format, out STGMEDIUM medium)
    {
        medium = default;
        short cf = format.cfFormat;
        if (cf == CfContents)
        {
            if (format.lindex < 0 || format.lindex >= files.Count) throw new COMException("No such file.", Ole.DV_E_LINDEX);
            var d = service.Fetch(offer, format.lindex, files[format.lindex]);
            Taken?.Invoke();
            if ((format.tymed & TYMED.TYMED_ISTREAM) != 0)
            {
                var stream = new DownloadStream(d, WindowsName(format.lindex));
                medium.tymed = TYMED.TYMED_ISTREAM;
                medium.unionmember = Marshal.GetComInterfaceForObject(stream, typeof(IStream));
                return;
            }
            if ((format.tymed & TYMED.TYMED_HGLOBAL) != 0)
            {
                // A target that wants the bytes in memory: whole first, and only a file of sensible size.
                if (d.Size > 256UL << 20) throw new COMException("Too large for memory.", Ole.DV_E_TYMED);
                if (d.WaitDone(Quiet) is not { } path || Directory.Exists(path)) throw new COMException(d.Failure?.Message ?? "The file didn’t arrive.", Ole.STG_E_READFAULT);
                medium.tymed = TYMED.TYMED_HGLOBAL;
                medium.unionmember = Ole.ToHGlobal(File.ReadAllBytes(path));
                return;
            }
            throw new COMException("", Ole.DV_E_TYMED);
        }
        if ((format.tymed & TYMED.TYMED_HGLOBAL) == 0) throw new COMException("", Ole.DV_E_TYMED);
        byte[] bytes;
        if (cf == CfDescriptor)
            bytes = ShellFormats.FileGroupDescriptor(files.Select((f, i) => new ShellFormats.Descriptor(WindowsName(i), f.Size, false, SizeKnown: f.Folder != true)).ToList());
        else if (cf == Ole.CF_HDROP) bytes = ShellFormats.DropFiles(RenderPaths());
        else if (cf == CfPreferredEffect) bytes = BitConverter.GetBytes(Ole.DROPEFFECT_COPY);
        else if (cf == CfOrigin) bytes = Encoding.UTF8.GetBytes($"Sill for Windows {Environment.ProcessId}");
        else if (cf == Ole.CF_UNICODETEXT && text != null) bytes = Encoding.Unicode.GetBytes(text + "\0");
        else if (set.TryGetValue(cf, out var stored)) bytes = stored;
        else throw new COMException("", Ole.DV_E_FORMATETC);
        medium.tymed = TYMED.TYMED_HGLOBAL;
        medium.unionmember = Ole.ToHGlobal(bytes);
    }

    /// <summary>CF_HDROP: every file whole, moved to Downloads\From ‹Mac›. Blocks the calling (COM)
    /// thread while they come; a target in async mode calls this from a thread of its own.</summary>
    List<string> RenderPaths()
    {
        Taken?.Invoke();
        var downloads = Enumerable.Range(0, files.Count).Select(i => service.Fetch(offer, i, files[i])).ToList();
        var dir = Staging.FromMac(service.MacName);
        var paths = new List<string>();
        foreach (var d in downloads)
        {
            if (d.WaitDone(Quiet) == null || d.Deliver(dir) is not { } p)
                throw new COMException($"{d.File.Name} didn’t arrive from {service.MacName}: {d.Failure?.Message ?? "nothing came for a minute"}", Ole.STG_E_READFAULT);
            paths.Add(p);
        }
        return paths;
    }

    public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => throw new COMException("", Ole.E_NOTIMPL);

    public int QueryGetData(ref FORMATETC format)
    {
        foreach (var f in Offered())
        {
            if (f.cfFormat != format.cfFormat) continue;
            return (f.tymed & format.tymed) != 0 ? Ole.S_OK : Ole.DV_E_TYMED;
        }
        if (set.ContainsKey(format.cfFormat)) return (format.tymed & TYMED.TYMED_HGLOBAL) != 0 ? Ole.S_OK : Ole.DV_E_TYMED;
        return Ole.DV_E_FORMATETC;
    }

    public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
    {
        formatOut = formatIn;
        formatOut.ptd = IntPtr.Zero;
        return Ole.E_NOTIMPL;
    }

    public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
    {
        // Kept when it is memory (what targets set: effects, a drop description); anything else ignored.
        if (medium.tymed == TYMED.TYMED_HGLOBAL && Ole.FromHGlobal(medium.unionmember, 1 << 20) is { } bytes)
            set[formatIn.cfFormat] = bytes;
        if (release && medium.pUnkForRelease == null)
        {
            if (medium.tymed == TYMED.TYMED_HGLOBAL) Ole.GlobalFree(medium.unionmember);
            else if (medium.tymed == TYMED.TYMED_ISTREAM && medium.unionmember != IntPtr.Zero) Marshal.Release(medium.unionmember);
        }
    }

    public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
    {
        if (direction != DATADIR.DATADIR_GET) throw new COMException("", Ole.E_NOTIMPL);
        var formats = Offered();
        int hr = Ole.SHCreateStdEnumFmtEtc((uint)formats.Length, formats, out var e);
        if (hr < 0 || e == null) throw new COMException("SHCreateStdEnumFmtEtc", hr);
        return e;
    }

    public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection) { connection = 0; return Ole.OLE_E_ADVISENOTSUPPORTED; }
    public void DUnadvise(int connection) => throw new COMException("", Ole.OLE_E_ADVISENOTSUPPORTED);
    public int EnumDAdvise(out IEnumSTATDATA? enumAdvise) { enumAdvise = null; return Ole.OLE_E_ADVISENOTSUPPORTED; }

    // MARK: IDataObjectAsyncCapability

    public int SetAsyncMode(int fDoOpAsync) { asyncMode = fDoOpAsync; return Ole.S_OK; }
    public int GetAsyncMode(out int pfIsOpAsync) { pfIsOpAsync = asyncMode; return Ole.S_OK; }
    public int StartOperation(IBindCtx? pbcReserved) { inOperation = 1; return Ole.S_OK; }
    public int InOperation(out int pfInAsyncOp) { pfInAsyncOp = inOperation; return Ole.S_OK; }
    public int EndOperation(int hResult, IBindCtx? pbcReserved, uint dwEffects) { inOperation = 0; return Ole.S_OK; }
}

/// <summary>One of the Mac's files as an IStream (CFSTR_FILECONTENTS): reads what has arrived and waits
/// (without pumping) for more, up to a minute with nothing arriving; Stat names it and gives its size
/// once the Mac's first part has said it. Read-only.</summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class DownloadStream : IStream
{
    const int STATFLAG_NONAME = 1, STGTY_STREAM = 2, STREAM_SEEK_SET = 0, STREAM_SEEK_CUR = 1, STREAM_SEEK_END = 2;
    readonly Download download;
    readonly string name;
    long position;

    public DownloadStream(Download download, string name) { this.download = download; this.name = name; }

    /// <summary>The size, waiting for the first part when only the offer has spoken (a folder's zip).</summary>
    long Size()
    {
        if (!download.SizeKnown && download.File.Folder == true) download.WaitFor(1, MacFilesDataObject.Quiet);
        if (download.Failed) throw new COMException(download.Failure?.Message ?? "", Ole.STG_E_READFAULT);
        return (long)download.Size;
    }

    public void Read(byte[] pv, int cb, IntPtr pcbRead)
    {
        int n = 0;
        if (cb > 0)
        {
            long size = Size();
            long want = Math.Min(position + cb, size);
            if (position < want)
            {
                if (!download.WaitFor(want, MacFilesDataObject.Quiet))
                    throw new COMException($"Nothing came from the Mac for {MacFilesDataObject.Quiet.TotalSeconds:0} s.", Ole.STG_E_READFAULT);
                if (download.Failed) throw new COMException(download.Failure?.Message ?? "", Ole.STG_E_READFAULT);
                n = download.ReadAt(position, pv.AsSpan(0, (int)Math.Min(cb, want - position)));
                position += n;
            }
        }
        if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, n);
    }

    public void Write(byte[] pv, int cb, IntPtr pcbWritten) => throw new COMException("", Ole.STG_E_INVALIDFUNCTION);

    public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
    {
        long p = dwOrigin switch
        {
            STREAM_SEEK_SET => dlibMove,
            STREAM_SEEK_CUR => position + dlibMove,
            STREAM_SEEK_END => Size() + dlibMove,
            _ => throw new COMException("", Ole.STG_E_INVALIDFUNCTION),
        };
        if (p < 0) throw new COMException("", Ole.STG_E_INVALIDFUNCTION);
        position = p;
        if (plibNewPosition != IntPtr.Zero) Marshal.WriteInt64(plibNewPosition, p);
    }

    public void SetSize(long libNewSize) => throw new COMException("", Ole.STG_E_INVALIDFUNCTION);

    public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
    {
        var buffer = new byte[256 * 1024];
        long done = 0;
        var tmp = Marshal.AllocHGlobal(4);
        try
        {
            while (done < cb)
            {
                Read(buffer, (int)Math.Min(buffer.Length, cb - done), tmp);
                int n = Marshal.ReadInt32(tmp);
                if (n == 0) break;
                pstm.Write(buffer, n, IntPtr.Zero);
                done += n;
            }
        }
        finally { Marshal.FreeHGlobal(tmp); }
        if (pcbRead != IntPtr.Zero) Marshal.WriteInt64(pcbRead, done);
        if (pcbWritten != IntPtr.Zero) Marshal.WriteInt64(pcbWritten, done);
    }

    public void Commit(int grfCommitFlags) { }
    public void Revert() => throw new COMException("", Ole.STG_E_INVALIDFUNCTION);
    public void LockRegion(long libOffset, long cb, int dwLockType) => throw new COMException("", Ole.STG_E_INVALIDFUNCTION);
    public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new COMException("", Ole.STG_E_INVALIDFUNCTION);
    public void Clone(out IStream ppstm) => throw new COMException("", Ole.E_NOTIMPL);

    public void Stat(out STATSTG pstatstg, int grfStatFlag)
    {
        pstatstg = new STATSTG
        {
            pwcsName = (grfStatFlag & STATFLAG_NONAME) != 0 ? null! : name,
            type = STGTY_STREAM,
            cbSize = Size(),
            grfMode = 0,   // STGM_READ
        };
    }
}
