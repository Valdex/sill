using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Sill.Windows.Clipboards;
using Sill.Windows.Protocol;

namespace Sill.Checks;

// The clipboard's later generations on this side: CF_HTML's header with UTF-8 offsets, what of a copy
// goes to the Mac within the caps, what of the Mac's item is written, loop prevention and the rate,
// and the pictures (a DIB read with its alpha, CF_DIBV5 written, PNG written and read back).
static class ClipboardChecks
{
    static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    public static void Html()
    {
        // Built: every offset in bytes, pointing at exactly what it names.
        var html = "<b>Grüße, 世界 😀</b>";
        var built = CfHtml.Build(html);
        var text = Encoding.UTF8.GetString(built, 0, built.Length - 1);
        Check.That(built[^1] == 0, "the block ends in a NUL");
        Check.That(text.StartsWith("Version:0.9\r\nStartHTML:", StringComparison.Ordinal), "the header starts with Version:0.9 and StartHTML");
        long F(string key) => long.Parse(text.Split("\r\n").First(l => l.StartsWith(key + ":", StringComparison.Ordinal))[(key.Length + 1)..]);
        long sh = F("StartHTML"), eh = F("EndHTML"), sf = F("StartFragment"), ef = F("EndFragment");
        Check.Equal(built.Length - 1L, eh, "EndHTML is the byte length before the NUL");
        Check.Equal(html, Encoding.UTF8.GetString(built, (int)sf, (int)(ef - sf)), "StartFragment..EndFragment are the HTML's bytes, not its characters");
        Check.That(Encoding.UTF8.GetString(built, (int)sh, 6) == "<html>", "StartHTML points at <html>");
        Check.That(Encoding.UTF8.GetByteCount(html) != html.Length, "(the sample has more bytes than characters, so a count in characters would fail)");
        Check.That(Encoding.UTF8.GetString(built, (int)sf - 20, 20) == "<!--StartFragment-->" && Encoding.UTF8.GetString(built, (int)ef, 18) == "<!--EndFragment-->",
                   "the fragment sits between the two marker comments");

        // Parsed back: the HTML and the fragment.
        var parsed = CfHtml.Parse(built);
        Check.That(parsed is { } p && p.fragment == html && p.html.Contains(html, StringComparison.Ordinal) && p.html.StartsWith("<html>", StringComparison.Ordinal),
                   "parsed back: the fragment exactly, the HTML with its context");

        // A whole document keeps its head and body; its fragment is the body's inside.
        var doc = "<html><head><meta charset=\"utf-8\"><style>p{color:red}</style></head><body class=\"x\"><p>Ü</p></body></html>";
        var d = CfHtml.Parse(CfHtml.Build(doc));
        Check.That(d is { } dd && dd.fragment == "<p>Ü</p>" && dd.html.Contains("<style>p{color:red}</style>", StringComparison.Ordinal), "a document: the body's inside is the fragment, the head kept");
        // A document with markers of its own keeps them.
        var marked = "<html><body><table><tr><!--StartFragment--><td>1</td><!--EndFragment--></tr></table></body></html>";
        var m = CfHtml.Parse(CfHtml.Build(marked));
        Check.That(m is { } mm && mm.fragment == "<td>1</td>" && mm.html.Contains("<table>", StringComparison.Ordinal), "its own markers kept: a table cell with its table around it");

        // What other apps write: Word-like CRLF header with SourceURL, -1 for StartHTML, offsets past the end, NUL padding.
        string Header(int sh2, int eh2, int sf2, int ef2, string extra = "") =>
            $"Version:1.0\r\nStartHTML:{sh2:D10}\r\nEndHTML:{eh2:D10}\r\nStartFragment:{sf2:D10}\r\nEndFragment:{ef2:D10}\r\n{extra}";
        var body = "<html><body><!--StartFragment-->é<!--EndFragment--></body></html>";
        int hl = Header(0, 0, 0, 0, "SourceURL:https://example.com/a?b=c\r\n").Length;
        int fs = hl + body.IndexOf("é", StringComparison.Ordinal) , fe = fs + 2;
        var chrome = U(Header(hl, hl + U(body).Length, fs, fe, "SourceURL:https://example.com/a?b=c\r\n") + body);
        Check.That(CfHtml.Parse(chrome) is { } c && c.fragment == "é" && c.html == body, "Version 1.0 with a SourceURL line");
        var padded = chrome.Concat(new byte[13]).ToArray();
        Check.That(CfHtml.Parse(padded) is { } pd && pd.fragment == "é", "NULs after the HTML (a rounded-up block) are not HTML");
        int hl2 = Header(0, 0, 0, 0).Length;
        var minus = U(Header(0, 0, 0, 0).Replace("StartHTML:0000000000", "StartHTML:-000000001").Replace("EndHTML:0000000000", "EndHTML:-000000001")
                      .Replace("StartFragment:0000000000", $"StartFragment:{hl2 + 32:D10}").Replace("EndFragment:0000000000", $"EndFragment:{hl2 + 34:D10}") + body);
        Check.That(CfHtml.Parse(minus) is { } mi && mi.fragment == "é" && mi.html == body, "StartHTML and EndHTML -1: the HTML is all after the header");
        var past = U(Header(hl2, hl2 + 9999, hl2 + 32, hl2 + 34) + body);
        Check.That(CfHtml.Parse(past) is { } pa && pa.html == body, "an EndHTML past the end is held to what is there");
        var wrongFragment = U(Header(hl2, hl2 + U(body).Length, hl2 + 5000, hl2 + 6000) + body);
        Check.That(CfHtml.Parse(wrongFragment) is { } wf && wf.fragment == "é", "a fragment offset outside the HTML: the markers decide");
        Check.That(CfHtml.Parse(U("<html><body>no header</body></html>")) == null, "no header: null");
        Check.That(CfHtml.Parse(Array.Empty<byte>()) == null, "nothing: null");
        // Random documents round-trip.
        var rng = new Random(7);
        int good = 0;
        for (int i = 0; i < 300; i++)
        {
            var sb = new StringBuilder();
            int n = rng.Next(0, 60);
            var tokens = new[] { "a", "Z", "<", ">", "&", "é", "€", "😀", "\n", "\t", "\"", "'", "<p>", "</td>" };
            for (int k = 0; k < n; k++) sb.Append(tokens[rng.Next(tokens.Length)]);
            var frag = sb.ToString().Replace("<!--", "");
            if (CfHtml.Parse(CfHtml.Build(frag)) is { } r && r.fragment == frag) good++;
        }
        Check.Equal(300, good, "300 random fragments (ASCII, accents, the euro, emoji, tags) come back exactly");
    }

    public static void Rules()
    {
        // The subscription by the Mac's generation.
        Check.Bytes(U("{}"), ClipboardRules.Subscription(1), "a Mac of text alone: {}");
        Check.Bytes(U("{}"), ClipboardRules.Subscription(null), "a Mac that says nothing: {} (ShareClipboard sends nothing to it anyway)");
        Check.SameJson("{\"accepts\":2}", ClipboardRules.Subscription(2), "a Mac of generation 2: {\"accepts\":2}");
        Check.SameJson("{\"accepts\":3}", ClipboardRules.Subscription(3), "a Mac of generation 3: {\"accepts\":3}");
        Check.SameJson("{\"accepts\":3}", ClipboardRules.Subscription(9), "a later Mac: what this PC takes, 3");

        var png = new byte[1000]; png[0] = 0x89; png[1] = 0x50; png[2] = 0x4E; png[3] = 0x47; png[4] = 0x0D; png[5] = 0x0A; png[6] = 0x1A; png[7] = 0x0A;
        var copy = new PcCopy { Text = "hi", Html = "<b>hi</b>", Rtf = U("{\\rtf1 hi}"), Png = png };
        var p1 = ClipboardRules.Outgoing(copy, 1, out var l1);
        Check.SameJson("{\"text\":\"hi\"}", p1, "generation 1: the text alone");
        var p2 = ClipboardRules.Outgoing(copy, 2, out var l2);
        var c2 = Json.Decode<ClipboardPayload>(p2);
        Check.That(c2 is { Text: "hi", Html: "<b>hi</b>" } && c2.Rtf!.SequenceEqual(copy.Rtf!) && c2.Image!.SequenceEqual(png) && l2.Count == 0,
                   "generation 2: text, html, rtf and the image, nothing left out");
        Check.That(ClipboardRules.Outgoing(copy, 0, out _) == null, "a Mac that takes no clipboard: nothing");
        Check.That(ClipboardRules.Outgoing(new PcCopy(), 3, out _) == null, "an empty copy: nothing");
        Check.That(ClipboardRules.Outgoing(new PcCopy { Png = png }, 1, out _) == null, "an image alone to generation 1: nothing");

        // The caps: the whole within 1 MiB; the image first to go, then RTF, then HTML.
        var bigPng = new byte[700_000]; png.CopyTo(bigPng, 0);
        var p3 = ClipboardRules.Outgoing(new PcCopy { Text = "t", Png = bigPng, Html = new string('h', 200_000) }, 2, out var l3);
        var c3 = Json.Decode<ClipboardPayload>(p3);
        Check.That(p3 != null && p3.Length <= Wire.MaxClientPayload && c3!.Image == null && c3.Html != null && l3.SequenceEqual(new[] { ClipboardRules.Left.Image }),
                   $"an image that fits alone but not with 200 KB of HTML: the image dropped ({p3?.Length} bytes)");
        var hugePng = new byte[800_000]; png.CopyTo(hugePng, 0);
        ClipboardRules.Outgoing(new PcCopy { Text = "t", Png = hugePng }, 2, out var l4);
        Check.That(l4.SequenceEqual(new[] { ClipboardRules.Left.ImageTooLarge }), "an 800 KB PNG (over 1 MiB as base64): skipped, said");
        var justPng = new byte[780_000]; png.CopyTo(justPng, 0);
        var p5 = ClipboardRules.Outgoing(new PcCopy { Png = justPng }, 2, out var l5);
        Check.That(p5 != null && p5.Length <= Wire.MaxClientPayload && l5.Count == 0, $"a 780 KB PNG alone fits ({p5?.Length} bytes)");
        var longText = new string('x', ClipboardRules.MaxTextBytes + 1);
        var p6 = ClipboardRules.Outgoing(new PcCopy { Text = longText, Html = "<p>x</p>" }, 2, out var l6);
        Check.That(Json.Decode<ClipboardPayload>(p6) is { Text: null, Html: "<p>x</p>" } && l6.Contains(ClipboardRules.Left.TextTooLong),
                   "text over 256 KB is left out, the HTML still goes");
        var control = new string('\u0001', ClipboardRules.MaxTextBytes);
        var p7 = ClipboardRules.Outgoing(new PcCopy { Text = control }, 1, out var l7);
        Check.That(p7 == null && l7.Contains(ClipboardRules.Left.TextTooLong), "256 KB of control characters (six bytes each as JSON): not sent");
        var p8 = ClipboardRules.Outgoing(new PcCopy { Text = "t", Rtf = new byte[500_000], Html = new string('h', 400_000) }, 3, out var l8);
        Check.That(Json.Decode<ClipboardPayload>(p8) is { Rtf: null, Html: not null } && l8.SequenceEqual(new[] { ClipboardRules.Left.Rtf }),
                   "RTF goes before HTML when both do not fit");
        Check.That(ClipboardRules.Describe(l3).Length > 0 && !ClipboardRules.Describe(l3).Contains("hhh", StringComparison.Ordinal), "the log's words name what was dropped, never what was copied");

        // The Mac's item, by the generation subscribed with.
        var mac = new ClipboardPayload { Text = "t", Html = "<i>t</i>", Rtf = U("{\\rtf1}"), Image = png, Concealed = true,
                                         Files = new ClipboardFiles { Offer = 12, Files = new() { new OfferedFile { Name = "a.pdf", Size = 5 } } } };
        var m1 = ClipboardRules.Incoming(mac, 1);
        Check.That(m1 is { Text: "t", Html: null, Rtf: null, Image: null, Files: null, Concealed: true }, "subscribed as 1: the text alone is written (concealed still kept from history)");
        var m2 = ClipboardRules.Incoming(mac, 2);
        Check.That(m2 is { Html: "<i>t</i>", Rtf: not null, Image: not null, Files: null }, "subscribed as 2: html, rtf and image, no files");
        var m3 = ClipboardRules.Incoming(mac, 3);
        Check.That(m3.Files is { offer: 12 } f && f.files[0].Name == "a.pdf", "subscribed as 3: the files' offer too");
        Check.That(ClipboardRules.Incoming(new ClipboardPayload { Image = U("GIF89a not a png") }, 3).Image == null, "an image that is neither PNG nor JPEG is not written");
        Check.That(ClipboardRules.Incoming(new ClipboardPayload { Image = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0 } }, 2).Image != null, "a JPEG (#66's host sends some) is");
        Check.That(ClipboardRules.Incoming(new ClipboardPayload { Files = new ClipboardFiles { Offer = 3, Files = new() } }, 3).Files == null, "an offer of no files is nothing");
        Check.That(ClipboardRules.Incoming(new ClipboardPayload(), 3).IsEmpty, "an empty item is empty");
        Check.That(!ClipboardRules.Incoming(mac, 3).IsEmpty, "a full item is not");
    }

    public static void Loop()
    {
        var loop = new ClipboardLoop();
        Check.Equal(ClipboardLoop.Verdict.Send, loop.Decide(10, false, false, 0).Verdict, "a change made here: sent");
        loop.Sent(0);
        loop.WroteOwn(11);
        Check.Equal(ClipboardLoop.Verdict.Own, loop.Decide(11, false, false, 0.2).Verdict, "this PC's own write of the Mac's item (its sequence number): not sent back");
        Check.Equal(ClipboardLoop.Verdict.Marked, loop.Decide(12, true, false, 0.3).Verdict, "a change carrying Sill.Origin (another Sill, or our write's number missed): not sent");
        Check.Equal(ClipboardLoop.Verdict.Excluded, loop.Decide(13, false, true, 0.4).Verdict, "a password manager's (excluded from monitors): not sent");
        Check.Equal(ClipboardLoop.Verdict.Own, loop.Decide(11, true, true, 0.5).Verdict, "the own number wins over the rest");
        // The rate: four in any second, the fifth held to when the first is a second old.
        var r = new ClipboardLoop();
        for (int i = 0; i < 4; i++) { Check.Equal(ClipboardLoop.Verdict.Send, r.Decide((uint)(20 + i), false, false, i * 0.1).Verdict, $"copy {i + 1} in the second: sent"); r.Sent(i * 0.1); }
        var held = r.Decide(30, false, false, 0.5);
        Check.That(held.Verdict == ClipboardLoop.Verdict.Later && Math.Abs(held.Wait - 0.5) < 1e-9, $"the fifth within a second: held 0.5 s, until the first is a second old ({held.Wait})");
        Check.Equal(ClipboardLoop.Verdict.Send, r.Decide(30, false, false, 1.0).Verdict, "then sent");
        Check.That(r.Decide(31, false, false, 1.0).Verdict == ClipboardLoop.Verdict.Send, "skips do not count against the rate");
        // A random hour of copies: never more than 4 in any second; own writes never sent.
        var rng = new Random(11);
        var lp = new ClipboardLoop();
        var sentAt = new List<double>();
        double t = 0; uint seq = 1; int own = 0, ownSent = 0;
        for (int i = 0; i < 20_000; i++)
        {
            t += rng.NextDouble() * 0.4;
            seq++;
            bool isOwn = rng.Next(5) == 0;
            if (isOwn) { lp.WroteOwn(seq); own++; }
            var d = lp.Decide(seq, false, false, t);
            if (d.Verdict == ClipboardLoop.Verdict.Send) { lp.Sent(t); sentAt.Add(t); if (isOwn) ownSent++; }
        }
        int worst = 0;
        for (int i = 0; i < sentAt.Count; i++) { int j = i; while (j < sentAt.Count && sentAt[j] - sentAt[i] < 1) j++; worst = Math.Max(worst, j - i); }
        Check.That(worst <= ClipboardRules.PerSecond && ownSent == 0 && own > 0, $"20,000 random changes: at most {worst} sent in any second, none of the {own} own writes");
    }

    public static void Pictures()
    {
        // A 3x2 picture with alpha, through CF_DIBV5 and back.
        var px = new byte[3 * 2 * 4];
        for (int i = 0; i < 6; i++) { px[i * 4] = (byte)(i * 40); px[i * 4 + 1] = (byte)(200 - i * 30); px[i * 4 + 2] = (byte)(i * 7); px[i * 4 + 3] = (byte)(255 - i * 50); }
        var img = new Bgra(3, 2, px);
        var v5 = Dib.ToDibV5(img);
        Check.Equal(124 + 24, v5.Length, "CF_DIBV5: a 124-byte header, then 4 bytes a pixel");
        Check.Equal(124, BinaryPrimitives.ReadInt32LittleEndian(v5), "bV5Size 124");
        Check.That(BinaryPrimitives.ReadInt32LittleEndian(v5.AsSpan(8)) == 2, "a positive height: bottom-up");
        Check.That(BinaryPrimitives.ReadInt32LittleEndian(v5.AsSpan(16)) == 3 && BinaryPrimitives.ReadUInt32LittleEndian(v5.AsSpan(52)) == 0xFF000000,
                   "BI_BITFIELDS with an alpha mask");
        Check.That(v5.AsSpan(124, 12).SequenceEqual(px.AsSpan(12, 12)), "the last row first (bottom-up)");
        var back = Dib.ToBgra(v5);
        Check.That(back != null && back.Width == 3 && back.Height == 2 && back.Pixels.SequenceEqual(px), "read back: the same pixels, alpha kept");

        // A 24-bit DIB with row padding, bottom-up; a 32-bit one whose alpha is unused; a top-down one; BITFIELDS after a plain header.
        var d24 = new byte[40 + 8 * 2];
        BinaryPrimitives.WriteInt32LittleEndian(d24, 40); BinaryPrimitives.WriteInt32LittleEndian(d24.AsSpan(4), 2); BinaryPrimitives.WriteInt32LittleEndian(d24.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(d24.AsSpan(12), 1); BinaryPrimitives.WriteUInt16LittleEndian(d24.AsSpan(14), 24);
        // Bottom row (stored first): blue, green; top row: red, white.
        new byte[] { 255, 0, 0, 0, 255, 0, 0, 0 }.CopyTo(d24, 40);
        new byte[] { 0, 0, 255, 255, 255, 255, 0, 0 }.CopyTo(d24, 48);
        var r24 = Dib.ToBgra(d24);
        Check.That(r24 != null && r24.Pixels.SequenceEqual(new byte[] { 0, 0, 255, 255, 255, 255, 255, 255, 255, 0, 0, 255, 0, 255, 0, 255 }),
                   "a 24-bit DIB, rows padded to 4 bytes, bottom-up: red and white on top, opaque");
        var d32 = new byte[40 + 4];
        BinaryPrimitives.WriteInt32LittleEndian(d32, 40); BinaryPrimitives.WriteInt32LittleEndian(d32.AsSpan(4), 1); BinaryPrimitives.WriteInt32LittleEndian(d32.AsSpan(8), -1);
        BinaryPrimitives.WriteUInt16LittleEndian(d32.AsSpan(12), 1); BinaryPrimitives.WriteUInt16LittleEndian(d32.AsSpan(14), 32);
        new byte[] { 10, 20, 30, 0 }.CopyTo(d32, 40);
        Check.That(Dib.ToBgra(d32) is { } r32 && r32.Pixels.SequenceEqual(new byte[] { 10, 20, 30, 255 }), "a 32-bit DIB whose alpha bytes are all 0: opaque, not invisible");
        var dbf = new byte[52 + 4];
        BinaryPrimitives.WriteInt32LittleEndian(dbf, 40); BinaryPrimitives.WriteInt32LittleEndian(dbf.AsSpan(4), 1); BinaryPrimitives.WriteInt32LittleEndian(dbf.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dbf.AsSpan(12), 1); BinaryPrimitives.WriteUInt16LittleEndian(dbf.AsSpan(14), 32); BinaryPrimitives.WriteInt32LittleEndian(dbf.AsSpan(16), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(dbf.AsSpan(40), 0x000000FF); BinaryPrimitives.WriteUInt32LittleEndian(dbf.AsSpan(44), 0x0000FF00); BinaryPrimitives.WriteUInt32LittleEndian(dbf.AsSpan(48), 0x00FF0000);
        new byte[] { 1, 2, 3, 99 }.CopyTo(dbf, 52);
        Check.That(Dib.ToBgra(dbf) is { } rbf && rbf.Pixels.SequenceEqual(new byte[] { 3, 2, 1, 255 }), "BI_BITFIELDS after a plain header (RGB order masks): read by the masks, opaque (no alpha mask)");
        Check.That(Dib.ToBgra(d24.AsSpan(0, 50)) == null, "rows cut short: null");
        var d8 = (byte[])d24.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(d8.AsSpan(14), 8);
        Check.That(Dib.ToBgra(d8) == null, "an 8-bit (palette) DIB: null, not guessed");
        var huge = (byte[])d24.Clone(); BinaryPrimitives.WriteInt32LittleEndian(huge.AsSpan(4), 100_000); BinaryPrimitives.WriteInt32LittleEndian(huge.AsSpan(8), 100_000);
        Check.That(Dib.ToBgra(huge) == null, "a header claiming 10 gigapixels: null, nothing allocated");

        // PNG: written, then read back by a decoder written here (inflate and the five filters).
        var png = Png.Encode(img);
        Check.That(ClipboardRules.IsPng(png) && Png.Size(png) == (3, 2), "a PNG with its IHDR's size");
        Check.That(Decode(png) is { } dec && dec.SequenceEqual(px), "decoded back: the same pixels and alpha (RGBA)");
        var opaque = new Bgra(2, 2, new byte[] { 1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255 });
        var op = Png.Encode(opaque);
        Check.Equal((byte)2, op[25], "an opaque picture is RGB (colour type 2), a quarter smaller");
        Check.That(Decode(op) is { } dop && dop.SequenceEqual(opaque.Pixels), "and decodes to the same pixels");
        // A screenshot-like 200x150: gradients and flat areas, every filter used somewhere.
        var rng = new Random(3);
        var big = new byte[200 * 150 * 4];
        for (int y = 0; y < 150; y++) for (int x = 0; x < 200; x++)
        {
            int o = (y * 200 + x) * 4;
            big[o] = (byte)(x < 100 ? 240 : x); big[o + 1] = (byte)(y * 2); big[o + 2] = (byte)(rng.Next(4) == 0 ? rng.Next(256) : 30); big[o + 3] = (byte)(y < 20 ? 128 : 255);
        }
        var bigImg = new Bgra(200, 150, big);
        var bigPng = Png.Encode(bigImg);
        Check.That(Decode(bigPng) is { } db && db.SequenceEqual(big), $"a 200x150 picture with noise and alpha decodes exactly ({bigPng.Length:N0} bytes of {big.Length:N0})");
        That(Crc32.Update(0xFFFFFFFF, "123456789"u8) ^ 0xFFFFFFFF, "CRC-32 of 123456789 is CBF43926");
        // A PNG from this encoder through the DIB path: DIBV5 of the decoded pixels reads back the same.
        Check.That(Dib.ToBgra(Dib.ToDibV5(bigImg)) is { } rt && rt.Pixels.SequenceEqual(big), "a picture through CF_DIBV5 and back: the same");
    }

    static void That(uint crc, string what) => Check.Equal(0xCBF43926u, crc, what);

    /// <summary>A PNG decoder for what Png.Encode writes (8-bit RGB or RGBA, no interlacing), as BGRA.</summary>
    static byte[]? Decode(byte[] png)
    {
        int pos = 8, w = 0, h = 0, type = 0;
        var idat = new MemoryStream();
        while (pos + 8 <= png.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            var t = Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = png.AsSpan(pos + 8, len);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + len));
            if ((Crc32.Update(Crc32.Update(0xFFFFFFFF, png.AsSpan(pos + 4, 4)), data) ^ 0xFFFFFFFF) != crc) return null;
            if (t == "IHDR") { w = BinaryPrimitives.ReadInt32BigEndian(data); h = BinaryPrimitives.ReadInt32BigEndian(data[4..]); type = data[9]; }
            if (t == "IDAT") idat.Write(data);
            pos += 12 + len;
        }
        int ch = type == 6 ? 4 : 3, rb = w * ch;
        var raw = new MemoryStream();
        using (var z = new ZLibStream(new MemoryStream(idat.ToArray()), CompressionMode.Decompress)) z.CopyTo(raw);
        var f = raw.ToArray();
        if (f.Length != h * (rb + 1)) return null;
        var prior = new byte[rb];
        var cur = new byte[rb];
        var outp = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int filter = f[y * (rb + 1)];
            for (int i = 0; i < rb; i++)
            {
                int left = i >= ch ? cur[i - ch] : 0, up = prior[i], ul = i >= ch ? prior[i - ch] : 0;
                int pred = filter switch { 0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2, _ => Png.Paeth(left, up, ul) };
                cur[i] = (byte)(f[y * (rb + 1) + 1 + i] + pred);
            }
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                outp[o] = cur[x * ch + 2]; outp[o + 1] = cur[x * ch + 1]; outp[o + 2] = cur[x * ch]; outp[o + 3] = ch == 4 ? cur[x * ch + 3] : (byte)255;
            }
            (prior, cur) = (cur, prior);
        }
        return outp;
    }
}
