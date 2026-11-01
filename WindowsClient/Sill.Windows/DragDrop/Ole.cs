using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using IComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Sill.Windows.DragDrop;

// OLE's drag and drop and clipboard through .NET's built-in COM interop: the interfaces .NET has
// (System.Runtime.InteropServices.ComTypes' IDataObject, IStream, IEnumFORMATETC) and the three it
// does not, declared here from oleidl.h and shldisp.h ([PreserveSig], so every HRESULT is the
// caller's to see). A managed class implementing them is handed to OLE as a COM object by the
// runtime's own callable wrapper; no package, no generated code.

[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IOleDropTarget
{
    // POINTL by value: two LONGs, passed in one 64-bit register on x64 and ARM64 alike, as a long is
    // (x in the low half).
    [PreserveSig] int DragEnter([In, MarshalAs(UnmanagedType.Interface)] IComDataObject pDataObj, int grfKeyState, long pt, ref int pdwEffect);
    [PreserveSig] int DragOver(int grfKeyState, long pt, ref int pdwEffect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop([In, MarshalAs(UnmanagedType.Interface)] IComDataObject pDataObj, int grfKeyState, long pt, ref int pdwEffect);
}

[ComImport, Guid("00000121-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IOleDropSource
{
    [PreserveSig] int QueryContinueDrag(int fEscapePressed, int grfKeyState);
    [PreserveSig] int GiveFeedback(int dwEffect);
}

/// <summary>shldisp.h's IDataObjectAsyncCapability (IAsyncOperation before Vista): a target that sees
/// it may take the data on a thread of its own after the drop, so a drop of files still downloading
/// never holds the drag (or the paste) until the last byte.</summary>
[ComImport, Guid("3D8B0590-F691-11d2-8EA9-006097DF5BD4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDataObjectAsyncCapability
{
    [PreserveSig] int SetAsyncMode(int fDoOpAsync);
    [PreserveSig] int GetAsyncMode(out int pfIsOpAsync);
    [PreserveSig] int StartOperation(IBindCtx? pbcReserved);
    [PreserveSig] int InOperation(out int pfInAsyncOp);
    [PreserveSig] int EndOperation(int hResult, IBindCtx? pbcReserved, uint dwEffects);
}

public static class Ole
{
    public const int S_OK = 0, S_FALSE = 1, E_NOTIMPL = unchecked((int)0x80004001), E_FAIL = unchecked((int)0x80004005), E_INVALIDARG = unchecked((int)0x80070057);
    public const int DV_E_FORMATETC = unchecked((int)0x80040064), DV_E_TYMED = unchecked((int)0x80040069), DV_E_LINDEX = unchecked((int)0x80040068);
    public const int OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003), STG_E_READFAULT = unchecked((int)0x8003001E), STG_E_INVALIDFUNCTION = unchecked((int)0x80030001);
    public const int DRAGDROP_S_DROP = 0x00040100, DRAGDROP_S_CANCEL = 0x00040101, DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;
    public const int DROPEFFECT_NONE = 0, DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2, DROPEFFECT_LINK = 4;
    public const int MK_LBUTTON = 1, MK_RBUTTON = 2, MK_SHIFT = 4, MK_CONTROL = 8, MK_MBUTTON = 0x10, MK_ALT = 0x20;
    public const short CF_UNICODETEXT = 13, CF_HDROP = 15, CF_DIB = 8, CF_DIBV5 = 17;
    public const uint GMEM_MOVEABLE = 0x2;

    [DllImport("ole32.dll")] public static extern int OleInitialize(IntPtr reserved);
    [DllImport("ole32.dll")] public static extern int RegisterDragDrop(IntPtr hwnd, IOleDropTarget target);
    [DllImport("ole32.dll")] public static extern int RevokeDragDrop(IntPtr hwnd);
    [DllImport("ole32.dll")] public static extern int DoDragDrop(IComDataObject data, IOleDropSource source, int okEffects, out int effect);
    [DllImport("ole32.dll")] public static extern int OleSetClipboard(IComDataObject? data);
    [DllImport("ole32.dll")] public static extern int OleGetClipboard(out IComDataObject? data);
    [DllImport("ole32.dll")] public static extern int OleIsCurrentClipboard(IComDataObject data);
    [DllImport("ole32.dll")] public static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    [DllImport("shell32.dll")] public static extern int SHCreateStdEnumFmtEtc(uint count, [In] FORMATETC[] formats, out IEnumFORMATETC? enumerator);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern uint RegisterClipboardFormatW(string name);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern UIntPtr GlobalSize(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr GlobalFree(IntPtr h);

    /// <summary>A registered format's number as FORMATETC's cfFormat carries it (a CLIPFORMAT, 16 bits).</summary>
    public static short Format(string name) => unchecked((short)RegisterClipboardFormatW(name));

    /// <summary>Movable global memory holding <paramref name="bytes"/> (what the clipboard and
    /// STGMEDIUM's HGLOBAL want); the receiver frees it.</summary>
    public static IntPtr ToHGlobal(ReadOnlySpan<byte> bytes)
    {
        var h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)Math.Max(1, bytes.Length));
        if (h == IntPtr.Zero) throw new OutOfMemoryException("GlobalAlloc");
        var p = GlobalLock(h);
        try { unsafe { bytes.CopyTo(new Span<byte>((void*)p, bytes.Length)); } }
        finally { GlobalUnlock(h); }
        return h;
    }

    /// <summary>A global memory block's bytes (its whole size: the clipboard may round it up).</summary>
    public static byte[]? FromHGlobal(IntPtr h, long cap = 64L << 20)
    {
        if (h == IntPtr.Zero) return null;
        long size = (long)(ulong)GlobalSize(h);
        if (size <= 0 || size > cap) return null;
        var p = GlobalLock(h);
        if (p == IntPtr.Zero) return null;
        try
        {
            var b = new byte[size];
            Marshal.Copy(p, b, 0, (int)size);
            return b;
        }
        finally { GlobalUnlock(h); }
    }

    public static FORMATETC Etc(short format, TYMED tymed, int lindex = -1) =>
        new() { cfFormat = format, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = lindex, ptd = IntPtr.Zero, tymed = tymed };

    /// <summary>A screen point from POINTL as IDropTarget passes it.</summary>
    public static (int x, int y) Point(long pt) => ((int)(pt & 0xFFFFFFFF), (int)(pt >> 32));
}
