using System.Buffers.Binary;
using Sill.Windows.Audio;
using Sill.Windows.Protocol;

namespace Sill.Checks;

// The Mac's sound as this client plays it: what of kind 43 is played (AudioReceiver), and the jitter
// buffer's policy (JitterBuffer) on a simulated clock, each frame numbered (left sample the low 16
// bits, right the high) so order, loss and repeats show.
static class AudioChecks
{
    const int Rate = 48_000, Packet = 480;

    static AudioFormat Pcm(int epoch = 0, int? priming = null) =>
        new() { Codec = AudioCodec.PcmS16LE, SampleRate = Rate, Channels = 2, FramesPerPacket = Packet, Epoch = epoch, PrimingFrames = priming };

    static AudioPackets Packets(int epoch, uint seq, bool segmentStart, params byte[][] packets) => new(epoch, seq, segmentStart, packets);

    /// <summary>Stereo frames numbered from <paramref name="next"/>.</summary>
    static byte[] Frames(ref int next, int count)
    {
        var b = new byte[count * 4];
        for (int i = 0; i < count; i++, next++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(i * 4), (ushort)next);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(i * 4 + 2), (ushort)(next >> 16));
        }
        return b;
    }

    static int Number(ReadOnlySpan<byte> b, int frame) =>
        BinaryPrimitives.ReadUInt16LittleEndian(b[(frame * 4)..]) | (BinaryPrimitives.ReadUInt16LittleEndian(b[(frame * 4 + 2)..]) << 16);

    public static void Receiver()
    {
        var r = new AudioReceiver();
        int n = 0;
        Check.Equal(0, r.Packets(Packets(0, 0, true, Frames(ref n, Packet))).Count, "packets before any format: nothing played");

        Check.That(!r.Apply(new AudioFormat { Codec = AudioCodec.AacEld, SampleRate = Rate, Channels = 2, FramesPerPacket = 480, Epoch = 0 }) && r.Shape == null,
                   "an AAC-ELD format: not played here (no change from nothing)");
        Check.Equal(0, r.Packets(Packets(0, 0, true, new byte[170])).Count, "its packets: nothing played");

        Check.That(r.Apply(Pcm()) && r.Shape == new PcmShape(Rate, 2, Packet), "a pcm-s16le format: played as 48 kHz stereo, 480 frames a packet (the card opens)");
        Check.That(!r.Apply(Pcm(epoch: 1)), "a new epoch of the same shape: no change (the card and what waits stay)");
        n = 0;
        var played = r.Packets(Packets(1, 0, true, Frames(ref n, Packet), Frames(ref n, Packet)));
        Check.That(played.Count == 2 && played[0].Length == Packet * 4 && Number(played[1], 0) == Packet, "the epoch's packets: both played, in order");
        Check.Equal(0, r.Packets(Packets(0, 2, false, Frames(ref n, Packet))).Count, "packets of an epoch whose format has not come: not played");
        Check.Equal(1L, r.Refused, "… and counted");
        Check.Equal(0, r.Packets(Packets(1, 2, false, new byte[Packet * 4 + 1])).Count, "a packet that is not whole frames: refused");
        Check.Equal(0L, r.Gaps, "no gap so far");
        r.Packets(Packets(1, 5, false, Frames(ref n, Packet)));
        Check.Equal(1L, r.Gaps, "a packet that does not follow the one before: a gap counted");

        var p = new AudioReceiver();
        p.Apply(Pcm(priming: 600));
        n = 0;
        Check.Equal(0, p.Packets(Packets(0, 0, true, Frames(ref n, Packet))).Count, "a segment's first 600 frames (primingFrames) dropped: the whole first packet");
        var rest = p.Packets(Packets(0, 1, false, Frames(ref n, Packet)));
        Check.That(rest.Count == 1 && rest[0].Length == 360 * 4 && Number(rest[0], 0) == 600, "… and the first 120 of the next: the segment's own audio from frame 600");
        Check.That(p.Packets(Packets(0, 2, false, Frames(ref n, Packet))).Single().Length == Packet * 4, "after that, whole packets");

        var wrap = new AudioReceiver();
        wrap.Apply(Pcm(epoch: 65_536 + 3));
        Check.Equal(1, wrap.Packets(Packets(3, 0, true, new byte[8])).Count, "the format's epoch is matched modulo 65,536, as the packets carry it");

        Check.Equal(new PcmShape(96_000, 2, 960), AudioReceiver.Playable(new AudioFormat { Codec = "pcm-s16le", SampleRate = 96_000, Channels = 2, FramesPerPacket = 960 }), "96 kHz plays");
        Check.Equal(new PcmShape(44_100, 1, 441), AudioReceiver.Playable(new AudioFormat { Codec = "pcm-s16le", SampleRate = 44_100, Channels = 1 }), "no packet size: 10 ms (441 at 44.1 kHz), mono plays");
        Check.Equal(new PcmShape(Rate, 2, 480), AudioReceiver.Playable(new AudioFormat { Codec = "pcm-s16le", SampleRate = Rate, Channels = 2, FramesPerPacket = 48_000 }), "a packet size past 100 ms: 10 ms");
        Check.Equal(null, AudioReceiver.Playable(new AudioFormat { Codec = "pcm-s16le", SampleRate = 4_000, Channels = 2 }), "4 kHz: not played");
        Check.Equal(null, AudioReceiver.Playable(new AudioFormat { Codec = "pcm-s16le", SampleRate = Rate, Channels = 6 }), "six channels: not played (plain PCM to the card is mono or stereo)");
        Check.Equal(null, AudioReceiver.Playable(new AudioFormat { SampleRate = Rate, Channels = 2 }), "no codec: not played");
        r.Reset();
        Check.That(r.Shape == null && r.Packets(Packets(1, 0, true, new byte[8])).Count == 0, "the session's end: nothing plays until the next format");

        // A whole message through the parser and the receiver into the buffer, as the client takes it.
        var e2e = new AudioReceiver();
        var buffer = new JitterBuffer(Rate, 2);
        e2e.Apply(((AudioMessage.Format)AudioMessage.Parse(AudioMessage.Serialize(Pcm()))!).Value);
        n = 0;
        for (uint seq = 0; seq < 4; seq++)
        {
            var bytes = new AudioPackets(0, seq, seq == 0, new[] { Frames(ref n, Packet) }).Serialize();
            foreach (var pcm in e2e.Packets(((AudioMessage.Packets)AudioMessage.Parse(bytes)!).Value)) buffer.Push(pcm);
        }
        var out1 = new byte[Packet * 4];
        Check.That(buffer.Pull(out1) == Packet && Number(out1, 0) == 0 && Number(out1, Packet - 1) == Packet - 1, "four packets through the parser and the receiver: 40 ms queued, played from the first frame");
    }

    public static void Buffer()
    {
        var b = new JitterBuffer(Rate, 2);
        Check.That(b.TargetFrames == 1920 && b.MaxFrames == 2880 && b.FrameBytes == 4, "48 kHz stereo: starts at 40 ms (1,920 frames), drops past 60 ms (2,880)");
        Check.Equal(1764, new JitterBuffer(44_100, 1).TargetFrames, "44.1 kHz mono: 40 ms is 1,764 frames");

        // Priming.
        int n = 0;
        var dest = new byte[Packet * 4];
        for (int i = 0; i < 3; i++) b.Push(Frames(ref n, Packet));
        Array.Fill(dest, (byte)0x55);
        Check.That(b.Pull(dest) == 0 && dest.All(x => x == 0) && !b.Playing, "30 ms in: silence, not yet playing");
        b.Push(Frames(ref n, Packet));
        Check.That(b.Pull(dest) == Packet && b.Playing && Number(dest, 0) == 0 && Number(dest, Packet - 1) == Packet - 1, "40 ms in: plays, from the first frame");

        // Steady: a packet in, a packet out, a minute.
        var s = new JitterBuffer(Rate, 2);
        n = 0;
        int expect = 0, minBuffered = int.MaxValue, maxBuffered = 0;
        bool inOrder = true;
        for (int i = 0; i < 4; i++) s.Push(Frames(ref n, Packet));
        for (int t = 0; t < 6000; t++)
        {
            int got = s.Pull(dest);
            for (int f = 0; f < got; f++) if (Number(dest, f) != expect++) inOrder = false;
            minBuffered = Math.Min(minBuffered, s.BufferedFrames);
            s.Push(Frames(ref n, Packet));
            maxBuffered = Math.Max(maxBuffered, s.BufferedFrames);
        }
        Check.That(s.Underruns == 0 && s.DroppedFrames == 0 && inOrder, "a steady minute: nothing dropped, never dry, every frame once and in order");
        Check.That(minBuffered == 1440 && maxBuffered == 1920, $"… 30 to 40 ms waiting throughout ({minBuffered} to {maxBuffered} frames)");

        // A stall, then the burst it held back: the old audio goes, not played late.
        int lastPushed = n - 1;
        int dryPulls = 0;
        for (int t = 0; t < 20; t++) if (s.Pull(dest) == 0 && dest.All(x => x == 0)) dryPulls++;
        Check.That(s.Underruns == 1 && !s.Playing && dryPulls >= 16, $"200 ms with nothing: it runs dry once, then silence ({dryPulls} silent pulls)");
        for (int t = 0; t < 20; t++) s.Push(Frames(ref n, Packet));
        int kept = s.BufferedFrames;
        Check.That(kept >= s.TargetFrames && kept <= s.MaxFrames, $"the 200 ms burst, packet by packet: cut back each time it passes 60 ms, 40 to 60 ms left ({kept} frames)");
        Check.Equal((long)(20 * Packet - kept), s.DroppedFrames, "… the rest dropped, oldest first");
        s.Pull(dest);
        Check.Equal(n - kept, Number(dest, 0), "… and what plays next is the newest audio kept");
        Check.That(lastPushed < n - kept, "never the audio from before the stall");

        // Clocks that disagree: the Mac 0.5 % fast, then 0.5 % slow, ten minutes each.
        foreach (var (name, ratio) in new[] { ("fast", 1.005), ("slow", 0.995) })
        {
            var d = new JitterBuffer(Rate, 2);
            n = 0;
            double owed = 0;
            int highest = 0, last = -1, repeats = 0;
            bool lateStart = false;
            for (int t = 0; t < 60_000; t++)
            {
                owed += Packet * ratio;
                int frames = (int)owed;
                owed -= frames;
                d.Push(Frames(ref n, frames));
                highest = Math.Max(highest, d.BufferedFrames);
                bool wasPlaying = d.Playing;
                int buffered = d.BufferedFrames;
                int got = d.Pull(dest);
                if (!wasPlaying && got > 0 && buffered < d.TargetFrames) lateStart = true;
                for (int f = 0; f < got; f++) { int k = Number(dest, f); if (k <= last) repeats++; last = k; }
            }
            Check.That(highest <= d.MaxFrames, $"the Mac {name}: never more than 60 ms waiting ({highest} frames at most)");
            Check.That(repeats == 0 && !lateStart, $"the Mac {name}: no frame twice or out of order, and after running dry it starts again only at 40 ms");
            if (ratio > 1) Check.That(d.DroppedFrames > 0 && d.Underruns == 0, $"the Mac fast: old audio dropped now and then ({d.DroppedFrames} frames in 10 min), never dry");
            else Check.That(d.Underruns > 0 && d.DroppedFrames == 0, $"the Mac slow: dry now and then ({d.Underruns} times in 10 min), nothing dropped");
        }

        // Wi-Fi: each packet made every 10 ms and held back 0 to 30 ms on its way (one in a thousand 100 ms),
        // in order, as TCP keeps it: the same rate in, in bursts.
        var w = new JitterBuffer(Rate, 2);
        var random = new Random(43);
        n = 0;
        int wLast = -1, wRepeats = 0, wHighest = 0, made = 0;
        int arrives = 0;
        var inFlight = new Queue<int>();
        long played = 0;
        for (int t = 0; t < 30_000; t++)
        {
            int delay = random.Next(1000) == 0 ? 10 : random.Next(4);
            arrives = Math.Max(arrives, t + delay);
            inFlight.Enqueue(arrives);
            made++;
            while (inFlight.Count > 0 && inFlight.Peek() <= t) { inFlight.Dequeue(); w.Push(Frames(ref n, Packet)); wHighest = Math.Max(wHighest, w.BufferedFrames); }
            int got = w.Pull(dest);
            played += got;
            for (int f = 0; f < got; f++) { int k = Number(dest, f); if (k <= wLast) wRepeats++; wLast = k; }
        }
        Check.That(wHighest <= w.MaxFrames && wRepeats == 0, $"jittery arrival, 5 minutes: never over 60 ms, no frame twice ({w.Underruns} times dry, {w.DroppedFrames * 1000 / Rate} ms dropped)");
        Check.That(w.DroppedFrames * 100 < (long)made * Packet, $"… under 1 % of the sound dropped ({w.DroppedFrames * 100.0 / ((long)made * Packet):0.00} %), {played * 100.0 / ((long)made * Packet):0.0} % played");

        // Sizes that are not a packet's.
        var o = new JitterBuffer(Rate, 2);
        n = 0;
        for (int i = 0; i < 5; i++) o.Push(Frames(ref n, Packet));
        var odd = new byte[441 * 4];
        Check.That(o.Pull(odd) == 441 && Number(odd, 440) == 440 && o.Pull(odd) == 441 && Number(odd, 0) == 441, "pulls of 441 frames: in order across packets");
        o.Push(new byte[] { 1, 2, 3, 4, 5 });
        Check.Equal(5 * Packet - 882 + 1, o.BufferedFrames, "a trailing part of a frame is ignored");
        var huge = Frames(ref n, Rate * 2);
        o.Push(huge);
        Check.That(o.BufferedFrames == o.TargetFrames && o.Pull(dest) > 0 && Number(dest, 0) == n - o.TargetFrames, "two seconds at once (past the ring): only the newest 40 ms stay");
        o.Clear();
        Check.That(o.BufferedFrames == 0 && !o.Playing && o.Pull(dest) == 0, "cleared: empty, silent until 40 ms come again");
    }
}
