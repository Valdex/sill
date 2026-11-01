using Sill.Windows.Interop;
using Sill.Windows.UI;
using Sill.Windows.Video;

namespace Sill.Windows;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (!Environment.Is64BitProcess)
        {
            MessageBox.Show("Sill needs 64-bit Windows.", "Sill", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        TestArguments.Read(args);
        if (args.Contains("--diag")) return Diagnostics.Run();
        if (args.Length >= 2 && args[0] == "--probe") return Diagnostics.Probe(args[1], args.Length > 2 ? args[2] : "sill/1");
        // One Sill per user: opened again, or with a pinned Mac app's `--launch`, it hands that to the
        // Sill already running (which comes forward or opens the app) and quits. Test runs run alone.
        var launch = Coherence.LaunchRequest.FromArgs(args);
        // Explorer's "Open on Mac" (Shares/OpenOnMac.cs): a Sill already running gets the path through
        // the single-instance pipe and opens it there; this Sill opens it once a session is up.
        var openOnMac = Shares.OpenOnMac.FromArguments(args);
        Shell.SingleInstance? instance = null;
        if (Coherence.LaunchRequest.HandsOff(args))
        {
            instance = Shell.SingleInstance.Claim();
            var line = openOnMac != null ? Coherence.LaunchRequest.OpenLine(openOnMac) : launch?.Encode() ?? Coherence.LaunchRequest.ShowLine;
            if (instance == null && Shell.SingleInstance.HandOff(line)) return 0;
        }
        ApplicationConfiguration.Initialize();
        if (openOnMac != null) Shares.OpenOnMac.Later(openOnMac);
        Application.ThreadException += (_, e) => TestArguments.Log($"UI exception: {e.Exception}");
        var connect = new ConnectForm { PendingLaunch = launch };
        instance?.Listen(line =>
        {
            if (connect.IsDisposed || !connect.IsHandleCreated) return;
            if (Coherence.LaunchRequest.OpenPath(line) is { } path)
            {
                try { connect.BeginInvoke(() => Shares.OpenOnMac.Open(path)); } catch (InvalidOperationException) { }
                return;
            }
            if (Coherence.LaunchRequest.Decode(line) is not var (_, request)) return;
            try { connect.BeginInvoke(() => connect.FromShell(request)); } catch (InvalidOperationException) { }
        });
        Application.Run(connect);
        instance?.Dispose();
        TestArguments.Log("exit");
        return 0;
    }
}

/// <summary>`Sill.exe --diag`: what this PC's Media Foundation and Direct3D offer Sill.</summary>
static class Diagnostics
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    /// <summary>`Sill.exe --probe host:port [alpn|plain]`: one dial, any key, and what came of it.</summary>
    public static int Probe(string address, string alpn)
    {
        AttachConsole(-1);
        var w = Console.Out;
        w.WriteLine();
        try
        {
            var endpoints = Net.Transport.Resolve(address, CancellationToken.None).GetAwaiter().GetResult();
            var id = Net.Identity.Load();
            w.WriteLine($"this PC: {Protocol.Base64Url.Encode(id.Fingerprint)}");
            using var d = Net.Transport.Dial(endpoints, alpn == "plain" ? null : alpn, null, CancellationToken.None).GetAwaiter().GetResult();
            w.WriteLine($"connected to {d.Endpoint}, tls {d.Tls}");
            if (d.Stream is System.Net.Security.SslStream ssl)
                w.WriteLine($"  {ssl.SslProtocol} {ssl.NegotiatedCipherSuite} alpn {ssl.NegotiatedApplicationProtocol} mac {Protocol.Base64Url.Encode(d.MacFingerprint!)} ({Protocol.MacID.Make(d.MacFingerprint!)})");
            return 0;
        }
        catch (Exception e)
        {
            w.WriteLine($"failed: {e.GetType().Name}: {e.Message}{(e.InnerException != null ? " / " + e.InnerException.Message : "")}");
            return 1;
        }
    }

    public static int Run()
    {
        AttachConsole(-1);
        var w = Console.Out;
        w.WriteLine();
        MF.Startup();
        foreach (var (label, cat, inT, outT, flags) in new[]
        {
            ("HEVC decoders (sync)", MF.MFT_CATEGORY_VIDEO_DECODER, (Guid?)MF.MFVideoFormat_HEVC, (Guid?)MF.MFVideoFormat_NV12, MF.MFT_ENUM_FLAG_SYNCMFT | MF.MFT_ENUM_FLAG_LOCALMFT | MF.MFT_ENUM_FLAG_SORTANDFILTER),
            ("HEVC decoders (all)", MF.MFT_CATEGORY_VIDEO_DECODER, (Guid?)MF.MFVideoFormat_HEVC, null, 0x3Fu),
            ("HEVC encoders (all)", MF.MFT_CATEGORY_VIDEO_ENCODER, (Guid?)null, (Guid?)MF.MFVideoFormat_HEVC, 0x3Fu),
        })
        {
            w.WriteLine(label + ":");
            foreach (var (a, name, hw, async) in MF.Enumerate(cat, flags, inT, outT))
            {
                w.WriteLine($"  {name}{(hw ? " [hardware]" : "")}{(async ? " [async]" : "")}");
                Com.Release(a);
            }
        }
        var (device, context, video) = D3D.CreateDevice();
        w.WriteLine($"D3D11 device, video support: {video}");
        var dec = HevcDecoder.Create(video ? device : IntPtr.Zero, 1920, 1080);
        w.WriteLine(dec == null ? $"decoder: none ({HevcDecoder.Problem})" : $"decoder: {dec.Name}, GPU {dec.OnGpu}");
        dec?.Dispose();
        Com.Release(context); Com.Release(device);
        return 0;
    }
}
