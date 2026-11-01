using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Sill.Windows.Protocol;

namespace Sill.TestHost;

// TEST ONLY: what a Mac does with a device's files and richer clipboard (M4, docs/coherence-protocol.md),
// enough to try the Windows client's flows on a PC without a Mac: the bulk connection's ticket and its
// admission, kind 34 parts saved and printed, kinds 35 and 38 printed, kind 36 offers of generated files
// (F6, then a drag in the picture), kind 37 answered with their parts, kind 32's generations 2 and 3
// echoed back, and offered by key (F7 files, F8 a picture with HTML and RTF, F9 a concealed text).
sealed partial class HostSession
{
    static readonly ConcurrentDictionary<string, HostSession> tickets = new();
    static readonly string Inbox = Path.Combine(Path.GetTempPath(), "SillTestHost");
    const ushort F6 = 0x3F, F7 = 0x40, F8 = 0x41, F9 = 0x42;

    sealed class Incoming { public required string Path; public required FileStream File; public required string Name; public ulong Total, Written; public required string Via; }
    sealed record OfferFile(string Name, bool Folder, Func<byte[]> Bytes);

    string? ticket;
    volatile Stream? data;
    readonly SemaphoreSlim dataGate = new(1, 1);
    readonly object files = new();
    readonly Dictionary<uint, Incoming> incoming = new();
    readonly Dictionary<uint, string> received = new();
    readonly Dictionary<uint, List<OfferFile>> offers = new();
    uint nextOffer = (uint)RandomNumberGenerator.GetInt32(1000, 1_000_000);
    int accepts;
    bool dragArmed, dragPress, dragOffered;
    double downX, downY;

    // MARK: the bulk connection

    async Task OfferDataLink()
    {
        ticket = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        tickets[ticket] = this;
        await Send(Kind.DataTicket, Json.Encode(new DataTicket { Token = ticket }));
        Program.Say($"{who}: sent a data ticket (kind 48)");
    }

    /// <summary>A connection whose first message was kind 49: admitted for the session holding the
    /// ticket, with the same key, one per session, closed with it.</summary>
    public static async Task ServeData(Stream s, string who, byte[]? key, Header h, byte[] payload)
    {
        if (h.Kind != (byte)Kind.DataHello) { Program.Say($"{who}: a sill-data/1 connection whose first message was kind {h.Kind}: closed"); return; }
        var hello = Json.Decode<DataHello>(payload);
        if (hello == null || !tickets.TryGetValue(hello.Token, out var session)) { Program.Say($"{who}: bulk connection refused: no session holds that ticket"); return; }
        if (!(session.deviceKey ?? Array.Empty<byte>()).AsSpan().SequenceEqual(key ?? Array.Empty<byte>())) { Program.Say($"{who}: bulk connection refused: another key than its session's"); return; }
        if (session.data != null) { Program.Say($"{who}: bulk connection refused: its session has one"); return; }
        session.data = s;
        Program.Say($"{who}: bulk connection admitted for {session.who}");
        try
        {
            while (!session.stop.IsCancellationRequested)
            {
                var head = new byte[Header.Length];
                await s.ReadExactlyAsync(head, session.stop.Token);
                var hh = Header.Parse(head);
                if (hh.PayloadLength > Wire.MaxDataPayload) throw new IOException($"a message of {hh.PayloadLength} bytes on the bulk connection");
                var p = new byte[hh.PayloadLength];
                await s.ReadExactlyAsync(p, session.stop.Token);
                session.OnFiles(hh, p, "bulk");
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            session.data = null;
            Program.Say($"{who}: bulk connection closed");
        }
    }

    void EndData()
    {
        if (ticket != null) tickets.TryRemove(ticket, out _);
        try { data?.Dispose(); } catch (IOException) { }
        lock (files) foreach (var f in incoming.Values) f.File.Dispose();
    }

    /// <summary>A part (or anything) on the bulk connection when it is up, else on the session.</summary>
    async Task SendBulk(Kind kind, byte[] payload)
    {
        var d = data;
        if (d == null) { await Send(kind, payload); return; }
        var m = Wire.Message(kind, payload);
        await dataGate.WaitAsync();
        try { await d.WriteAsync(m, stop.Token); await d.FlushAsync(stop.Token); }
        finally { dataGate.Release(); }
    }

    // MARK: the device's files

    void OnFiles(Header h, byte[] p, string via)
    {
        switch ((Kind)h.Kind)
        {
            case Kind.FileChunk: OnPart(p, via); break;
            case Kind.FileDrop:
                if (Json.Decode<FileDrop>(p) is { } drop)
                    Program.Say($"{who}: DROP {Names(drop.Files)} at {drop.X:0.000},{drop.Y:0.000}{(drop.Window is { } w ? $" of window {w}" : "")}{(drop.Folders is { Count: > 0 } fo ? $", folders {string.Join(",", fo)}" : "")}");
                break;
            case Kind.LiveDrag:
                if (Json.Decode<LiveDrag>(p) is { } live)
                {
                    if (live.Phase == LiveDrag.Phases.Move) { if (Environment.TickCount64 % 10 == 0) Program.Say($"{who}: live drag {live.Id} move {live.X:0.000},{live.Y:0.000} modifiers {live.Modifiers ?? 0}"); }
                    else Program.Say($"{who}: LIVE DRAG {live.Id} {live.Phase} at {live.X:0.000},{live.Y:0.000}{(live.Window is { } w ? $" of window {w}" : "")}{(live.Files is { } f ? $", files {Names(f)}" : "")}{(live.Folders is { Count: > 0 } fo ? $", folders {string.Join(",", fo)}" : "")}");
                }
                break;
            case Kind.FileRequest:
                if (Json.Decode<FileRequest>(p) is { } r) _ = Task.Run(() => Answer(r));
                break;
        }
    }

    string Names(IEnumerable<uint> ids)
    {
        lock (files) return string.Join(", ", ids.Select(id => received.TryGetValue(id, out var path) ? $"{id} ({Path.GetFileName(path)})" : $"{id} (not here whole)"));
    }

    void OnPart(byte[] payload, string via)
    {
        if (FileChunk.Unpack(payload) is not { } part) { Program.Say($"{who}: a kind 34 that does not read"); return; }
        lock (files)
        {
            if (part.Offset == 0)
            {
                if (incoming.Remove(part.Id, out var old)) old.File.Dispose();
                Directory.CreateDirectory(Inbox);
                var safe = new string(part.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
                var path = Path.Combine(Inbox, $"{part.Id}-{safe}");
                incoming[part.Id] = new Incoming { Path = path, File = File.Create(path), Name = part.Name, Total = part.Total, Via = via };
                Program.Say($"{who}: receiving \"{part.Name}\" ({part.Total:N0} bytes) as transfer {part.Id} on the {via}");
            }
            if (!incoming.TryGetValue(part.Id, out var f)) { Program.Say($"{who}: a part of transfer {part.Id}, which never began here"); return; }
            if (part.Offset != f.Written || part.Total != f.Total) { Program.Say($"{who}: transfer {part.Id}: a part at {part.Offset:N0} where {f.Written:N0} was due (dropped, as a Mac does)"); return; }
            if (f.Via != via) Program.Say($"{who}: transfer {part.Id}: a part on the {via}, begun on the {f.Via} (a Mac keys parts by connection)");
            f.File.Write(part.Bytes);
            f.Written += (ulong)part.Bytes.Length;
            if (f.Written < f.Total) return;
            f.File.Dispose();
            incoming.Remove(part.Id);
            received[part.Id] = f.Path;
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Path)))[..16].ToLowerInvariant();
            Program.Say($"{who}: RECEIVED \"{f.Name}\", {f.Total:N0} bytes, sha256 {hash}…, transfer {part.Id} → {f.Path}");
        }
    }

    /// <summary>Kind 37: one offered file back as kind 34 parts (512 KB) under the device's transfer ID.</summary>
    async Task Answer(FileRequest r)
    {
        OfferFile? file;
        lock (files) file = offers.TryGetValue(r.Offer, out var list) && r.Index >= 0 && r.Index < list.Count ? list[r.Index] : null;
        if (file == null) { Program.Say($"{who}: kind 37 for offer {r.Offer} file {r.Index}, which this host never offered"); return; }
        if (r.Trash == true) { Program.Say($"{who}: \"{file.Name}\" would go to the Trash"); return; }
        var bytes = file.Bytes();
        Program.Say($"{who}: sending \"{file.Name}\" ({bytes.Length:N0} bytes{(file.Folder ? ", a folder zipped" : "")}) as transfer {r.Transfer} on the {(data != null ? "bulk connection" : "session")}");
        try
        {
            ulong offset = 0;
            do
            {
                int n = (int)Math.Min(FileChunk.MaxBytes, (ulong)bytes.Length - offset);
                await SendBulk(Kind.FileChunk, FileChunk.Pack(r.Transfer, file.Name, (ulong)bytes.Length, offset, bytes.AsSpan((int)offset, n)));
                offset += (ulong)n;
            } while (offset < (ulong)bytes.Length);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { Program.Say($"{who}: sending \"{file.Name}\" stopped: {e.Message}"); }
    }

    // MARK: generated files, drags out and keys

    static List<OfferFile> Generated() => new()
    {
        new("Test from Mac.txt", false, () => Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 60_000).Select(i => $"Line {i:D6} of a file from the test Mac. Grüße!\n")))),
        new("Notes: 2026?.txt", false, () => Encoding.UTF8.GetBytes("A name with a colon and a question mark, which Windows maps.\n")),
        new("Test folder", true, ZipFolder),
    };

    /// <summary>A folder as the Mac sends one (ditto's --keepParent: the folder at the zip's top, and the
    /// resource-fork files under __MACOSX that a receiver leaves out).</summary>
    static byte[] ZipFolder()
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            void Add(string name, byte[]? content)
            {
                var e = z.CreateEntry(name);
                if (content == null) return;
                using var s = e.Open();
                s.Write(content);
            }
            Add("Test folder/", null);
            Add("Test folder/readme.txt", Encoding.UTF8.GetBytes("Unpacked from the test Mac's zip.\n"));
            Add("Test folder/Sub folder/", null);
            Add("Test folder/Sub folder/data.bin", Enumerable.Range(0, 200_000).Select(i => (byte)(i * 31)).ToArray());
            Add("__MACOSX/Test folder/._readme.txt", new byte[] { 0, 5, 22, 7 });
        }
        return ms.ToArray();
    }

    uint Offer(List<OfferFile> list)
    {
        lock (files)
        {
            uint id = nextOffer++;
            offers[id] = list;
            return id;
        }
    }

    static List<OfferedFile> Describe(List<OfferFile> list) =>
        list.Select(f => new OfferedFile { Name = f.Name, Size = f.Folder ? 0 : (ulong)f.Bytes().Length, Folder = f.Folder ? true : null }).ToList();

    void OnPointerForDrag(string action, double x, double y)
    {
        switch (action)
        {
            case "leftDown":
                dragPress = true; dragOffered = false; downX = x; downY = y;
                break;
            case "move" when dragPress && dragArmed && !dragOffered && Math.Abs(x - downX) + Math.Abs(y - downY) > 0.02:
                // An app on the Mac started dragging files under the device's held button (#66's DragOut).
                var list = Generated();
                uint id = Offer(list);
                dragOffered = true; dragArmed = false;
                _ = Send(Kind.DragOffer, Json.Encode(new DragOffer { Id = id, Files = Describe(list) }));
                Program.Say($"{who}: DRAG OFFER {id}: {string.Join(", ", list.Select(f => f.Name))} (drag out of the window to drop them on the PC)");
                break;
            case "leftUp":
                if (dragOffered)
                {
                    _ = Send(Kind.DragOffer, Json.Encode(new DragOffer { Id = 0 }));   // the drag ended here
                    Program.Say($"{who}: the drag ended on the Mac (offer id 0)");
                }
                dragPress = false; dragOffered = false;
                break;
        }
    }

    void OnKey(int usage)
    {
        switch (usage)
        {
            case F6:
                dragArmed = true;
                Program.Say($"{who}: F6: press in the picture and drag: a file drag offer comes, then drag out of the window");
                break;
            case F7:
                if (accepts < Capabilities.ClipboardFiles) { Program.Say($"{who}: F7: the client's subscription takes generation {accepts}, not files"); break; }
                var list = Generated();
                uint id = Offer(list);
                _ = Send(Kind.Clipboard, Json.Encode(new ClipboardPayload { Text = "Test from Mac.txt\nNotes: 2026?.txt\nTest folder", Files = new ClipboardFiles { Offer = id, Files = Describe(list) } }));
                Program.Say($"{who}: F7: files on the client's clipboard (offer {id}); paste them in Explorer");
                break;
            case F8:
                if (accepts < Capabilities.ClipboardRich) { Program.Say($"{who}: F8: the client's subscription takes generation {accepts}, not pictures"); break; }
                _ = Send(Kind.Clipboard, Json.Encode(new ClipboardPayload
                {
                    Text = "Hello from the test Mac, Grüße 😀",
                    Html = "<p>Hello from the <b>test</b> Mac, <i>Grüße</i> 😀</p>",
                    Rtf = Encoding.ASCII.GetBytes(@"{\rtf1\ansi{\fonttbl{\f0 Helvetica;}}\f0\fs24 Hello from the {\b test} Mac.}"),
                    Image = Picture(),
                }));
                Program.Say($"{who}: F8: a picture with HTML, RTF and text on the client's clipboard");
                break;
            case F9:
                _ = Send(Kind.Clipboard, Json.Encode(new ClipboardPayload { Text = "hunter2 (a concealed copy)", Concealed = true }));
                Program.Say($"{who}: F9: a concealed text (kept out of Windows' clipboard history)");
                break;
        }
    }

    static byte[] Picture()
    {
        using var bmp = new Bitmap(320, 200, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 320, 200), Color.FromArgb(255, 30, 120, 220), Color.FromArgb(40, 220, 60, 30), 30f);
            g.FillEllipse(brush, 10, 10, 300, 180);
            g.DrawString("Test Mac", new Font("Segoe UI", 28, FontStyle.Bold), Brushes.White, 70, 75);
        }
        var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    // MARK: the clipboard

    void OnClipboard(byte[] p)
    {
        var text = Encoding.UTF8.GetString(p);
        var clip = Json.Decode<ClipboardPayload>(p);
        if (clip == null) { Program.Say($"{who}: clipboard that does not decode"); return; }
        if (clip.Stop == true) { accepts = 0; Program.Say($"{who}: clipboard: stop"); return; }
        bool bare = clip.Text == null && clip.Image == null && clip.Html == null && clip.Rtf == null && clip.Files == null;
        // A subscription says what it takes ({} text alone); a later message without `accepts` keeps it.
        if (bare) accepts = Math.Min(clip.Accepts ?? 1, Program.ClipboardGeneration);
        else if (clip.Accepts is int a) accepts = Math.Min(a, Program.ClipboardGeneration);
        if (bare) { Program.Say($"{who}: clipboard subscription {text} (generation {accepts})"); return; }
        var parts = new List<string>();
        if (clip.Text != null) parts.Add($"text \"{(clip.Text.Length > 60 ? clip.Text[..60] + "…" : clip.Text)}\"");
        if (clip.Html != null) parts.Add($"html {clip.Html.Length:N0} characters");
        if (clip.Rtf != null) parts.Add($"rtf {clip.Rtf.Length:N0} bytes");
        if (clip.Image != null) parts.Add($"image {clip.Image.Length:N0} bytes{(clip.Image.Length > 24 && clip.Image[1] == 'P' ? $" ({System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(clip.Image.AsSpan(16))}×{System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(clip.Image.AsSpan(20))})" : "")}");
        if (clip.Files is { } fl) parts.Add($"files {Names(fl.Transfers ?? new())}");
        Program.Say($"{who}: CLIPBOARD from the PC: {string.Join(", ", parts)}");
        // The echo, as another copy on the Mac: only what the subscription takes.
        var echo = new ClipboardPayload { Text = clip.Text != null ? "Mac got: " + clip.Text : null };
        if (accepts >= Capabilities.ClipboardRich)
        {
            echo.Html = clip.Html != null ? "<p><b>Mac got:</b></p>" + clip.Html : null;
            echo.Rtf = clip.Rtf;
            echo.Image = clip.Image;
        }
        if (accepts >= Capabilities.ClipboardFiles && clip.Files?.Transfers is { Count: > 0 } ids)
        {
            List<OfferFile> back;
            lock (files) back = ids.Where(received.ContainsKey).Select(id => received[id]).Select(path =>
                new OfferFile(Path.GetFileName(path)[(Path.GetFileName(path).IndexOf('-') + 1)..], false, () => File.ReadAllBytes(path))).ToList();
            if (back.Count > 0) echo.Files = new ClipboardFiles { Offer = Offer(back), Files = Describe(back) };
        }
        if (echo.Text == null && echo.Html == null && echo.Rtf == null && echo.Image == null && echo.Files == null) return;
        var payload = Json.Encode(echo);
        if (payload.Length > Wire.MaxOtherHostPayload) { Program.Say($"{who}: the echo would be {payload.Length:N0} bytes: not sent"); return; }
        _ = Send(Kind.Clipboard, payload);
    }
}
