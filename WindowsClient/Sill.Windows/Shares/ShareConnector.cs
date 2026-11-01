using System.Runtime.InteropServices;
using System.Text;
using Sill.Windows.Net;

namespace Sill.Windows.Shares;

/// <summary>The Mac's shares this PC was set up with (shares.json in %LOCALAPPDATA%\Sill): names and
/// paths only, never a password.</summary>
public sealed class ShareStore
{
    public List<MacShare> Shares { get; set; } = new();

    const string File = "shares.json";
    static readonly object gate = new();

    public static List<MacShare> Load() { lock (gate) return AppData.Read<ShareStore>(File).Shares; }

    /// <summary>Adds a share or replaces the one with its UNC name.</summary>
    public static void Save(MacShare share)
    {
        lock (gate)
        {
            var s = AppData.Read<ShareStore>(File);
            s.Shares.RemoveAll(x => PathMapping.SameHost(x.Host, share.Host) && string.Equals(x.Share, share.Share, StringComparison.OrdinalIgnoreCase));
            if (share.Drive != null) s.Shares.RemoveAll(x => string.Equals(x.Drive, share.Drive, StringComparison.OrdinalIgnoreCase));
            s.Shares.Add(share);
            AppData.Write(File, s);
        }
    }

    public static void Remove(MacShare share)
    {
        lock (gate)
        {
            var s = AppData.Read<ShareStore>(File);
            s.Shares.RemoveAll(x => PathMapping.SameHost(x.Host, share.Host) && string.Equals(x.Share, share.Share, StringComparison.OrdinalIgnoreCase));
            AppData.Write(File, s);
        }
    }
}

/// <summary>A Mac's SMB share mapped to a drive with Windows' own calls: the account's password kept by
/// Windows' Credential Manager (CredWriteW, a domain password for the Mac's host name, which Windows'
/// SMB client offers the Mac at every connection, so the drive comes back after a restart), then
/// WNetAddConnection2W with CONNECT_UPDATE_PROFILE (the mapping remembered). Sill never stores the
/// password itself.</summary>
public static class ShareConnector
{
    const int RESOURCETYPE_DISK = 1, CONNECT_UPDATE_PROFILE = 0x1, CRED_TYPE_DOMAIN_PASSWORD = 2, CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NETRESOURCE
    {
        public int dwScope, dwType, dwDisplayType, dwUsage;
        public string? lpLocalName, lpRemoteName, lpComment, lpProvider;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CREDENTIAL
    {
        public int Flags, Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)] static extern int WNetAddConnection2W(ref NETRESOURCE resource, string? password, string? user, int flags);
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)] static extern int WNetCancelConnection2W(string name, int flags, bool force);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredWriteW(ref CREDENTIAL credential, int flags);
    [DllImport("kernel32.dll")] static extern uint GetLogicalDrives();

    /// <summary>The drive letters free for a mapping, Z first (as Explorer offers them).</summary>
    public static List<string> FreeDrives()
    {
        uint used = GetLogicalDrives();
        var free = new List<string>();
        for (char c = 'Z'; c >= 'D'; c--) if ((used & (1u << (c - 'A'))) == 0) free.Add(c + ":");
        return free;
    }

    /// <summary>Saves the password for the Mac's host, then maps the share (to <paramref name="drive"/>,
    /// or without a letter when null). Null on success, else what went wrong in words.</summary>
    public static string? Connect(string host, string share, string? drive, string user, string password)
    {
        if (!SavePassword(host, user, password, out var credError)) return $"Windows didn’t keep the password ({credError}).";
        var nr = new NETRESOURCE { dwType = RESOURCETYPE_DISK, lpLocalName = drive, lpRemoteName = $@"\\{host}\{share}" };
        int r = WNetAddConnection2W(ref nr, password, user, drive != null ? CONNECT_UPDATE_PROFILE : 0);
        return r == 0 ? null : Describe(r, host, share, drive);
    }

    public static void Disconnect(string name)
    {
        try { WNetCancelConnection2W(name, CONNECT_UPDATE_PROFILE, true); } catch (DllNotFoundException) { }
    }

    static bool SavePassword(string host, string user, string password, out int error)
    {
        error = 0;
        var blob = Encoding.Unicode.GetBytes(password);
        var p = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, p, blob.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_DOMAIN_PASSWORD, TargetName = host, UserName = user,
                CredentialBlobSize = blob.Length, CredentialBlob = p, Persist = CRED_PERSIST_LOCAL_MACHINE,
                Comment = "Sill: the Mac's File Sharing",
            };
            if (CredWriteW(ref cred, 0)) return true;
            error = Marshal.GetLastPInvokeError();
            return false;
        }
        finally
        {
            // The password's bytes do not stay in this process's memory longer than the call.
            Array.Clear(blob);
            for (int i = 0; i < blob.Length; i++) Marshal.WriteByte(p, i, 0);
            Marshal.FreeHGlobal(p);
        }
    }

    /// <summary>WNet's errors in words, with what to do.</summary>
    public static string Describe(int error, string host, string share, string? drive) => error switch
    {
        53 or 1222 => $"Windows couldn’t reach {host}. Check that File Sharing is on in the Mac’s System Settings › General › Sharing, and that both are on the same network.",
        67 => $"{host} has no share named “{share}”. Turn on your account under Options… in the Mac’s File Sharing, or type the name of a folder it shares.",
        86 or 1326 => "The Mac refused the account name or password. Use the Mac account’s short name and its login password.",
        85 => $"{drive} is already in use. Pick another drive letter.",
        1219 => $"Windows is already connected to {host} with another account. Disconnect it in Explorer (or sign out of Windows) and try again.",
        1203 or 1200 => $"“\\\\{host}\\{share}” isn’t a name Windows can use.",
        1244 or 5 => "The Mac asked for an account it would let in. Check that your account is ticked under Windows File Sharing in the Mac’s File Sharing options.",
        _ => $"Windows couldn’t connect to \\\\{host}\\{share} (error {error}).",
    };
}
