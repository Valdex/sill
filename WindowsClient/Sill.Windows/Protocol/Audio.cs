using System.Buffers.Binary;
using System.Text.Json.Serialization;

namespace Sill.Windows.Protocol;

// The Mac's sound (kind 43), as Sources/StreamProtocol/Audio.swift frames it: a payload's first byte
// says what follows, type 1 how to decode what follows (JSON AudioFormat), type 2 packets of one
// epoch (binary AudioPackets). A reader skips a type it does not know. A host sends kind 43 only to a
// device whose hello lists a codec it makes, the first of that list it can make, and only while its
// Send Audio is on.
//
// This client plays `pcm-s16le` (docs/coherence-protocol.md): Media Foundation has no AAC-ELD
// decoder, so it lists only that. Each packet is interleaved signed 16-bit little-endian frames,
// 480 frames (10 ms at 48 kHz) a packet.

public static class AudioCodec
{
    /// <summary>AAC-ELD, upstream's codec; not played here.</summary>
    public const string AacEld = "aac-eld";
    /// <summary>Plain PCM (AudioCodec.pcmS16LE in this fork's host): 1.5 Mbit/s at 48 kHz stereo, for the local network.</summary>
    public const string PcmS16LE = "pcm-s16le";
    /// <summary>What this client lists in its hello, best first.</summary>
    public static readonly IReadOnlyList<string> DevicePlays = new[] { PcmS16LE };

    /// <summary>The host's pick: the first of the device's list it makes; null for no list or none it makes.</summary>
    public static string? Choose(IReadOnlyList<string>? offered, IReadOnlyCollection<string> makes) =>
        offered?.FirstOrDefault(makes.Contains);
}

/// <summary>Kind 43 type 1: how to decode what follows. Every field optional.</summary>
public sealed class AudioFormat
{
    [JsonPropertyName("codec")] public string? Codec { get; set; }
    [JsonPropertyName("sampleRate")] public int? SampleRate { get; set; }
    [JsonPropertyName("channels")] public int? Channels { get; set; }
    /// <summary>The frames each packet decodes to: 480 for 10 ms at 48 kHz.</summary>
    [JsonPropertyName("framesPerPacket")] public int? FramesPerPacket { get; set; }
    /// <summary>Frames a segment starts with that are the codec's own, dropped (AAC-ELD's; none for PCM).</summary>
    [JsonPropertyName("primingFrames")] public int? PrimingFrames { get; set; }
    [JsonPropertyName("bitrate")] public int? Bitrate { get; set; }
    /// <summary>The epoch of the packets this format belongs to, 0 to 65535.</summary>
    [JsonPropertyName("epoch")] public int? Epoch { get; set; }
    /// <summary>The codec's magic cookie (base64 in the JSON); none for PCM.</summary>
    [JsonPropertyName("cookie")] public byte[]? Cookie { get; set; }
    /// <summary>"app", "desktop" or "test"; display only.</summary>
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("app")] public string? App { get; set; }
}

/// <summary>Kind 43 type 2: one or more packets of one epoch, back to back. The header's time stamp is
/// the host's wall clock of the first packet's first sample.
/// <code>type 2 · epoch UInt16 · seq UInt32 · flags UInt8 · count UInt8 · count × (length UInt16 · bytes)</code>
/// Flags bit 0: the first packet starts a segment.</summary>
public sealed record AudioPackets(int Epoch, uint Seq, bool SegmentStart, IReadOnlyList<byte[]> Packets)
{
    public const int MaxCount = 255;
    public const int MaxPacketBytes = 65_535;
    public const int HeaderBytes = 9;

    /// <summary>The payload, its type byte included (at most <see cref="MaxCount"/> packets of at most
    /// <see cref="MaxPacketBytes"/> each, as Swift's serialized()).</summary>
    public byte[] Serialize()
    {
        var kept = Packets.Take(MaxCount).ToList();
        var d = new byte[HeaderBytes + kept.Sum(p => 2 + Math.Min(p.Length, MaxPacketBytes))];
        d[0] = AudioMessage.TypePackets;
        BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(1), (ushort)Epoch);
        BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(3), Seq);
        d[7] = SegmentStart ? (byte)1 : (byte)0;
        d[8] = (byte)kept.Count;
        int o = HeaderBytes;
        foreach (var p in kept)
        {
            int n = Math.Min(p.Length, MaxPacketBytes);
            BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(o), (ushort)n);
            p.AsSpan(0, n).CopyTo(d.AsSpan(o + 2));
            o += 2 + n;
        }
        return d;
    }
}

/// <summary>A kind 43 payload, either type.</summary>
public abstract record AudioMessage
{
    public sealed record Format(AudioFormat Value) : AudioMessage;
    public sealed record Packets(AudioPackets Value) : AudioMessage;

    public const byte TypeFormat = 1, TypePackets = 2;
    /// <summary>The largest payload of each type a reader takes; past them it is dropped whole.</summary>
    public const int MaxFormatPayload = 4096, MaxPacketsPayload = 16384;

    public static byte[] Serialize(AudioFormat f)
    {
        var json = Json.Encode(f);
        var d = new byte[1 + json.Length];
        d[0] = TypeFormat;
        json.CopyTo(d, 1);
        return d;
    }

    /// <summary>As Audio.swift's parse: null for an empty payload, a type not known, a format over
    /// its cap or one that does not decode, and packets over their cap, cut short, with a count of 0,
    /// a length past the end or bytes left over. Never throws.</summary>
    public static AudioMessage? Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty) return null;
        switch (payload[0])
        {
            case TypeFormat:
                if (payload.Length > MaxFormatPayload) return null;
                return Json.Decode<AudioFormat>(payload[1..]) is { } f ? new Format(f) : null;
            case TypePackets:
                if (payload.Length > MaxPacketsPayload || payload.Length < AudioPackets.HeaderBytes) return null;
                int epoch = BinaryPrimitives.ReadUInt16BigEndian(payload[1..]);
                uint seq = BinaryPrimitives.ReadUInt32BigEndian(payload[3..]);
                byte flags = payload[7];
                int count = payload[8];
                if (count < 1) return null;
                var packets = new List<byte[]>(count);
                int o = AudioPackets.HeaderBytes;
                for (int i = 0; i < count; i++)
                {
                    if (o + 2 > payload.Length) return null;
                    int len = BinaryPrimitives.ReadUInt16BigEndian(payload[o..]);
                    o += 2;
                    if (o + len > payload.Length) return null;
                    packets.Add(payload.Slice(o, len).ToArray());
                    o += len;
                }
                if (o != payload.Length) return null;
                return new Packets(new AudioPackets(epoch, seq, (flags & 1) == 1, packets));
            default:
                return null;
        }
    }
}
