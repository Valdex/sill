using Sill.Windows.Protocol;

namespace Sill.Windows.Audio;

/// <summary>What the sound card is opened for: the rate, the channels, and the frames a packet carries
/// (one sound card buffer each).</summary>
public readonly record struct PcmShape(int Rate, int Channels, int FramesPerPacket)
{
    public int FrameBytes => Channels * 2;
}

/// <summary>Kind 43 as this client plays it: the format (type 1) says what the packets (type 2) are,
/// and only `pcm-s16le` is played (docs/coherence-protocol.md: interleaved signed 16-bit little-endian
/// frames, which the sound card takes as they are). Packets of an epoch whose format has not come are
/// not played (a format comes before each epoch's first packet); a segment's first frames that are the
/// codec's own (`primingFrames`, none for PCM) are dropped; a packet that is not whole frames is
/// refused. Anything else (AAC-ELD, a rate or a channel count the sound card is not opened for here)
/// plays nothing. Pure: checked by Sill.Checks.</summary>
public sealed class AudioReceiver
{
    AudioFormat? format;
    int primingLeft;
    uint? nextSeq;

    /// <summary>What the current format plays as; null for none, or one this client does not play.</summary>
    public PcmShape? Shape { get; private set; }
    /// <summary>Packets that did not follow the one before (a gap in `seq`).</summary>
    public long Gaps { get; private set; }
    /// <summary>Packets refused: of another epoch, or not whole frames.</summary>
    public long Refused { get; private set; }

    /// <summary>The shape a format plays as: `pcm-s16le` at 8 to 192 kHz, mono or stereo (plain
    /// WAVE_FORMAT_PCM; more channels would need another format), its packet size or 10 ms.</summary>
    public static PcmShape? Playable(AudioFormat f)
    {
        if (f.Codec != AudioCodec.PcmS16LE || f.SampleRate is not (>= 8_000 and <= 192_000) || f.Channels is not (1 or 2)) return null;
        int rate = f.SampleRate.Value;
        int frames = f.FramesPerPacket is int n && n > 0 && n <= rate / 10 ? n : rate / 100;
        return new PcmShape(rate, f.Channels.Value, frames);
    }

    /// <summary>A new format (type 1). True when what is played changes (the rate, the channels, the
    /// packet size, or playable or not): the sound card is opened again. A new epoch of the same shape
    /// keeps it, and what is queued.</summary>
    public bool Apply(AudioFormat f)
    {
        format = f;
        nextSeq = null;
        primingLeft = 0;
        var shape = Playable(f);
        bool changed = shape != Shape;
        Shape = shape;
        return changed;
    }

    /// <summary>The PCM of a packets message (type 2) to play, in order; none when nothing plays.</summary>
    public List<byte[]> Packets(AudioPackets p)
    {
        var pcm = new List<byte[]>();
        if (Shape is not { } shape || format == null) return pcm;
        if (format.Epoch is int epoch && (epoch & 0xFFFF) != p.Epoch)
        {
            Refused += p.Packets.Count;
            return pcm;
        }
        if (nextSeq is uint expected && p.Seq != expected) Gaps++;
        nextSeq = p.Seq + (uint)p.Packets.Count;
        if (p.SegmentStart) primingLeft = Math.Max(0, format.PrimingFrames ?? 0);
        foreach (var packet in p.Packets)
        {
            if (packet.Length % shape.FrameBytes != 0) { Refused++; continue; }
            int skip = Math.Min(primingLeft, packet.Length / shape.FrameBytes);
            primingLeft -= skip;
            if (skip * shape.FrameBytes < packet.Length) pcm.Add(skip == 0 ? packet : packet[(skip * shape.FrameBytes)..]);
        }
        return pcm;
    }

    /// <summary>The session ended: nothing plays until the next format.</summary>
    public void Reset()
    {
        format = null;
        Shape = null;
        nextSeq = null;
        primingLeft = 0;
    }
}
