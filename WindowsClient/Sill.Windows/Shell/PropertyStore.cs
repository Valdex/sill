using System.Runtime.InteropServices;
using Sill.Windows.Interop;

namespace Sill.Windows.Shell;

// IPropertyStore by vtable slot (propsys.h: IUnknown's three, then GetCount 3, GetAt 4, GetValue 5,
// SetValue 6, Commit 7), as Interop/ reaches Direct3D: a window's (SHGetPropertyStoreForWindow) for its
// taskbar identity, and a shell link's for its AppUserModelID and title. Strings only.

/// <summary>PROPERTYKEY: a format ID and a property ID.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
    public PROPERTYKEY(string fmtid, uint pid) { this.fmtid = new Guid(fmtid); this.pid = pid; }
}

/// <summary>PROPVARIANT on a 64-bit process: the type (2 bytes), three reserved words, then the
/// 16-byte union, of which only a pointer (VT_LPWSTR's string) is used here.</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct PROPVARIANT
{
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(8)] public IntPtr pointer;
}

public static unsafe class PropertyStore
{
    public static readonly Guid IID_IPropertyStore = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
    public const ushort VT_EMPTY = 0, VT_LPWSTR = 31;

    // propkey.h
    const string AppUserModel = "9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3";
    public static readonly PROPERTYKEY AppUserModelID = new(AppUserModel, 5);
    public static readonly PROPERTYKEY RelaunchCommand = new(AppUserModel, 2);
    public static readonly PROPERTYKEY RelaunchIconResource = new(AppUserModel, 3);
    public static readonly PROPERTYKEY RelaunchDisplayNameResource = new(AppUserModel, 4);
    /// <summary>System.Title: a jump list item's text.</summary>
    public static readonly PROPERTYKEY Title = new("F29F85E0-4FF9-1068-AB91-08002B27B3D9", 2);

    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, out IntPtr store);

    /// <summary>The window's property store (released by the caller), or zero.</summary>
    public static IntPtr ForWindow(IntPtr hwnd)
    {
        var iid = IID_IPropertyStore;
        return SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) >= 0 ? store : IntPtr.Zero;
    }

    /// <summary>Sets a string property, or clears it (VT_EMPTY) for null. The store copies the value.</summary>
    public static int SetString(IntPtr store, PROPERTYKEY key, string? value)
    {
        var v = new PROPVARIANT { vt = value == null ? VT_EMPTY : VT_LPWSTR, pointer = value == null ? IntPtr.Zero : Marshal.StringToCoTaskMemUni(value) };
        try
        {
            return ((delegate* unmanaged<IntPtr, PROPERTYKEY*, PROPVARIANT*, int>)Com.Slot(store, 6))(store, &key, &v);
        }
        finally
        {
            if (v.pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(v.pointer);
        }
    }

    public static int Commit(IntPtr store) => Com.Call(store, 7);
}

/// <summary>A window's identity on the taskbar (Shell/TaskbarIdentity): its AppUserModelID, so every
/// window of one Mac app groups under one button of its own (not under Sill's), and the relaunch
/// properties, so that button pinned opens the Mac app again through `Sill.exe --launch`. Set once
/// the window has a handle and before it is first shown (the taskbar reads them as the button is
/// made), and emptied before the window is destroyed, as SHGetPropertyStoreForWindow asks.</summary>
public static class TaskbarIdentity
{
    /// <summary>The relaunch properties first and the ID last: the taskbar reads the set when the ID
    /// changes. False when the shell gave no store (the window then groups as Sill's).</summary>
    public static bool Set(IntPtr hwnd, string aumid, string? relaunchCommand, string? displayName, string? iconPath)
    {
        var store = PropertyStore.ForWindow(hwnd);
        if (store == IntPtr.Zero) return false;
        try
        {
            if (relaunchCommand != null && displayName != null)
            {
                PropertyStore.SetString(store, PropertyStore.RelaunchCommand, relaunchCommand);
                PropertyStore.SetString(store, PropertyStore.RelaunchDisplayNameResource, displayName);
                if (iconPath != null) PropertyStore.SetString(store, PropertyStore.RelaunchIconResource, iconPath + ",0");
            }
            return PropertyStore.SetString(store, PropertyStore.AppUserModelID, aumid) >= 0;
        }
        finally { Com.Release(store); }
    }

    /// <summary>The relaunch icon alone, once the Mac app's icon has come.</summary>
    public static void SetIcon(IntPtr hwnd, string iconPath)
    {
        var store = PropertyStore.ForWindow(hwnd);
        if (store == IntPtr.Zero) return;
        try { PropertyStore.SetString(store, PropertyStore.RelaunchIconResource, iconPath + ",0"); }
        finally { Com.Release(store); }
    }

    /// <summary>Every property back to VT_EMPTY, before the window is destroyed.</summary>
    public static void Clear(IntPtr hwnd)
    {
        var store = PropertyStore.ForWindow(hwnd);
        if (store == IntPtr.Zero) return;
        try
        {
            PropertyStore.SetString(store, PropertyStore.AppUserModelID, null);
            PropertyStore.SetString(store, PropertyStore.RelaunchCommand, null);
            PropertyStore.SetString(store, PropertyStore.RelaunchDisplayNameResource, null);
            PropertyStore.SetString(store, PropertyStore.RelaunchIconResource, null);
        }
        finally { Com.Release(store); }
    }
}
