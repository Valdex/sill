namespace Sill.Windows.Audio;

/// <summary>The Mac's sound waiting to be played: interleaved signed 16-bit frames in from the network
/// (10 ms a packet, in bursts when Wi-Fi holds them), out to the sound card at its own steady pace.
/// The policy keeps it short and never lets it drift behind the picture:
/// <list type="bullet">
/// <item>It starts (and starts again after running dry) once <see cref="TargetFrames"/> (40 ms) are
///   in, playing silence until then, so a packet a little late finds audio still queued.</item>
/// <item>Past <see cref="MaxFrames"/> (60 ms) it drops its oldest audio, back to 40 ms: a burst after
///   a stall, or a Mac whose clock runs a little faster than the sound card's, would otherwise add
///   their delay for good. Old sound is dropped, never played late.</item>
/// <item>Run dry, it plays silence for what is missing and waits for 40 ms again (counted).</item>
/// </list>
/// So 40 to 60 ms wait here, and the sound card's few buffers (WaveOutDevice, 30 ms) after it.
/// Pure: one thread at a time (MacSound locks around it). Checked by Sill.Checks.</summary>
public sealed class JitterBuffer
{
    public const double TargetMs = 40, MaxMs = 60;

    public int SampleRate { get; }
    public int Channels { get; }
    public int FrameBytes => Channels * 2;
    public int TargetFrames { get; }
    public int MaxFrames { get; }

    readonly byte[] ring;
    int start, count;   // bytes

    /// <summary>Playing: primed, and not run dry since.</summary>
    public bool Playing { get; private set; }
    public int BufferedFrames => count / FrameBytes;
    public double BufferedMs => BufferedFrames * 1000.0 / SampleRate;
    /// <summary>Frames dropped as too old.</summary>
    public long DroppedFrames { get; private set; }
    /// <summary>Times it ran dry while playing.</summary>
    public long Underruns { get; private set; }

    public JitterBuffer(int sampleRate, int channels, double targetMs = TargetMs, double maxMs = MaxMs)
    {
        if (sampleRate <= 0 || channels <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        SampleRate = sampleRate;
        Channels = channels;
        TargetFrames = Math.Max(1, (int)Math.Round(sampleRate * targetMs / 1000));
        MaxFrames = Math.Max(TargetFrames, (int)Math.Round(sampleRate * maxMs / 1000));
        // Room for a burst well past the cap; whatever is beyond it is trimmed at once anyway.
        ring = new byte[(MaxFrames * 4 + sampleRate / 10) * FrameBytes];
    }

    /// <summary>Audio in, whole frames (a trailing part of a frame is ignored). Past the cap, the
    /// oldest goes, back to the target.</summary>
    public void Push(ReadOnlySpan<byte> pcm)
    {
        pcm = pcm[..(pcm.Length / FrameBytes * FrameBytes)];
        if (pcm.Length >= ring.Length)
        {
            // More than the ring holds: only its newest part could stay anyway.
            DroppedFrames += (count + pcm.Length - ring.Length) / FrameBytes;
            pcm = pcm[^ring.Length..];
            start = 0;
            count = 0;
        }
        else if (count + pcm.Length > ring.Length)
        {
            int over = count + pcm.Length - ring.Length;
            Skip(over);
            DroppedFrames += over / FrameBytes;
        }
        int end = (start + count) % ring.Length;
        int first = Math.Min(pcm.Length, ring.Length - end);
        pcm[..first].CopyTo(ring.AsSpan(end));
        pcm[first..].CopyTo(ring);
        count += pcm.Length;
        if (BufferedFrames > MaxFrames)
        {
            int drop = (BufferedFrames - TargetFrames) * FrameBytes;
            Skip(drop);
            DroppedFrames += drop / FrameBytes;
        }
    }

    /// <summary>Fills <paramref name="dest"/> (whole frames) for the sound card: buffered audio, or
    /// silence while it is not playing and for what is missing. Returns the frames of audio given.</summary>
    public int Pull(Span<byte> dest)
    {
        int frames = dest.Length / FrameBytes;
        if (!Playing)
        {
            if (BufferedFrames < TargetFrames) { dest.Clear(); return 0; }
            Playing = true;
        }
        int have = Math.Min(frames, BufferedFrames);
        int bytes = have * FrameBytes;
        int first = Math.Min(bytes, ring.Length - start);
        ring.AsSpan(start, first).CopyTo(dest);
        ring.AsSpan(0, bytes - first).CopyTo(dest[first..]);
        Skip(bytes);
        if (have < frames)
        {
            dest[bytes..].Clear();
            Underruns++;
            Playing = false;
        }
        return have;
    }

    public void Clear()
    {
        start = count = 0;
        Playing = false;
    }

    void Skip(int bytes)
    {
        start = (start + bytes) % ring.Length;
        count -= bytes;
        if (count == 0) start = 0;
    }
}
