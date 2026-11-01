using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sill.Windows.Protocol;

namespace Sill.TestHost;

// TEST ONLY: a stand-in Mac for the Windows client. Not Sill's host: a home door that speaks the
// wire format (plain, or TLS 1.3 with both keys and pairing by the typed code), a test pattern
// encoded as HEVC, and every message a device sends printed. Loopback unless --any.
//
//   SillTestHost [--tls] [--pairing] [--port N] [--size WxH] [--fps N] [--any] [--seconds N]
//
// The pattern shows what the client sends: a cross where its pointer is, a disc while the left
// button is down, a square that changes colour with each key press, a bar that moves every frame.
static class Program
{
    static bool tls, requirePairing, any;
    static int port = 47100, width = 1600, height = 1000, fps = 60;
    static int bitrate = 15_000_000;
    static X509Certificate2? hostCert;
    static byte[] hostFp = Array.Empty<byte>();
    static readonly HashSet<string> paired = new();
    static readonly byte[] recognitionKey = RandomNumberGenerator.GetBytes(32);
    static string code = "";
    static int tries = 5;
    static readonly object log = new();

    static void Log(string s) { lock (log) Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {s}"); }

    static int Main(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--tls": tls = true; break;
                case "--pairing": tls = true; requirePairing = true; break;
                case "--any": any = true; break;
                case "--port": port = int.Parse(args[++i]); break;
                case "--fps": fps = int.Parse(args[++i]); break;
                case "--size": var p = args[++i].Split('x'); width = int.Parse(p[0]) & ~1; height = int.Parse(p[1]) & ~1; break;
                case "--seconds": var s = int.Parse(args[++i]); _ = Task.Delay(s * 1000).ContinueWith(_ => { Log("time is up"); Environment.Exit(0); }); break;
            }
        }
        if (tls)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var req = new CertificateRequest($"CN={Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}", key, HashAlgorithmName.SHA256);
            using var made = req.CreateSelfSigned(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
            // Schannel needs the key in a key container, not an ephemeral one.
            hostCert = X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.UserKeySet);
            hostFp = Spki.Fingerprint(hostCert.RawData)!;
            code = NewCode();
            Log($"TLS door, Mac ID {MacID.Make(hostFp)}; pairing {(requirePairing ? "required" : "open")}; code {code[..4]} {code[4..8]} {code[8..]}");
        }
        var listener = new TcpListener(any ? IPAddress.Any : IPAddress.Loopback, port);
        listener.Start();
        Log($"listening on {listener.LocalEndpoint} ({(tls ? "TLS" : "plain")}), stream {width}x{height} at {fps} fps");
        while (true)
        {
            var c = listener.AcceptTcpClient();
            c.NoDelay = true;
            _ = Task.Run(() => Serve(c));
        }
    }

    static string NewCode()
    {
        var digits = new List<int>();
        while (digits.Count < 11) digits.Add(RandomNumberGenerator.GetInt32(10));
        for (int check = 0; check < 10; check++)
        {
            var candidate = string.Concat(digits.Select(d => (char)('0' + d))) + (char)('0' + check);
            if (PairingCode.Check(candidate).code != null) return candidate;
        }
        throw new InvalidOperationException();
    }

    static async Task Serve(TcpClient tcp)
    {
        var who = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
        Stream stream = tcp.GetStream();
        byte[]? deviceFp = null;
        string alpn = "sill/1";
        try
        {
            if (tls)
            {
                var ssl = new SslStream(stream, false, (_, cert, _, _) =>
                {
                    deviceFp = cert == null ? null : Spki.Fingerprint(cert.GetRawCertData());
                    return deviceFp != null;
                });
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = hostCert, ClientCertificateRequired = true, EnabledSslProtocols = SslProtocols.Tls13,
                    ApplicationProtocols = new() { new("sill/1"), new("sill-pair/1") },
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                });
                alpn = ssl.NegotiatedApplicationProtocol.ToString();
                stream = ssl;
                Log($"{who}: TLS {ssl.SslProtocol} {ssl.NegotiatedCipherSuite}, ALPN {alpn}, device {(deviceFp == null ? "?" : Base64Url.Encode(deviceFp)[..10])}");
            }
            if (alpn == "sill-pair/1") { await Pair(stream, deviceFp!, who); return; }
            if (requirePairing && !paired.Contains(Base64Url.Encode(deviceFp!)))
            {
                Log($"{who}: session refused: this device is not paired");
                return;
            }
            await new HostSession(stream, who).Run();
        }
        catch (Exception e) when (e is IOException or AuthenticationException or ObjectDisposedException or SocketException or OperationCanceledException)
        {
            Log($"{who}: ended: {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            stream.Dispose();
            tcp.Dispose();
        }
    }

    static async Task<(Header h, byte[] payload)> Read(Stream s, CancellationToken ct = default)
    {
        var header = new byte[Header.Length];
        await s.ReadExactlyAsync(header, ct);
        var h = Header.Parse(header);
        if (h.PayloadLength > Wire.MaxClientPayload) throw new IOException($"payload of {h.PayloadLength} bytes");
        var payload = new byte[h.PayloadLength];
        await s.ReadExactlyAsync(payload, ct);
        return (h, payload);
    }

    static async Task Pair(Stream s, byte[] deviceFp, string who)
    {
        using var cts = new CancellationTokenSource(10_000);
        var (h, payload) = await Read(s, cts.Token);
        if (h.Kind != (byte)Kind.PairRequest) { Log($"{who}: pairing connection sent kind {h.Kind}"); return; }
        var req = Json.Decode<PairRequest>(payload)!;
        Log($"{who}: pair request {Encoding.UTF8.GetString(payload)}");
        PairResult answer;
        if (req.V != 1) answer = new PairResult { Ok = false, Reason = "closed" };
        else if (req.Method == "ask") { answer = new PairResult { Ok = false, Reason = "shown" }; Log($"ASK from {req.Name}: the code is {code[..4]} {code[4..8]} {code[8..]}"); }
        else if (req.Method == "cancel") { answer = new PairResult { Ok = false, Reason = "closed" }; Log($"{req.Name} cancelled"); }
        else if (req.Method == "code")
        {
            var key = PairingProof.CodeKey(code, hostFp);
            var want = PairingProof.DeviceProof(key, deviceFp, hostFp);
            var got = Base64Url.Decode(req.Proof);
            if (got != null && CryptographicOperations.FixedTimeEquals(got, want))
            {
                paired.Add(Base64Url.Encode(deviceFp));
                answer = new PairResult
                {
                    Ok = true, Proof = Base64Url.Encode(PairingProof.MacProof(key, hostFp, deviceFp)), MacID = MacID.Make(hostFp),
                    Name = "Test Mac", RecognitionKey = Base64Url.Encode(recognitionKey),
                };
                Log($"PAIRED {req.Name} ({Base64Url.Encode(deviceFp)[..10]})");
                code = NewCode();
            }
            else
            {
                tries--;
                answer = tries > 0 ? new PairResult { Ok = false, Reason = "code", TriesLeft = tries } : new PairResult { Ok = false, Reason = "stopped" };
                Log($"wrong code from {req.Name}; {tries} tries left");
            }
        }
        else answer = new PairResult { Ok = false, Reason = "closed" };
        var m = Wire.Message(Kind.PairResult, Json.Encode(answer));
        await s.WriteAsync(m);
        await s.FlushAsync();
    }

    public static int Width => width;
    public static int Height => height;
    public static int Fps => fps;
    public static int Bitrate { get => bitrate; set => bitrate = value; }
    public static void Say(string s) => Log(s);
}

/// <summary>One session: the catalog, the settings, the stream, and what the device sends.</summary>
sealed class HostSession
{
    readonly Stream stream;
    readonly string who;
    readonly SemaphoreSlim writeGate = new(1, 1);
    readonly CancellationTokenSource stop = new();
    Source active = Source.None;
    volatile bool streaming;
    double px = 0.5, py = 0.5;
    volatile bool leftDown, rightDown;
    int keys;
    int inputs;
    long lastStatsLog;
    double captureScale = 2, maxFps = 60;
    Task? streamTask;

    static readonly WindowInfo[] Windows =
    {
        new() { Id = 101, Title = "Readme.md", AppName = "TextEdit", BundleID = "com.apple.TextEdit", Width = 800, Height = 500 },
        new() { Id = 102, Title = "Downloads", AppName = "Finder", BundleID = "com.apple.finder", Width = 900, Height = 560 },
    };

    public HostSession(Stream stream, string who) { this.stream = stream; this.who = who; }

    async Task Send(Kind kind, byte[] payload, bool key = false)
    {
        var m = Wire.Message(kind, payload, key);
        await writeGate.WaitAsync();
        try { await stream.WriteAsync(m, stop.Token); await stream.FlushAsync(stop.Token); }
        finally { writeGate.Release(); }
    }

    byte[] ListJson() => Json.Encode(new JsonObject
    {
        ["macName"] = "Test Mac",
        ["windows"] = JsonSerializer.SerializeToNode(Windows, Json.Options),
        ["active"] = active.ToJson(),
        ["launchID"] = "test-launch",
        ["hostVersion"] = "0.5.1",
        ["protocol"] = 1,
        ["gestures"] = 1,
        ["clipboard"] = 1,
    });

    byte[] SettingsJson(int? answering) => Json.Encode(new HostSettingsState
    {
        Settings = new StreamSettings { MaxFPS = (int)maxFps, Bitrate = Program.Bitrate, CaptureScale = captureScale, PrioritizeSpeed = false, VirtualDisplay = false, DirectWireless = false },
        Persistent = false, VirtualDisplayAvailable = false, SoftwareEncoder = false,
        Stream = streaming ? new RunningStream { Width = Program.Width, Height = Program.Height, Fps = Program.Fps, Mbps = Program.Bitrate / 1_000_000 } : null,
        Answering = answering,
    });

    public async Task Run()
    {
        // The hello first (a device from 2026-09-25 on).
        using (var first = new CancellationTokenSource(2000))
        {
            try
            {
                var (h, p) = await ReadMessage(first.Token);
                if (h.Kind == (byte)Kind.Hello) Program.Say($"{who}: hello {Encoding.UTF8.GetString(p)}");
                else Handle(h, p);
            }
            catch (OperationCanceledException) { Program.Say($"{who}: no hello in 2 s"); }
        }
        await Send(Kind.WindowList, ListJson());
        await Send(Kind.HostSettings, SettingsJson(null));
        await Send(Kind.AppList, Json.Encode(new List<AppInfo> { new() { Name = "TextEdit", BundleID = "com.apple.TextEdit" }, new() { Name = "Finder", BundleID = "com.apple.finder" }, new() { Name = "Safari", BundleID = "com.apple.Safari" } }));
        foreach (var w in Windows) await Send(Kind.Thumbnail, Blobs.Thumbnail(w.Id, Pictures.Jpeg(160, 100, w.Id == 101 ? Color.SteelBlue : Color.DarkOrange)));
        await Send(Kind.CursorShape, Pictures.CursorBlob());
        try
        {
            while (true)
            {
                var (h, p) = await ReadMessage(stop.Token);
                Handle(h, p);
            }
        }
        finally
        {
            stop.Cancel();
            streaming = false;
            Program.Say($"{who}: left after {inputs} input messages");
        }
    }

    async Task<(Header, byte[])> ReadMessage(CancellationToken ct)
    {
        var header = new byte[Header.Length];
        await stream.ReadExactlyAsync(header, ct);
        var h = Header.Parse(header);
        if (h.PayloadLength > Wire.MaxClientPayload) throw new IOException("too large");
        var payload = new byte[h.PayloadLength];
        await stream.ReadExactlyAsync(payload, ct);
        return (h, payload);
    }

    void Handle(Header h, byte[] p)
    {
        string text = Encoding.UTF8.GetString(p);
        switch ((Kind)h.Kind)
        {
            case Kind.Ping:
                _ = Send(Kind.Pong, p);
                break;
            case Kind.SelectSource:
                using (var doc = JsonDocument.Parse(p)) active = Source.Parse(doc.RootElement);
                Program.Say($"{who}: select {text} → {active}");
                StartOrStop();
                _ = Send(Kind.WindowList, ListJson());
                break;
            case Kind.Input:
                inputs++;
                using (var doc = JsonDocument.Parse(p))
                {
                    var r = doc.RootElement;
                    if (r.TryGetProperty("pointer", out var ptr))
                    {
                        px = ptr.GetProperty("x").GetDouble(); py = ptr.GetProperty("y").GetDouble();
                        var a = ptr.GetProperty("_0").GetString();
                        if (a == "leftDown") leftDown = true; else if (a == "leftUp") leftDown = false;
                        else if (a == "rightDown") rightDown = true; else if (a == "rightUp") rightDown = false;
                        if (a != "move") Program.Say($"{who}: input {text}");
                    }
                    else
                    {
                        if (r.TryGetProperty("key", out var k) && k.GetProperty("down").GetBoolean()) keys++;
                        Program.Say($"{who}: input {text}");
                    }
                }
                break;
            case Kind.Viewport:
            case Kind.LaunchApp:
            case Kind.WindowCommand:
            case Kind.Hello:
            case Kind.Gesture:
                Program.Say($"{who}: kind {h.Kind} {text}");
                if ((Kind)h.Kind == Kind.LaunchApp) { active = Source.Window(101); StartOrStop(); _ = Send(Kind.WindowList, ListJson()); }
                break;
            case Kind.Clipboard:
                Program.Say($"{who}: clipboard {text}");
                if (Json.Decode<ClipboardPayload>(p)?.Text is { } t) _ = Send(Kind.Clipboard, Json.Encode(new ClipboardPayload { Text = "Mac got: " + t }));
                break;
            case Kind.ChangeSettings:
                Program.Say($"{who}: settings change {text}");
                var change = Json.Decode<HostSettingsChange>(p);
                if (change?.Bitrate is long b) Program.Bitrate = (int)b;
                if (change?.CaptureScale is double cs) captureScale = cs;
                if (change?.MaxFPS is int mf) maxFps = mf;
                _ = Send(Kind.HostSettings, SettingsJson(change?.Token));
                break;
            case Kind.ClientStats:
                if (Environment.TickCount64 - lastStatsLog > 5000) { lastStatsLog = Environment.TickCount64; Program.Say($"{who}: stats {text}"); }
                break;
            default:
                Program.Say($"{who}: kind {h.Kind}, {p.Length} bytes");
                break;
        }
    }

    void StartOrStop()
    {
        if (active.Kind == SourceKind.None) { streaming = false; return; }
        if (streamTask is { IsCompleted: false }) return;
        streaming = true;
        streamTask = Task.Run(async () =>
        {
            try { await StreamLoop(); }
            catch (Exception e) { Program.Say($"{who}: stream stopped: {e.GetType().Name}: {e.Message}"); streaming = false; }
        });
    }

    async Task StreamLoop()
    {
        int w = Program.Width, h = Program.Height, rate = Program.Fps;
        using var encoder = new HevcEncoder(w, h, rate, Program.Bitrate);
        Program.Say($"{who}: streaming with {encoder.Name}");
        _ = Send(Kind.HostSettings, SettingsJson(null));
        var nv12 = new byte[w * h * 3 / 2];
        ParameterSets? sent = null;
        var clock = Stopwatch.StartNew();
        long frame = 0;
        int lastKeys = -1;
        while (streaming && !stop.IsCancellationRequested)
        {
            Pictures.Draw(nv12, w, h, frame, px, py, leftDown, rightDown, keys, active.Kind == SourceKind.Desktop);
            foreach (var au in encoder.Encode(nv12, rate))
            {
                var nals = HevcEncoder.Nals(au);
                var sets = nals.Where(n => n.Length > 2 && ((n[0] >> 1) & 0x3F) is 32 or 33 or 34).ToList();
                if (sets.Count == 3)
                {
                    var ps = new ParameterSets(4, sets.OrderBy(n => (n[0] >> 1) & 0x3F).ToList());
                    if (!ps.SameAs(sent)) { await Send(Kind.ParameterSets, ps.Encode()); sent = ps; }
                }
                var vcl = nals.Where(n => n.Length > 2 && ((n[0] >> 1) & 0x3F) is not (32 or 33 or 34)).ToList();
                if (vcl.Count == 0) continue;
                bool key = vcl.Any(n => ((n[0] >> 1) & 0x3F) is >= 16 and <= 21);
                var ms = new MemoryStream();
                foreach (var n in vcl) { ms.WriteByte((byte)(n.Length >> 24)); ms.WriteByte((byte)(n.Length >> 16)); ms.WriteByte((byte)(n.Length >> 8)); ms.WriteByte((byte)n.Length); ms.Write(n); }
                if (sent != null) await Send(Kind.Frame, ms.ToArray(), key);
            }
            if (keys != lastKeys) lastKeys = keys;
            frame++;
            var due = TimeSpan.FromSeconds(frame / (double)rate) - clock.Elapsed;
            if (due > TimeSpan.Zero) await Task.Delay(due);
        }
    }
}

static class Pictures
{
    /// <summary>A pattern that shows the device's input: a moving bar, fine lines (sharpness), a cross
    /// at the pointer, a disc while the left button is down, a square whose colour counts key presses.</summary>
    public static void Draw(byte[] nv12, int w, int h, long frame, double px, double py, bool left, bool right, int keys, bool desktop)
    {
        var y = nv12.AsSpan(0, w * h);
        var uv = nv12.AsSpan(w * h);
        for (int r = 0; r < h; r++)
        {
            var row = y.Slice(r * w, w);
            byte bg = (byte)(desktop ? 40 + r * 60 / h : 200 - r * 60 / h);
            row.Fill(bg);
        }
        uv.Fill(128);
        // Fine lines: 1 px black and white, top-left 200x100.
        for (int r = 20; r < 120; r++) for (int c = 20; c < 220; c++) y[r * w + c] = (byte)(c % 2 == 0 ? 0 : 255);
        // The bar moves 8 px a frame.
        int bx = (int)(frame * 8 % w);
        for (int r = h - 120; r < h - 40; r++) y.Slice(r * w + Math.Min(bx, w - 40), 40).Fill(235);
        // Key presses: a square whose chroma turns with each.
        int sq = 120, sx = w - sq - 40, sy = 40;
        byte cu = (byte)(128 + 100 * Math.Cos(keys * 0.9)), cv = (byte)(128 + 100 * Math.Sin(keys * 0.9));
        for (int r = sy; r < sy + sq; r++) y.Slice(r * w + sx, sq).Fill(140);
        for (int r = sy / 2; r < (sy + sq) / 2; r++) for (int c = sx / 2; c < (sx + sq) / 2; c++) { uv[r * w + c * 2] = cu; uv[r * w + c * 2 + 1] = cv; }
        // The pointer.
        int cx = (int)Math.Clamp(px * w, 0, w - 1), cy = (int)Math.Clamp(py * h, 0, h - 1);
        if (left || right)
        {
            int rad = 26;
            for (int r = -rad; r <= rad; r++) for (int c = -rad; c <= rad; c++)
            {
                if (r * r + c * c > rad * rad) continue;
                int yy = cy + r, xx = cx + c;
                if (yy < 0 || yy >= h || xx < 0 || xx >= w) continue;
                y[yy * w + xx] = 90;
                uv[(yy / 2) * w + (xx / 2) * 2] = left ? (byte)90 : (byte)200;
                uv[(yy / 2) * w + (xx / 2) * 2 + 1] = left ? (byte)200 : (byte)90;
            }
        }
        for (int d = -30; d <= 30; d++)
        {
            if (cx + d >= 0 && cx + d < w) { y[cy * w + cx + d] = 0; if (cy + 1 < h) y[(cy + 1) * w + cx + d] = 255; }
            if (cy + d >= 0 && cy + d < h) { y[(cy + d) * w + cx] = 0; if (cx + 1 < w) y[(cy + d) * w + cx + 1] = 255; }
        }
    }

    public static byte[] Jpeg(int w, int h, Color color)
    {
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) { g.Clear(color); g.FillRectangle(Brushes.White, 10, 10, w - 20, 18); }
        var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
        return ms.ToArray();
    }

    /// <summary>An arrow, 2x, as the Mac's cursor shape: hot spot at its tip.</summary>
    public static byte[] CursorBlob()
    {
        using var bmp = new Bitmap(34, 50, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var pts = new[] { new PointF(2, 2), new PointF(2, 40), new PointF(12, 31), new PointF(19, 46), new PointF(25, 43), new PointF(18, 29), new PointF(31, 29) };
            g.FillPolygon(Brushes.Black, pts);
            using var pen = new Pen(Color.White, 2.5f);
            g.DrawPolygon(pen, pts);
        }
        var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        var png = ms.ToArray();
        var b = new byte[16 + png.Length];
        void F(int i, float v) => System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(b.AsSpan(i * 4), v);
        F(0, 1); F(1, 1); F(2, 17); F(3, 25);
        png.CopyTo(b, 16);
        return b;
    }
}
