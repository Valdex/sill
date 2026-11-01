using System.IO.Compression;
using System.Text;
using Sill.Windows.Data;
using Sill.Windows.Protocol;

namespace Sill.Checks;

// Files over the wire on this side: transfer IDs, a file cut into kind 34 parts and put together again
// (order, duplicates, caps, cancel), the bulk connection's ticket, hello and routing, upload pacing on
// a simulated clock, Windows names, the shell's formats, and folders zipped and unpacked for real.
static class DataChecks
{
    public static void Parts()
    {
        // IDs: random start, never 0, never one in use.
        var ids = new TransferIds(uint.MaxValue - 1);
        uint a = ids.Take(), b = ids.Take(), c = ids.Take();
        Check.That(a == uint.MaxValue - 1 && b == uint.MaxValue && c == 1, $"IDs wrap past the top and skip 0 ({a}, {b}, {c})");
        ids.Release(a);
        var again = new TransferIds(5);
        uint x = again.Take(); again.Take();
        var wrap = new TransferIds(5);
        Check.That(x == 5 && wrap.Take() == 5, "a fresh source starts where it is told");
        var many = TransferIds.Random();
        var seen = new HashSet<uint>();
        for (int i = 0; i < 10_000; i++) seen.Add(many.Take());
        Check.That(seen.Count == 10_000 && !seen.Contains(0), "10,000 IDs from a random start: all different, none 0");
        Check.That(TransferIds.Random().Take() != TransferIds.Random().Take() || TransferIds.Random().Take() != TransferIds.Random().Take(),
                   "two PCs start at different IDs (the host keys files by ID across devices)");

        // Splitting.
        var split = FileParts.Split(1_200_000, FileParts.DataPartBytes).ToList();
        Check.That(split.Count == 3 && split[0] == (0UL, 524288) && split[1] == (524288UL, 524288) && split[2] == (1048576UL, 151424),
                   "1.2 MB in 512 KB parts: two whole and the rest, at their offsets");
        Check.That(FileParts.Split(0, FileParts.SessionPartBytes).SequenceEqual(new[] { (0UL, 0) }), "an empty file is one empty part");
        Check.That(FileParts.Split(65536, FileParts.SessionPartBytes).Count() == 1 && FileParts.PartCount(65537, FileParts.SessionPartBytes) == 2, "64 KB is one session part, a byte more two");
        Check.That(Throws(() => FileParts.Split(10, FileChunk.MaxBytes + 1).ToList()), "a part over 512 KB is refused");
        Check.That(Wire.Fits(FileChunk.Pack(1, new string('n', 400), 1, 0, new byte[FileParts.DataPartBytes]).Length, Wire.MaxDataPayload) &&
                   Wire.Fits(FileChunk.Pack(1, new string('n', 400), 1, 0, new byte[FileParts.DataPartBytes]).Length, Wire.MaxClientPayload),
                   "a 512 KB part with a long name fits both connections' caps");

        // Reassembly: in order, out of order, duplicates, gaps, a size that changes, past the end.
        var r = new Reassembly(7, "a.bin", 100);
        Check.Equal(Reassembly.Verdict.Write, r.Add(0, 40, 100), "the first part: written");
        Check.Equal(40UL, r.Contiguous, "40 bytes readable");
        Check.Equal(Reassembly.Verdict.Write, r.Add(60, 40, 100), "a later part early: written at its place");
        Check.That(r.Contiguous == 40 && !r.Complete && r.Received == 80, "a gap: still 40 readable, 80 received, not whole");
        Check.Equal(Reassembly.Verdict.Duplicate, r.Add(0, 40, 100), "the first part again: nothing to write");
        Check.Equal(Reassembly.Verdict.Duplicate, r.Add(10, 20, 100), "a part inside one already here: a duplicate");
        Check.Equal(Reassembly.Verdict.Write, r.Add(30, 40, 100), "an overlap that fills the gap: written");
        Check.That(r.Complete && r.Contiguous == 100 && r.Received == 100 && r.Ranges.Count == 1, "whole: one range, 100 bytes counted once");
        Check.Equal(Reassembly.Verdict.Refused, new Reassembly(1, "x", 10).Add(8, 4, 10), "a part past the end: refused");
        Check.Equal(Reassembly.Verdict.Refused, new Reassembly(1, "x", 10).Add(0, 4, 11), "another size: refused");
        var empty = new Reassembly(2, "e", 0);
        Check.That(!empty.Complete && empty.Add(0, 0, 0) == Reassembly.Verdict.Write && empty.Complete && empty.Add(0, 0, 0) == Reassembly.Verdict.Duplicate,
                   "an empty file is whole at its one empty part, once");
        // Random orders of random parts: always whole, every byte counted once.
        var rng = new Random(5);
        int ok = 0;
        for (int run = 0; run < 500; run++)
        {
            ulong total = (ulong)rng.Next(1, 5000);
            var parts = FileParts.Split(total, rng.Next(1, 700)).ToList();
            var order = parts.Concat(parts.Where(_ => rng.Next(4) == 0)).OrderBy(_ => rng.Next()).ToList();
            var re = new Reassembly(1, "r", total);
            ulong maxContig = 0; bool monotone = true;
            foreach (var (o, l) in order)
            {
                re.Add(o, l, total);
                if (re.Contiguous < maxContig) monotone = false;
                maxContig = re.Contiguous;
            }
            if (re.Complete && re.Received == total && monotone && re.Ranges.Count == 1) ok++;
        }
        Check.Equal(500, ok, "500 files in random order with repeats: whole, each byte once, the readable part never shrinking");

        // Routing by ID: only what was asked for, cancel, the cap.
        var inc = new IncomingParts();
        Check.Equal(IncomingParts.Outcome.Unknown, inc.Route(9, "x", 10, 0, 10).Outcome, "a part never asked for: unknown, dropped");
        inc.Expect(9);
        var first = inc.Route(9, "x.txt", 10, 0, 4);
        Check.That(first.Outcome == IncomingParts.Outcome.Write && first.File!.Name == "x.txt", "asked for: written, the name from the first part");
        Check.Equal(IncomingParts.Outcome.WriteAndComplete, inc.Route(9, "x.txt", 10, 4, 6).Outcome, "the last part: written and whole");
        inc.Expect(10);
        inc.Route(10, "y", 100, 0, 10);
        inc.Cancel(10);
        Check.Equal(IncomingParts.Outcome.Cancelled, inc.Route(10, "y", 100, 10, 10).Outcome, "a part after cancel: cancelled, dropped");
        inc.Expect(11);
        var tooBig = inc.Route(11, "big", FileParts.MaxDownload + 1, 0, 1);
        Check.That(tooBig.Outcome == IncomingParts.Outcome.Refused && tooBig.Why != null, $"a file over the cap: refused ({tooBig.Why})");
        inc.Expect(12);
        inc.Route(12, "z", 50, 0, 10);
        Check.Equal(IncomingParts.Outcome.Refused, inc.Route(12, "z", 60, 10, 10).Outcome, "a size that changes mid-transfer: refused");
        for (uint i = 100; i < 100 + IncomingParts.RememberCancelled + 10; i++) { inc.Expect(i); inc.Cancel(i); }
        Check.Equal(IncomingParts.Outcome.Unknown, inc.Route(100, "o", 1, 0, 1).Outcome, "only the last 256 cancels are remembered");
        // Parts of two transfers interleaved, on two "connections": each whole.
        var two = new IncomingParts();
        two.Expect(1); two.Expect(2);
        var sends = FileParts.Split(3000, 512).Select(p => (id: 1u, p)).Concat(FileParts.Split(2000, 512).Select(p => (id: 2u, p))).OrderBy(_ => rng.Next()).ToList();
        int completes = sends.Count(s => two.Route(s.id, "f", s.id == 1 ? 3000UL : 2000UL, s.p.offset, s.p.length).Outcome == IncomingParts.Outcome.WriteAndComplete);
        Check.Equal(2, completes, "two transfers' parts interleaved in random order: each completes once");
    }

    public static void DataLink()
    {
        var token = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        Check.That(DataLinkPolicy.Opens(1, new DataTicket { Token = token }), "a ticket from a host with `data` 1: opened");
        Check.That(!DataLinkPolicy.Opens(null, new DataTicket { Token = token }), "from a host whose list does not say `data`: not");
        Check.That(!DataLinkPolicy.Opens(1, new DataTicket { Token = Convert.ToBase64String(new byte[16]) }), "a token of 16 bytes: not");
        Check.That(!DataLinkPolicy.Opens(1, new DataTicket { Token = "not base64!" }), "a token that is not base64: not");
        Check.That(!DataLinkPolicy.Opens(1, null), "no ticket: not");
        var ticket = Json.Decode<DataTicket>(Encoding.UTF8.GetBytes($"{{\"token\":\"{token}\"}}"));
        Check.That(ticket?.Token == token, "kind 48's JSON decodes");
        Check.SameJson($"{{\"token\":\"{token}\"}}", DataLinkPolicy.Hello(token), "kind 49: {\"token\":…}, the same token");
        Check.That(Encoding.UTF8.GetString(DataLinkPolicy.Hello("a+b/c=")).Contains("a+b/c=", StringComparison.Ordinal), "base64's + and / are written as they are (Swift does too)");
        Check.Equal("sill-data/1", DataHello.Alpn, "the ALPN is sill-data/1");
        Check.That(DataLinkPolicy.NextTry(1) == 1 && DataLinkPolicy.NextTry(3) == 10 && DataLinkPolicy.NextTry(4) == null && DataLinkPolicy.NextTry(0) == null,
                   "tries after 1, 3 and 10 s, then none (the parts stay on the session)");
        Check.That(DataLinkPolicy.Route(true) == (true, 512 * 1024) && DataLinkPolicy.Route(false) == (false, 64 * 1024),
                   "parts of 512 KB on the bulk connection, 64 KB on the session");
        Check.That(DataLinkPolicy.Restart(startedOnData: true, dataUp: false, sessionUp: true), "a transfer whose bulk connection went: begun again on the session");
        Check.That(!DataLinkPolicy.Restart(true, false, false) && !DataLinkPolicy.Restart(false, true, true) && !DataLinkPolicy.Restart(true, true, true),
                   "not when the session went too, nor one on the session, nor one whose connection is up");
        Check.Equal(Wire.MaxDataPayload, (1 << 20) + 64, "the bulk connection's cap: 1 MiB + 64");
    }

    public static void Pacing()
    {
        // A steady LAN: round trips at 5 ms. The rate climbs to the top and parts go back to back.
        var p = new UploadPacer(FileParts.DataPartBytes);
        double t = 0;
        for (int i = 0; i < 60; i++) { p.OnRoundTrip(5, t); t += 0.5; }
        Check.That(p.Rate == UploadPacer.MaxRate && !p.Congested && p.Backoffs == 0, $"30 s of 5 ms round trips: the rate at its top ({p.Rate / 1e6:0} MB/s), no backoff");
        // The video's round trip rises 15 ms over the baseline: halved, at most every half second.
        p.OnRoundTrip(21, t);
        Check.That(p.Congested && p.Rate == UploadPacer.MaxRate / 2 && p.Backoffs == 1, "a round trip 16 ms over the 5 ms baseline: the rate halved");
        p.OnRoundTrip(30, t + 0.1);
        Check.That(p.Rate == UploadPacer.MaxRate / 2, "again 0.1 s later: not halved twice in half a second");
        p.OnRoundTrip(19.9, t + 0.7);
        Check.That(!p.Congested && p.Rate > UploadPacer.MaxRate / 2, "19.9 ms (14.9 over): not congested, the rate grows again");
        for (int i = 0; i < 40; i++) p.OnRoundTrip(80, t + 1 + i * 0.5);
        Check.Equal(UploadPacer.MinRate, p.Rate, "round trips stuck high: down to the floor, never 0");
        // The baseline forgets after 30 s: a path that got slower for good is the new normal.
        var q = new UploadPacer(FileParts.DataPartBytes);
        q.OnRoundTrip(3, 0);
        for (int i = 1; i <= 80; i++) q.OnRoundTrip(40, i * 0.5);
        Check.That(q.Baseline == 40 && !q.Congested, $"a baseline of 3 ms forgotten after 30 s of 40 ms ({q.Baseline} ms)");

        // The token bucket: at a rate of 1 MB/s, 512 KB parts go every half second.
        var b = new UploadPacer(FileParts.DataPartBytes);
        for (int i = 0; i < 30; i++) b.OnRoundTrip(i % 2 == 0 ? 5 : 100, i * 0.5);   // drives it to the floor
        Check.Equal(UploadPacer.MinRate, b.Rate, "(set to the floor, 256 KB/s)");
        double now = 100;
        Check.Equal(0.0, b.Wait(524288, 0, now), "the first part goes at once");
        b.Sent(524288, now);
        Check.That(Math.Abs(b.Wait(524288, 0, now) - 2.048) < 1e-9, "the next waits 2 s at 256 KB/s");
        Check.Equal(0.0, b.Wait(524288, 0, now + 2.048), "then goes");
        // Bytes in flight: never more than one part queued ahead of another.
        var f = new UploadPacer(65536);
        Check.Equal(131072L, f.InFlightCap, "on the session, two 64 KB parts in flight at most");
        Check.That(f.Wait(65536, 65536 + 14, 0) > 0, "a part waits while one is still queued beyond the cap");
        Check.Equal(0.0, f.Wait(65536, 60_000, 0), "and goes when what is queued leaves room");
        Check.Equal(0.0, f.Wait(65536, 0, 0), "nothing queued: goes");
        // A simulated upload of 200 MB beside video on a link of 20 MB/s whose queue shows in the round
        // trip: the rate overshoots the link, the queue shows, the pacer backs off, and the round trip
        // the video sees stays near the 15 ms it allows over the baseline while the file still goes.
        var sim = new UploadPacer(FileParts.DataPartBytes);
        double clock = 0, link = 20e6, queue = 0, sent = 0, worst = 0, lastPing = 0;
        var delays = new List<double>();
        while (sent < 200e6 && clock < 120)
        {
            double step = 0.001;
            queue = Math.Max(0, queue - link * step);
            if (sim.Wait(FileParts.DataPartBytes, 0, clock) == 0) { queue += FileParts.DataPartBytes; sent += FileParts.DataPartBytes; sim.Sent(FileParts.DataPartBytes, clock); }
            double rtt = 4 + queue / link * 1000;
            if (clock - lastPing >= 0.5) { sim.OnRoundTrip(rtt, clock); lastPing = clock; if (clock > 3) delays.Add(rtt); }
            worst = Math.Max(worst, rtt);
            clock += step;
        }
        delays.Sort();
        double p50 = delays.Count > 0 ? delays[delays.Count / 2] : 0;
        Check.That(sent >= 200e6 && clock < 30 && sim.Backoffs > 0 && p50 < 4 + 2 * UploadPacer.BackoffMs,
                   $"200 MB over a simulated 20 MB/s link: sent in {clock:0.0} s ({200 / clock:0.0} MB/s), the round trip's median {p50:0.0} ms over a 4 ms baseline (worst {worst:0} ms), {sim.Backoffs} backoffs");
    }

    public static void Names()
    {
        Check.Equal("report.pdf", WindowsNames.FromMac("report.pdf"), "a plain name stays");
        Check.Equal("a\uF022b\uF023c\uF024\uF025\uF026\uF027\uF021\uF020.txt", WindowsNames.FromMac("a:b<c>?\\|*\".txt"), "the forbidden characters map to Services for Mac's private-use range");
        Check.Equal("a\uF022b", WindowsNames.FromMac("a/b"), "a / (Finder's view of a :) maps as : does");
        Check.Equal("tab\uF009x", WindowsNames.FromMac("tab\tx"), "a control character maps to U+F001–U+F01F");
        Check.Equal("name\uF029", WindowsNames.FromMac("name."), "a trailing dot maps (Windows would drop it)");
        Check.Equal("name\uF028\uF029\uF028", WindowsNames.FromMac("name . "), "every trailing dot and space maps");
        Check.Equal("my file.txt", WindowsNames.FromMac("my file.txt"), "a space inside stays");
        Check.Equal("CON_", WindowsNames.FromMac("CON"), "CON gets an underscore");
        Check.Equal("con_.txt", WindowsNames.FromMac("con.txt"), "con.txt too (a reserved stem with any extension)");
        Check.Equal("LPT9_.tar.gz", WindowsNames.FromMac("LPT9.tar.gz"), "LPT9.tar.gz");
        Check.Equal("COM10.txt", WindowsNames.FromMac("COM10.txt"), "COM10 is not reserved");
        Check.Equal("CONSOLE.txt", WindowsNames.FromMac("CONSOLE.txt"), "CONSOLE is not");
        Check.Equal("_", WindowsNames.FromMac(""), "an empty name becomes _");
        Check.Equal("\u00e9t\u00e9.txt", WindowsNames.FromMac("e\u0301te\u0301.txt"), "a decomposed (NFD) name is composed");
        var longName = new string('a', 300) + ".docx";
        var shortened = WindowsNames.FromMac(longName);
        Check.That(shortened.Length == 255 && shortened.EndsWith(".docx", StringComparison.Ordinal), "a 305-character name is cut to 255 with its extension kept");
        var emoji = new string('a', 249) + "😀😀😀.txt";
        var cut = WindowsNames.FromMac(emoji);
        Check.That(cut.Length <= 255 && !char.IsHighSurrogate(cut[^5]) && cut.EndsWith(".txt", StringComparison.Ordinal), "never half an emoji");
        foreach (var n in new[] { "a:b", "x?", "CON", "trail. ", "q\"*<>|\\", "tab\tx", "a/b" })
            Check.Equal(n.Replace('/', ':') .Replace("CON", "CON"), WindowsNames.ToMac(WindowsNames.FromMac(n)).Replace("CON_", "CON"), $"\"{n}\" maps back (the reserved stem's underscore aside)");
        var rng = new Random(9);
        int valid = 0;
        for (int i = 0; i < 2000; i++)
        {
            const string alphabet = "ab. :<>?*\"|\\/\t\u0301é";
            var s = new string(Enumerable.Range(0, rng.Next(0, 20)).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray());
            var w = WindowsNames.FromMac(s);
            bool good = w.Length > 0 && w.IndexOfAny("<>:\"/\\|?*".ToCharArray()) < 0 && w.All(ch => ch >= 0x20) && !w.EndsWith('.') && !w.EndsWith(' ');
            if (good && WindowsNames.FromMac(w) == w) valid++;
        }
        Check.Equal(2000, valid, "2,000 random Mac names: each a valid Windows name, and mapping it again changes nothing");
    }

    public static void Shell()
    {
        var drop = ShellFormats.DropFiles(new[] { @"C:\a\b.txt", @"D:\Ü\c d.pdf" });
        Check.Equal(20, BitConverter.ToInt32(drop, 0), "DROPFILES.pFiles is 20");
        Check.Equal(1, BitConverter.ToInt32(drop, 16), "fWide is 1");
        Check.Equal(20 + (10 + 1 + 12 + 1 + 1) * 2, drop.Length, "the paths in UTF-16, each NUL-ended, and one more NUL");
        Check.That(ShellFormats.ReadDropFiles(drop).SequenceEqual(new[] { @"C:\a\b.txt", @"D:\Ü\c d.pdf" }), "read back");
        Check.That(ShellFormats.ReadDropFiles(new byte[5]).Count == 0, "too short: no paths");
        var fgd = ShellFormats.FileGroupDescriptor(new[]
        {
            new ShellFormats.Descriptor("a.pdf", 5_000_000_000, false, new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)),
            new ShellFormats.Descriptor("Folder", 0, true),
            new ShellFormats.Descriptor(new string('x', 300), 1, false),
        });
        Check.Equal(4 + 3 * 592, fgd.Length, "FILEGROUPDESCRIPTORW: a count, then 592 bytes a file");
        Check.Equal(3, BitConverter.ToInt32(fgd, 0), "cItems");
        Check.Equal(1u, BitConverter.ToUInt32(fgd, 4 + 64), "nFileSizeHigh of 5 GB is 1 (offset 64)");
        Check.Equal((uint)(5_000_000_000 & 0xFFFFFFFF), BitConverter.ToUInt32(fgd, 4 + 68), "nFileSizeLow (offset 68)");
        Check.Equal('a', (char)BitConverter.ToUInt16(fgd, 4 + 72), "cFileName at offset 72");
        Check.Equal(ShellFormats.FILE_ATTRIBUTE_DIRECTORY, BitConverter.ToUInt32(fgd, 4 + 592 + 36), "a folder's attributes say directory (offset 36)");
        var back = ShellFormats.ReadFileGroupDescriptor(fgd);
        Check.That(back.Count == 3 && back[0] == new ShellFormats.Descriptor("a.pdf", 5_000_000_000, false, new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)) &&
                   back[1].Folder && back[1].Name == "Folder" && back[2].Name.Length == 259, "read back: sizes past 4 GB, the time, a folder, a name held to 259");
        Check.That(ShellFormats.ReadFileGroupDescriptor(fgd.AsSpan(0, 100)).Count == 0, "cut short: nothing");
    }

    public static void Zip()
    {
        var root = Path.Combine(Path.GetTempPath(), "sill-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var src = Path.Combine(root, "src", "Holiday photos");
            Directory.CreateDirectory(Path.Combine(src, "Day 1", "empty"));
            File.WriteAllText(Path.Combine(src, "notes.txt"), "Grüße");
            File.WriteAllBytes(Path.Combine(src, "Day 1", "a.bin"), Enumerable.Range(0, 70_000).Select(i => (byte)(i * 7)).ToArray());
            File.WriteAllText(Path.Combine(src, "Day 1", "a\uF022b.txt"), "colon");   // a Mac name with ':' as Windows shows it
            var zip = Path.Combine(root, "Holiday photos.zip");
            FolderZip.Zip(src, zip);
            using (var z = ZipFile.OpenRead(zip))
            {
                var names = z.Entries.Select(e => e.FullName).ToList();
                Check.That(names.Contains("Holiday photos/") && names.All(n => n.StartsWith("Holiday photos/", StringComparison.Ordinal)), "the folder itself is the zip's top (ditto's --keepParent)");
                Check.That(names.Contains("Holiday photos/Day 1/empty/"), "an empty folder travels");
                Check.That(names.Contains("Holiday photos/Day 1/a:b.txt"), "a private-use name goes back to the Mac's ':'");
            }
            Check.That(FolderZip.Measure(src) == (70_000 + 7 + 5, 3), "measured: three files, their bytes (Grüße is 7 bytes of UTF-8)");
            var outDir = Path.Combine(root, "out");
            Directory.CreateDirectory(outDir);
            var folder = FolderZip.Unpack(zip, outDir, "Holiday photos");
            Check.Equal(Path.Combine(outDir, "Holiday photos"), folder, "unpacked: the zip's top folder");
            Check.That(File.ReadAllText(Path.Combine(folder, "notes.txt")) == "Grüße" &&
                       File.ReadAllBytes(Path.Combine(folder, "Day 1", "a.bin")).SequenceEqual(Enumerable.Range(0, 70_000).Select(i => (byte)(i * 7))) &&
                       Directory.Exists(Path.Combine(folder, "Day 1", "empty")) && File.Exists(Path.Combine(folder, "Day 1", "a\uF022b.txt")),
                       "every file and folder back, the ':' a private-use character again");

            // What ditto -c -k --sequesterRsrc --keepParent makes: __MACOSX and ._ files beside the folder.
            var ditto = Path.Combine(root, "ditto.zip");
            using (var z = new ZipArchive(File.Create(ditto), ZipArchiveMode.Create))
            {
                Write(z, "Report/", null);
                Write(z, "Report/Q4.numbers", "numbers");
                Write(z, "Report/Caf\u0065\u0301.txt", "nfd");
                Write(z, "__MACOSX/Report/._Q4.numbers", "rsrc");
                Write(z, "Report/._hidden", "appledouble");
                Write(z, "Report/.DS_Store", "ds");
                Write(z, "Report/what?.txt", "q");
            }
            var dOut = Path.Combine(root, "dout"); Directory.CreateDirectory(dOut);
            var df = FolderZip.Unpack(ditto, dOut, "Report");
            var files = Directory.GetFiles(df, "*", SearchOption.AllDirectories).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Check.That(files.SequenceEqual(new[] { "Caf\u00e9.txt", "Q4.numbers", "what\uF025.txt" }) && !Directory.Exists(Path.Combine(dOut, "__MACOSX")),
                       $"ditto's zip: __MACOSX, ._ files and .DS_Store left out, names composed and made Windows names ({string.Join(", ", files)})");

            // Zips that try to leave the folder.
            foreach (var evil in new[] { "../escape.txt", "Report/../../escape.txt", "/etc/passwd", "C:/Windows/x.txt", "Report\\..\\..\\x" })
            {
                var ez = Path.Combine(root, "evil.zip");
                File.Delete(ez);
                using (var z = new ZipArchive(File.Create(ez), ZipArchiveMode.Create)) { Write(z, "Report/ok.txt", "ok"); Write(z, evil, "evil"); }
                var eo = Path.Combine(root, "eout-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(eo);
                bool refused = false;
                try { FolderZip.Unpack(ez, eo, "Report"); } catch (FolderZip.UnpackException) { refused = true; }
                Check.That(refused && !File.Exists(Path.Combine(root, "escape.txt")) && !Directory.EnumerateFiles(eo, "*", SearchOption.AllDirectories).Any(),
                           $"an entry \"{evil}\": refused before anything is written");
            }
            // Loose files (no top folder): they go into a folder named for the transfer.
            var loose = Path.Combine(root, "loose.zip");
            using (var z = new ZipArchive(File.Create(loose), ZipArchiveMode.Create)) { Write(z, "a.txt", "a"); Write(z, "b/c.txt", "c"); }
            var lo = Path.Combine(root, "lout"); Directory.CreateDirectory(lo);
            var lf = FolderZip.Unpack(loose, lo, "Stuff");
            Check.That(lf == Path.Combine(lo, "Stuff") && File.Exists(Path.Combine(lf, "a.txt")) && File.Exists(Path.Combine(lf, "b", "c.txt")), "entries without one top folder go into one named for the transfer");
            Check.That(FolderZip.EntryPath("a/./b//c") is { } ep && ep.SequenceEqual(new[] { "a", "b", "c" }), "'.' and doubled slashes are dropped");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    static void Write(ZipArchive z, string name, string? content)
    {
        var e = z.CreateEntry(name);
        if (content == null) return;
        using var s = e.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }

    static bool Throws(Action a)
    {
        try { a(); return false; } catch (ArgumentException) { return true; }
    }
}
