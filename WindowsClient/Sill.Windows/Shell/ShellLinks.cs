using System.Runtime.InteropServices;
using Sill.Windows.Coherence;
using Sill.Windows.Interop;
using Sill.Windows.Net;

namespace Sill.Windows.Shell;

// Shell links by vtable slot (shobjidl_core.h, objidl.h), as Interop/ does Direct3D:
// IShellLinkW: SetDescription 7, SetWorkingDirectory 9, GetArguments 10, SetArguments 11,
//   SetIconLocation 17, SetPath 20;
// IPersistFile: Save 6;
// ICustomDestinationList: BeginList 4, AppendCategory 5, CommitList 8, AbortList 11;
// IObjectArray: GetCount 3, GetAt 4; IObjectCollection: AddObject 5.
// COM objects made on the UI thread, which is single-threaded (WinForms' [STAThread]).
static unsafe class ShellCom
{
    public static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    public static readonly Guid CLSID_DestinationList = new("77f10cf0-3db5-4966-b520-b7c54fd35ed6");
    public static readonly Guid CLSID_EnumerableObjectCollection = new("2d3468c1-36a7-43b6-ac24-d3f02fd9607a");
    public static readonly Guid IID_IShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    public static readonly Guid IID_IPersistFile = new("0000010b-0000-0000-C000-000000000046");
    public static readonly Guid IID_ICustomDestinationList = new("6332debf-87b5-4670-90c0-5e57b408a49e");
    public static readonly Guid IID_IObjectArray = new("92ca9dcd-5622-4bba-a805-5e9f541bd8c9");
    public static readonly Guid IID_IObjectCollection = new("5632b1a4-e38a-400a-928a-d4cd63230295");
    const uint CLSCTX_INPROC_SERVER = 1;

    [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr obj);

    public static IntPtr Create(Guid clsid, Guid iid, string what)
    {
        Com.Check(CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out var obj), $"CoCreateInstance({what})");
        return obj;
    }

    public static int CallString(IntPtr obj, int slot, string s)
    {
        fixed (char* p = s) return Com.Call(obj, slot, (IntPtr)p);
    }

    /// <summary>A shell link to this Sill.exe with arguments, an icon, a description, the AppUserModelID
    /// of the Mac app it opens (so it groups with that app's windows) and, for a jump list, a title.</summary>
    public static IntPtr MakeLink(string exe, string arguments, string? icon, string description, string? aumid, string? title)
    {
        var link = Create(CLSID_ShellLink, IID_IShellLinkW, "ShellLink");
        try
        {
            Com.Check(CallString(link, 20, exe), "IShellLinkW.SetPath");
            Com.Check(CallString(link, 11, arguments), "IShellLinkW.SetArguments");
            CallString(link, 7, description);
            if (Path.GetDirectoryName(exe) is { } dir) CallString(link, 9, dir);
            fixed (char* p = icon ?? exe) Com.Call(link, 17, (IntPtr)p, IntPtr.Zero);
            if (aumid != null || title != null)
            {
                var store = Com.Query(link, PropertyStore.IID_IPropertyStore, "IPropertyStore");
                try
                {
                    if (aumid != null) PropertyStore.SetString(store, PropertyStore.AppUserModelID, aumid);
                    if (title != null) PropertyStore.SetString(store, PropertyStore.Title, title);
                    PropertyStore.Commit(store);
                }
                finally { Com.Release(store); }
            }
            return link;
        }
        catch
        {
            Com.Release(link);
            throw;
        }
    }

    /// <summary>A link's arguments (a removed jump list item's, to leave it out).</summary>
    public static string Arguments(IntPtr link)
    {
        var buffer = new char[1024];
        fixed (char* p = buffer)
        {
            if (Com.Call(link, 10, (IntPtr)p, (IntPtr)buffer.Length) < 0) return "";
            return new string(p);
        }
    }
}

/// <summary>Start menu shortcuts, one per Mac app launched from Sill (only those: Sill never fills the
/// Start menu with every app a Mac has), in Start › Sill: "Safari on Noah’s MacBook Pro", which runs
/// `Sill.exe --launch <macKey> <bundleID>` and carries that app's AppUserModelID, so Windows groups it
/// with the app's windows and shows the Mac app's icon on its taskbar button. A test run
/// (SILL_TEST_DATA) writes them into its data folder instead.</summary>
public static class StartMenu
{
    public static string Folder => TestArguments.IsolatedData
        ? Path.Combine(AppData.Folder, "Start Menu")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Sill");

    /// <summary>Makes (or makes again) the shortcut; its path, or null when the shell refused.</summary>
    public static string? Make(RecentApp app, string? icon)
    {
        if (Environment.ProcessPath is not { } exe || !LaunchRequest.ValidBundleID(app.BundleID)) return null;
        IntPtr link = IntPtr.Zero, file = IntPtr.Zero;
        try
        {
            Directory.CreateDirectory(Folder);
            var name = Aumid.DisplayName(app.AppName, app.MacName);
            var path = Path.Combine(Folder, Aumid.FileName(name) + ".lnk");
            link = ShellCom.MakeLink(exe, Aumid.LaunchArguments(app.MacKey, app.BundleID), icon, $"Opens {name} with Sill",
                                     Aumid.For(app.MacKey, app.BundleID), null);
            file = Com.Query(link, ShellCom.IID_IPersistFile, "IPersistFile");
            unsafe
            {
                fixed (char* p = path) Com.Check(Com.Call(file, 6, (IntPtr)p, (IntPtr)1), "IPersistFile.Save");
            }
            return path;
        }
        catch (Exception e) when (e is ComException or IOException or UnauthorizedAccessException)
        {
            TestArguments.Log($"start menu: {e.Message}");
            return null;
        }
        finally
        {
            Com.Release(file);
            Com.Release(link);
        }
    }
}

/// <summary>Sill's jump list (right-click its taskbar button): "Recent Mac apps", each opening its app
/// in Coherence through `--launch`. Items the user removed from the list are left out, as Windows asks
/// (appending one makes the whole category fail).</summary>
public static class JumpList
{
    public const string Category = "Recent Mac apps";

    public static void Update(IReadOnlyList<RecentApp> apps, Func<RecentApp, string?> iconFor)
    {
        if (Environment.ProcessPath is not { } exe) return;
        IntPtr list = IntPtr.Zero, removed = IntPtr.Zero, collection = IntPtr.Zero, array = IntPtr.Zero;
        bool begun = false;
        try
        {
            list = ShellCom.Create(ShellCom.CLSID_DestinationList, ShellCom.IID_ICustomDestinationList, "DestinationList");
            uint slots = 0;
            var iid = ShellCom.IID_IObjectArray;
            unsafe
            {
                Com.Check(((delegate* unmanaged<IntPtr, uint*, Guid*, IntPtr*, int>)Com.Slot(list, 4))(list, &slots, &iid, &removed), "ICustomDestinationList.BeginList");
            }
            begun = true;
            var gone = RemovedArguments(removed);
            collection = ShellCom.Create(ShellCom.CLSID_EnumerableObjectCollection, ShellCom.IID_IObjectCollection, "EnumerableObjectCollection");
            int added = 0;
            foreach (var app in apps)
            {
                if (added >= Math.Max(1, (int)slots)) break;
                if (!LaunchRequest.ValidBundleID(app.BundleID)) continue;
                var args = Aumid.LaunchArguments(app.MacKey, app.BundleID);
                if (gone.Contains(args)) continue;
                var name = Aumid.DisplayName(app.AppName, app.MacName);
                var link = ShellCom.MakeLink(exe, args, iconFor(app), $"Opens {name} with Sill", null, name);
                try { if (Com.Call(collection, 5, link) >= 0) added++; }
                finally { Com.Release(link); }
            }
            array = Com.Query(collection, ShellCom.IID_IObjectArray, "IObjectArray");
            if (added > 0)
            {
                // A category the user's settings refuse (recent items off) fails alone: the list still commits.
                int hr = AppendCategory(list, array);
                if (hr < 0) TestArguments.Log($"jump list: the category was refused (0x{hr:X8})");
            }
            Com.Check(Com.Call(list, 8), "ICustomDestinationList.CommitList");
            begun = false;
        }
        catch (ComException e)
        {
            TestArguments.Log($"jump list: {e.Message}");
            if (begun && list != IntPtr.Zero) Com.Call(list, 11);
        }
        finally
        {
            Com.Release(array); Com.Release(collection); Com.Release(removed); Com.Release(list);
        }
    }

    static unsafe int AppendCategory(IntPtr list, IntPtr array)
    {
        fixed (char* p = Category) return Com.Call(list, 5, (IntPtr)p, array);
    }

    static HashSet<string> RemovedArguments(IntPtr removed)
    {
        var set = new HashSet<string>();
        if (removed == IntPtr.Zero) return set;
        uint count = 0;
        unsafe { if (((delegate* unmanaged<IntPtr, uint*, int>)Com.Slot(removed, 3))(removed, &count) < 0) return set; }
        var iid = ShellCom.IID_IShellLinkW;
        for (uint i = 0; i < count; i++)
        {
            IntPtr link = IntPtr.Zero;
            unsafe
            {
                if (((delegate* unmanaged<IntPtr, uint, Guid*, IntPtr*, int>)Com.Slot(removed, 4))(removed, i, &iid, &link) < 0) continue;
            }
            try { set.Add(ShellCom.Arguments(link)); }
            finally { Com.Release(link); }
        }
        return set;
    }
}
