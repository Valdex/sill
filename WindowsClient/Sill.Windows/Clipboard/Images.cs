using System.Buffers.Binary;
using System.IO.Compression;

namespace Sill.Windows.Clipboards;

// Clipboard images without GDI+: kind 32's `image` is PNG, and Windows' clipboard holds a DIB (CF_DIB,
// CF_DIBV5) beside an app's "PNG" or instead of it. Reading a DIB here keeps its alpha (GDI+ reads a
// 32-bit DIB as opaque RGB, or as garbage when its alpha byte is unused), and writing CF_DIBV5 with
// an alpha mask is what Windows makes CF_DIB and CF_BITMAP from for apps that read only those. Pure
// (System.IO.Compression's zlib only); WindowsClient/Sill.Checks checks the round trips.

/// <summary>A picture as 32-bit BGRA rows, top row first, alpha straight (not premultiplied).</summary>
public sealed record Bgra(int Width, int Height, byte[] Pixels)
{
    public bool Opaque
    {
        get
        {
            for (int i = 3; i < Pixels.Length; i += 4) if (Pixels[i] != 255) return false;
            return true;
        }
    }
}

public static class Dib
{
    const int BI_RGB = 0, BI_BITFIELDS = 3, V5Size = 124;
    /// <summary>The most pixels a clipboard picture may have here (a 64-megapixel DIB is 256 MB).</summary>
    public const long MaxPixels = 64L << 20;

    /// <summary>A packed DIB (a BITMAPINFOHEADER, V4 or V5 header, its masks or colour table, then the
    /// rows) as BGRA: 24- and 32-bit, BI_RGB or BI_BITFIELDS with 8-bit masks, bottom-up or top-down.
    /// A 32-bit DIB whose alpha bytes are all 0 is opaque (most apps leave the byte unused). Null for
    /// anything else, or when the rows are not all there.</summary>
    public static Bgra? ToBgra(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < 40) return null;
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        if (headerSize is not (40 or 52 or 56 or 108 or 124) || dib.Length < headerSize) return null;
        int width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(dib[16..]);
        int colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(dib[32..]);
        bool topDown = rawHeight < 0;
        int height = Math.Abs(rawHeight);
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels) return null;
        if (bits is not (24 or 32) || compression is not (BI_RGB or BI_BITFIELDS)) return null;
        if (compression == BI_BITFIELDS && bits != 32) return null;
        uint r = 0x00FF0000, g = 0x0000FF00, b = 0x000000FF, a = 0;
        int offset = headerSize;
        if (compression == BI_BITFIELDS)
        {
            if (headerSize == 40)
            {
                // The three masks follow a plain header.
                if (dib.Length < 52) return null;
                r = BinaryPrimitives.ReadUInt32LittleEndian(dib[40..]); g = BinaryPrimitives.ReadUInt32LittleEndian(dib[44..]); b = BinaryPrimitives.ReadUInt32LittleEndian(dib[48..]);
                offset += 12;
            }
            else
            {
                r = BinaryPrimitives.ReadUInt32LittleEndian(dib[40..]); g = BinaryPrimitives.ReadUInt32LittleEndian(dib[44..]); b = BinaryPrimitives.ReadUInt32LittleEndian(dib[48..]);
                if (headerSize >= 56) a = BinaryPrimitives.ReadUInt32LittleEndian(dib[52..]);
            }
            if (!Byte(r) || !Byte(g) || !Byte(b) || (a != 0 && !Byte(a))) return null;
        }
        // BI_RGB at 32 bits: the top byte is alpha or unused, judged by the bytes below.
        // A colour table may sit before the rows of a 24- or 32-bit DIB too (an optimisation hint).
        offset += Math.Max(0, colorsUsed) * 4;
        int stride = (width * bits + 31) / 32 * 4;
        if ((long)offset + (long)stride * height > dib.Length) return null;
        var px = new byte[width * height * 4];
        bool anyAlpha = false;
        int rs = Shift(r), gs = Shift(g), bs = Shift(b), asf = a == 0 ? 24 : Shift(a);
        // Masks that name no alpha: opaque, whatever the spare byte holds.
        bool noAlpha = compression == BI_BITFIELDS && a == 0;
        for (int y = 0; y < height; y++)
        {
            var row = dib.Slice(offset + (topDown ? y : height - 1 - y) * stride, stride);
            int o = y * width * 4;
            for (int x = 0; x < width; x++, o += 4)
            {
                if (bits == 24)
                {
                    px[o] = row[x * 3]; px[o + 1] = row[x * 3 + 1]; px[o + 2] = row[x * 3 + 2]; px[o + 3] = 255;
                }
                else
                {
                    uint v = BinaryPrimitives.ReadUInt32LittleEndian(row[(x * 4)..]);
                    px[o] = (byte)(v >> bs); px[o + 1] = (byte)(v >> gs); px[o + 2] = (byte)(v >> rs);
                    byte alpha = noAlpha ? (byte)255 : (byte)(v >> asf);
                    px[o + 3] = alpha;
                    if (alpha != 0) anyAlpha = true;
                }
            }
        }
        if (bits == 32 && !anyAlpha) for (int i = 3; i < px.Length; i += 4) px[i] = 255;
        return new Bgra(width, height, px);
    }

    /// <summary>A picture as CF_DIBV5: a BITMAPV5HEADER (BI_BITFIELDS, BGRA masks with alpha, sRGB),
    /// then the rows bottom-up, as apps that read a DIB expect most often.</summary>
    public static byte[] ToDibV5(Bgra image)
    {
        int w = image.Width, h = image.Height, stride = w * 4;
        var d = new byte[V5Size + stride * h];
        var s = d.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(s, V5Size);
        BinaryPrimitives.WriteInt32LittleEndian(s[4..], w);
        BinaryPrimitives.WriteInt32LittleEndian(s[8..], h);          // positive: bottom-up
        BinaryPrimitives.WriteUInt16LittleEndian(s[12..], 1);        // planes
        BinaryPrimitives.WriteUInt16LittleEndian(s[14..], 32);       // bits
        BinaryPrimitives.WriteInt32LittleEndian(s[16..], BI_BITFIELDS);
        BinaryPrimitives.WriteInt32LittleEndian(s[20..], stride * h);
        BinaryPrimitives.WriteInt32LittleEndian(s[24..], 3780);      // 96 dpi in pixels a metre
        BinaryPrimitives.WriteInt32LittleEndian(s[28..], 3780);
        BinaryPrimitives.WriteUInt32LittleEndian(s[40..], 0x00FF0000);
        BinaryPrimitives.WriteUInt32LittleEndian(s[44..], 0x0000FF00);
        BinaryPrimitives.WriteUInt32LittleEndian(s[48..], 0x000000FF);
        BinaryPrimitives.WriteUInt32LittleEndian(s[52..], 0xFF000000);
        BinaryPrimitives.WriteUInt32LittleEndian(s[56..], 0x73524742);   // LCS_sRGB, 'sRGB'
        BinaryPrimitives.WriteUInt32LittleEndian(s[108..], 4);           // LCS_GM_IMAGES
        for (int y = 0; y < h; y++)
            image.Pixels.AsSpan(y * stride, stride).CopyTo(s[(V5Size + (h - 1 - y) * stride)..]);
        return d;
    }

    static bool Byte(uint mask) => mask is 0xFF or 0xFF00 or 0xFF0000 or 0xFF000000;
    static int Shift(uint mask) => mask switch { 0xFF => 0, 0xFF00 => 8, 0xFF0000 => 16, _ => 24 };
}

/// <summary>PNG, written: 8-bit RGB (an opaque picture) or RGBA, each row with the filter that
/// leaves the least to compress (the PNG book's heuristic), zlib at its smallest. Kind 32's image has
/// to fit a 1 MiB message as base64, so the size counts. Only what a clipboard image needs: no
/// palette, no interlacing.</summary>
public static class Png
{
    static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static byte[] Encode(Bgra image)
    {
        int w = image.Width, h = image.Height;
        bool opaque = image.Opaque;
        int channels = opaque ? 3 : 4, rowBytes = w * channels;
        var raw = new byte[rowBytes];
        var prior = new byte[rowBytes];
        var best = new byte[rowBytes + 1];
        var trial = new byte[rowBytes + 1];
        using var ms = new MemoryStream();
        ms.Write(Signature);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8; ihdr[9] = (byte)(opaque ? 2 : 6);
        Chunk(ms, "IHDR", ihdr);
        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (int y = 0; y < h; y++)
            {
                int src = y * w * 4;
                for (int x = 0; x < w; x++, src += 4)
                {
                    int o = x * channels;
                    raw[o] = image.Pixels[src + 2]; raw[o + 1] = image.Pixels[src + 1]; raw[o + 2] = image.Pixels[src];
                    if (!opaque) raw[o + 3] = image.Pixels[src + 3];
                }
                long bestSum = long.MaxValue;
                for (byte f = 0; f <= 4; f++)
                {
                    trial[0] = f;
                    long sum = 0;
                    for (int i = 0; i < rowBytes; i++)
                    {
                        int left = i >= channels ? raw[i - channels] : 0, up = prior[i], upLeft = i >= channels ? prior[i - channels] : 0;
                        byte v = (byte)(raw[i] - f switch { 0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2, _ => Paeth(left, up, upLeft) });
                        trial[i + 1] = v;
                        sum += v < 128 ? v : 256 - v;
                    }
                    if (sum < bestSum) { bestSum = sum; trial.CopyTo(best, 0); }
                }
                z.Write(best);
                (raw, prior) = (prior, raw);
            }
        }
        Chunk(ms, "IDAT", idat.ToArray());
        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    /// <summary>A PNG's width and height from its IHDR; null when it is not a PNG.</summary>
    public static (int width, int height)? Size(ReadOnlySpan<byte> png)
    {
        if (png.Length < 24 || !png[..8].SequenceEqual(Signature) || !png.Slice(12, 4).SequenceEqual("IHDR"u8)) return null;
        return (BinaryPrimitives.ReadInt32BigEndian(png[16..]), BinaryPrimitives.ReadInt32BigEndian(png[20..]));
    }

    public static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        uint crc = Crc32.Update(Crc32.Update(0xFFFFFFFF, t), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(len, crc);
        s.Write(len);
    }
}

/// <summary>PNG's CRC-32 (ISO 3309, reflected 0xEDB88320), by table: System.IO.Hashing is a package.</summary>
public static class Crc32
{
    static readonly uint[] Table = Make();

    static uint[] Make()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    /// <summary>The running CRC (start at 0xFFFFFFFF, finish with ^ 0xFFFFFFFF).</summary>
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
