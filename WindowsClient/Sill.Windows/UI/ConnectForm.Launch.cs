using Sill.Windows.Coherence;
using Sill.Windows.Net;

namespace Sill.Windows.UI;

// The shell's way into Sill: `Sill.exe --launch <macKey> <bundleID>` (a Mac app pinned to the
// taskbar, its Start menu shortcut, the jump list) and Sill opened again, as the first Sill or handed
// to it by a later one over the single-instance pipe (Program.cs). ConnectForm.cs calls in twice:
// its auto-connect stands aside for a launch, and a stream window it makes takes the launch.
public sealed partial class ConnectForm
{
    LaunchRequest? atStart, forStream;
    bool startHooked;

    /// <summary>The launch Sill was started with: it connects to that Mac (in place of the last one)
    /// once the window shows.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal LaunchRequest? PendingLaunch
    {
        get => atStart;
        set
        {
            atStart = value;
            if (value == null || startHooked) return;
            startHooked = true;
            Shown += (_, _) => { if (atStart is { } l) { atStart = null; FromShell(l); } };
        }
    }

    /// <summary>A launch (or, null, Sill opened again) from the shell.</summary>
    internal void FromShell(LaunchRequest? launch)
    {
        if (launch == null) { BringSillForward(); return; }
        TestArguments.Log($"shell: launch {launch.BundleID} on {launch.MacKey}");
        if (stream is { IsDisposed: false } s && s.IsFor(launch.MacKey))
        {
            s.LaunchFromShell(launch);
            return;
        }
        var mac = SavedMacs.Load().Macs.FirstOrDefault(m => m.MacID == launch.MacKey);
        if (mac == null)
        {
            Show();
            Activate();
            SetStatus("That Mac app’s Mac isn’t one this PC has paired with. Connect to it first.", true);
            return;
        }
        // Another Mac than the one streaming: that one is left for this one.
        if (stream is { IsDisposed: false } other) other.Close();
        forStream = launch;
        Show();
        _ = AutoConnect(mac);
    }

    /// <summary>The stream window just made takes the launch waiting for it (it opens the app once its
    /// session's window list comes).</summary>
    void HandStreamItsLaunch()
    {
        if (forStream is not { } l || stream == null) return;
        forStream = null;
        stream.LaunchFromShell(l);
    }

    void BringSillForward()
    {
        if (stream is { IsDisposed: false } s)
        {
            if (s.Coherence is { Active: true } c) { c.BringAllForward(); return; }
            if (s.WindowState == FormWindowState.Minimized) s.WindowState = FormWindowState.Normal;
            s.Show();
            s.Activate();
            return;
        }
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        Activate();
    }
}
