using System.Runtime.InteropServices;

namespace Sill.Windows.Interop;

// COM by hand: every interface Sill calls is reached through its vtable, slot by slot, with the
// slot numbers taken from the Windows SDK's C headers (the `...Vtbl` structs, IUnknown's three
// first). No interop package: the client takes nothing but .NET and Windows (CLAUDE.md, zero
// dependencies). x64 and ARM64 only, where there is one calling convention.

/// <summary>A failed HRESULT, with the call that returned it.</summary>
public sealed class ComException : Exception
{
    public int HResult32 { get; }
    public ComException(string call, int hr) : base($"{call} failed: 0x{hr:X8}") { HResult32 = hr; HResult = hr; }
}

public static unsafe class Com
{
    /// <summary>The function in <paramref name="slot"/> of the object's vtable.</summary>
    public static void* Slot(IntPtr obj, int slot) => (*(void***)obj)[slot];

    public static void Check(int hr, string call)
    {
        if (hr < 0) throw new ComException(call, hr);
    }

    public static int QueryInterface(IntPtr obj, Guid iid, out IntPtr result)
    {
        IntPtr r = IntPtr.Zero;
        int hr = ((delegate* unmanaged<IntPtr, Guid*, IntPtr*, int>)Slot(obj, 0))(obj, &iid, &r);
        result = r;
        return hr;
    }

    public static IntPtr Query(IntPtr obj, Guid iid, string what)
    {
        Check(QueryInterface(obj, iid, out var r), $"QueryInterface({what})");
        return r;
    }

    public static uint AddRef(IntPtr obj) =>
        obj == IntPtr.Zero ? 0 : ((delegate* unmanaged<IntPtr, uint>)Slot(obj, 1))(obj);

    public static uint Release(IntPtr obj) =>
        obj == IntPtr.Zero ? 0 : ((delegate* unmanaged<IntPtr, uint>)Slot(obj, 2))(obj);

    /// <summary>Releases and clears a reference.</summary>
    public static void Release(ref IntPtr obj)
    {
        var p = obj;
        obj = IntPtr.Zero;
        Release(p);
    }

    // Calls of the shapes Sill needs. `h` is the object, `s` the slot.
    public static int Call(IntPtr h, int s) => ((delegate* unmanaged<IntPtr, int>)Slot(h, s))(h);
    public static int Call(IntPtr h, int s, IntPtr a) => ((delegate* unmanaged<IntPtr, IntPtr, int>)Slot(h, s))(h, a);
    public static int Call(IntPtr h, int s, IntPtr a, IntPtr b) => ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, int>)Slot(h, s))(h, a, b);
    public static int Call(IntPtr h, int s, IntPtr a, IntPtr b, IntPtr c) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, int>)Slot(h, s))(h, a, b, c);
    public static int Call(IntPtr h, int s, IntPtr a, IntPtr b, IntPtr c, IntPtr d) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int>)Slot(h, s))(h, a, b, c, d);
    public static int Call(IntPtr h, int s, IntPtr a, IntPtr b, IntPtr c, IntPtr d, IntPtr e) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int>)Slot(h, s))(h, a, b, c, d, e);
    public static int Call(IntPtr h, int s, IntPtr a, IntPtr b, IntPtr c, IntPtr d, IntPtr e, IntPtr f) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int>)Slot(h, s))(h, a, b, c, d, e, f);
    public static void CallVoid(IntPtr h, int s) => ((delegate* unmanaged<IntPtr, void>)Slot(h, s))(h);

    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(IntPtr p);

    public const uint COINIT_MULTITHREADED = 0x0;
}
