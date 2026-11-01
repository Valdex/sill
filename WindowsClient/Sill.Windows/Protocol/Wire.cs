using System.Buffers.Binary;

namespace Sill.Windows.Protocol;

// The wire format of Sources/StreamProtocol/StreamMessage.swift, as a device speaks it: a 14-byte
// big-endian header (kind 1, timestamp 8 as a Double, keyframe 1, payload length 4), then the
// payload. Kinds the Mac sends that this client does not know are read and skipped, as every
// Sill does.

public enum Kind : byte
{
    ParameterSets = 0, Frame = 1, WindowList = 2, Thumbnail = 3, AppIcon = 4, AppList = 5,
    SelectSource = 6, LaunchApp = 7, Input = 8, Viewport = 9, Ping = 10, Pong = 11, ClientStats = 12,
    Tick = 13, CursorShape = 14, WindowCommand = 15, HostSettings = 16, ChangeSettings = 17,
    MacInfo = 18, PairRequest = 19, PairResult = 20, PairingWanted = 21, Goodbye = 22, Hello = 23,
    MacMenu = 24, PressMenuItem = 25, MacPointer = 26, FetchMenu = 27, Gesture = 28,
    // Side streams, as upstream's pull request #66 numbers them (Coherence.cs): only to and from a
    // host whose window list says `sideStreams`.
    SideStreams = 29,        // device → host: JSON SideStreamRequest
    SideParameterSets = 30,  // host → device: window ID (UInt32 BE) + what kind 0 carries
    SideFrame = 31,          // host → device: window ID (UInt32 BE) + what kind 1 carries
    Clipboard = 32,
    // 33 is #66's sound, unused here (main's sound is 43).
    FileChunk = 34,          // either way: FileChunk, a part of a file
    FileDrop = 35,           // device → host: JSON FileDrop
    DragOffer = 36,          // host → device: JSON DragOffer
    FileRequest = 37,        // device → host: JSON FileRequest
    LiveDrag = 38,           // device → host: JSON LiveDrag
    // 39 (#66's Dock menu), 40 (its display pick) and 42 (its desk rebuild) keep #66's meanings; this
    // client sends none of them.
    TakenBack = 41,          // host → device: JSON TakenBack
    Audio = 43,              // host → device: AudioMessage (Audio.cs), to a device whose hello lists a codec
    KeyframeRequest = 44,    // device → host: empty, or JSON KeyframeRequest to a host that says `coherence` 1
    // This fork's (docs/coherence-protocol.md), to be reserved upstream.
    WindowState = 45,        // host → device: JSON WindowState
    PlaceWindow = 46,        // device → host: JSON PlaceWindow
    DataTicket = 48,         // host → device: JSON DataTicket
    DataHello = 49,          // device → host, the first message of a `sill-data/1` connection: JSON DataHello
}

public readonly record struct Header(byte Kind, double Timestamp, bool IsKeyframe, int PayloadLength)
{
    public const int Length = 14;

    public static Header Parse(ReadOnlySpan<byte> b) => new(
        b[0],
        BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(b.Slice(1, 8))),
        b[9] == 1,
        (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(b.Slice(10, 4)), int.MaxValue));
}

public static class Wire
{
    /// <summary>Caps a device puts on what a Mac may announce (StreamMessage.swift): a video frame
    /// (a Retina keyframe is 1–2 MB) and anything else. More closes the connection.</summary>
    public const int MaxFramePayload = 32 << 20;
    public const int MaxOtherHostPayload = 4 << 20;
    /// <summary>The Mac's cap on any message from a device, on the session connection: 1 MiB
    /// (StreamMessage.maxClientPayload). A message over it makes the Mac close the connection, so a
    /// device never sends one (Connection.Send refuses it and says so). The largest a device sends
    /// are a clipboard's text (256 KB of UTF-8, which JSON can make more than 1 MiB: then it is not
    /// sent) and a file's part (kind 34, at most <see cref="FileChunk.MaxBytes"/>, 512 KB,
    /// with its header well under the cap).</summary>
    public const int MaxClientPayload = 1 << 20;
    /// <summary>The cap on a message on the bulk connection (`sill-data/1`, docs/coherence-protocol.md):
    /// a 512 KB file part with room for its header.</summary>
    public const int MaxDataPayload = (1 << 20) + 64;

    /// <summary>Whether a device may send a payload this long on a connection with this cap.</summary>
    public static bool Fits(int payloadLength, int cap = MaxClientPayload) => payloadLength >= 0 && payloadLength <= cap;

    public static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    public static byte[] Message(Kind kind, ReadOnlySpan<byte> payload, bool keyframe = false) => Message(kind, payload, Now, keyframe);

    /// <summary>A message stamped with this time (seconds since 1970): kind 43's packets carry their first sample's.</summary>
    public static byte[] Message(Kind kind, ReadOnlySpan<byte> payload, double timestamp, bool keyframe = false)
    {
        var m = new byte[Header.Length + payload.Length];
        m[0] = (byte)kind;
        BinaryPrimitives.WriteInt64BigEndian(m.AsSpan(1, 8), BitConverter.DoubleToInt64Bits(timestamp));
        m[9] = keyframe ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32BigEndian(m.AsSpan(10, 4), (uint)payload.Length);
        payload.CopyTo(m.AsSpan(Header.Length));
        return m;
    }
}

/// <summary>HEVC VPS/SPS/PPS as VideoToolbox gives them, and the NAL length-prefix size (normally 4):
/// nalLen(1) count(1) then per set length(2) bytes.</summary>
public sealed record ParameterSets(int NalLengthSize, IReadOnlyList<byte[]> Sets)
{
    public static ParameterSets? Parse(ReadOnlySpan<byte> d)
    {
        if (d.Length < 2) return null;
        int nal = d[0], count = d[1], o = 2;
        var sets = new List<byte[]>();
        for (int i = 0; i < count; i++)
        {
            if (d.Length < o + 2) return null;
            int len = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(o, 2));
            o += 2;
            if (d.Length < o + len) return null;
            sets.Add(d.Slice(o, len).ToArray());
            o += len;
        }
        if (nal is < 1 or > 4) return null;
        return new ParameterSets(nal, sets);
    }

    public byte[] Encode()
    {
        var ms = new MemoryStream();
        ms.WriteByte((byte)NalLengthSize);
        ms.WriteByte((byte)Sets.Count);
        foreach (var s in Sets)
        {
            ms.WriteByte((byte)(s.Length >> 8));
            ms.WriteByte((byte)s.Length);
            ms.Write(s);
        }
        return ms.ToArray();
    }

    public bool SameAs(ParameterSets? other) =>
        other != null && other.NalLengthSize == NalLengthSize && other.Sets.Count == Sets.Count &&
        Sets.Zip(other.Sets).All(p => p.First.AsSpan().SequenceEqual(p.Second));
}

/// <summary>Binary image payloads (Switcher.swift's ImageBlob, Viewport.swift's CursorShapeBlob).</summary>
public static class Blobs
{
    /// <summary>windowID(4, BE) + JPEG.</summary>
    public static (uint windowID, byte[] jpeg)? Thumbnail(ReadOnlySpan<byte> d) =>
        d.Length > 4 ? (BinaryPrimitives.ReadUInt32BigEndian(d), d[4..].ToArray()) : null;

    /// <summary>bundleIDLength(2, BE) + bundleID UTF-8 + PNG.</summary>
    public static (string bundleID, byte[] png)? Icon(ReadOnlySpan<byte> d)
    {
        if (d.Length <= 2) return null;
        int len = BinaryPrimitives.ReadUInt16BigEndian(d);
        if (d.Length <= 2 + len) return null;
        return (System.Text.Encoding.UTF8.GetString(d.Slice(2, len)), d[(2 + len)..].ToArray());
    }

    /// <summary>hotX, hotY, width, height (Float32 BE, Mac points), then the PNG.</summary>
    public static (float hotX, float hotY, float width, float height, byte[] png)? Cursor(ReadOnlySpan<byte> d)
    {
        if (d.Length <= 16) return null;
        return (BinaryPrimitives.ReadSingleBigEndian(d[..4]), BinaryPrimitives.ReadSingleBigEndian(d[4..8]),
                BinaryPrimitives.ReadSingleBigEndian(d[8..12]), BinaryPrimitives.ReadSingleBigEndian(d[12..16]), d[16..].ToArray());
    }

    public static byte[] Thumbnail(uint windowID, ReadOnlySpan<byte> jpeg)
    {
        var b = new byte[4 + jpeg.Length];
        BinaryPrimitives.WriteUInt32BigEndian(b, windowID);
        jpeg.CopyTo(b.AsSpan(4));
        return b;
    }
}
