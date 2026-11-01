using Microsoft.Win32;
using Sill.Windows.Data;
using Sill.Windows.Net;
using Sill.Windows.Protocol;

namespace Sill.Windows.Shares;

/// <summary>"Open on Mac": a file or folder on a Mac's share, opened on the Mac with its default app
/// (kind 7 with `documents` and an empty bundle ID, to a host whose list says `documents` 1), from
/// Explorer's context menu (HKCU\Software\Classes\*\shell\SillOpenOnMac and Directory\shell, running
/// `Sill.exe --open-on-mac "%1"`). The path is mapped through the shares set up in the guide
/// (PathMapping); a file elsewhere on this PC has no path on the Mac, and the contract has no upload
/// for opening, so it is refused with "Copy it to the Mac share first".
///
/// <see cref="Open"/> is the entry point for a path in the running Sill: Program.cs takes the command
/// line of the first instance, and the single-instance pipe (the Coherence windows' work) calls it with
/// the path a second `Sill.exe --open-on-mac` handed over. A path given before any session is up waits
/// for the first one that can open it (<see cref="SessionReady"/>).</summary>
public static class OpenOnMac
{
    public const string Argument = "--open-on-mac";
    const string Verb = "SillOpenOnMac";
    static readonly string[] Keys = { @"Software\Classes\*\shell\" + Verb, @"Software\Classes\Directory\shell\" + Verb };
    static readonly object gate = new();
    static string? pending;

    /// <summary>What came of a request, in words for the user (Ok false: say it).</summary>
    public sealed record Result(bool Ok, string Message, bool Waiting = false);

    /// <summary>How the running Sill shows a result (a hint over the stream, a message box): set by
    /// the UI; a log line when unset.</summary>
    public static Action<Result>? Show { get; set; }

    /// <summary>The path after `--open-on-mac` in a command line, if any.</summary>
    public static string? FromArguments(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count - 1; i++) if (args[i] == Argument) return args[i + 1];
        return null;
    }

    /// <summary>Opens <paramref name="path"/> on its Mac. Call on the UI thread (it shows its result).</summary>
    public static Result Open(string path)
    {
        var result = Try(path);
        TestArguments.Log($"open on Mac: {path}: {result.Message}");
        if (Show != null) Show(result);
        else if (!result.Ok) MessageBox.Show(result.Message, "Open on Mac", MessageBoxButtons.OK, result.Waiting ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        return result;
    }

    static Result Try(string path)
    {
        var shares = ShareStore.Load();
        var macPath = PathMapping.ToMac(path, shares, out var via);
        if (macPath == null || via == null)
            return new(false, shares.Count == 0
                ? "Set up the Mac’s files first (View › The Mac’s Files on This PC…), then open files from its drive."
                : $"{Path.GetFileName(path.TrimEnd('\\'))} isn’t on the Mac’s share. Copy it to the Mac share first.");
        var sessions = FileService.Live.Select(f => f.Session).Where(s => s.IsOpen).ToList();
        var session = sessions.FirstOrDefault(s => Matches(s, via)) ?? (sessions.Count == 1 ? sessions[0] : null);
        if (session == null)
        {
            lock (gate) pending = path;
            return new(false, $"Connect to {via.MacName} first: the file opens there once Sill is connected.", Waiting: true);
        }
        if ((session.Windows?.Documents ?? 0) < Capabilities.Documents)
            return new(false, $"{session.MacName} can’t open files from this PC yet (update Sill on the Mac).");
        bool sent = session.Send(Kind.LaunchApp, Json.Encode(new LaunchApp { BundleID = "", Documents = new() { macPath } }));
        return sent ? new(true, $"Opening {Path.GetFileName(macPath)} on {session.MacName}.") : new(false, $"Lost the connection to {session.MacName}.");
    }

    static bool Matches(Session s, MacShare share)
    {
        if (share.MacID != null && s.MacFingerprint is { } fp && MacID.Make(fp) == share.MacID) return true;
        return string.Equals(s.MacName, share.MacName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A path given at launch, opened by the first session that can.</summary>
    public static void Later(string path) { lock (gate) pending = path; }

    /// <summary>A session's window list came: a path waiting for its Mac (or for any, when this is the
    /// only session) goes now; one of another Mac's keeps waiting.</summary>
    public static void SessionReady(Session session)
    {
        string? p;
        lock (gate) p = pending;
        if (p == null) return;
        PathMapping.ToMac(p, ShareStore.Load(), out var via);
        bool mine = via != null && Matches(session, via);
        if (!mine && FileService.Live.Count(f => f.Session.IsOpen) > 1) return;
        lock (gate) { if (pending != p) return; pending = null; }
        Open(p);
    }

    // MARK: Explorer's verb

    /// <summary>Whether Explorer's context menu has "Open on Mac" (this user's, no admin).</summary>
    public static bool Registered
    {
        get
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(Keys[0] + @"\command"); return k?.GetValue(null) is string; }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
        }
    }

    /// <summary>Adds or removes "Open on Mac" for files and folders, naming this Sill.exe where it is now.</summary>
    public static void Register(bool on)
    {
        foreach (var key in Keys)
        {
            if (!on) { Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false); continue; }
            if (Environment.ProcessPath is not { } exe) return;
            using var verb = Registry.CurrentUser.CreateSubKey(key);
            verb.SetValue("MUIVerb", "Open on Mac");
            verb.SetValue("Icon", $"\"{exe}\",0");
            using var command = verb.CreateSubKey("command");
            command.SetValue(null, $"\"{exe}\" {Argument} \"%1\"");
        }
    }
}
