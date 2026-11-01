using Sill.Windows.Protocol;

namespace Sill.Windows.Audio;

/// <summary>The Mac's sound on this PC (kind 43, `pcm-s16le`): each message from the session's reader
/// (<see cref="Receive"/>), through <see cref="AudioReceiver"/> into a <see cref="JitterBuffer"/>,
/// played by a <see cref="WaveOutDevice"/> opened for the format's shape. A format of another shape
/// opens the card again; one this client does not play (AAC-ELD) closes it; the session's end
/// (<see cref="Stop"/>) closes it and forgets the format. Muted, the card is closed and what comes is
/// dropped; unmuted, it opens again for the format in force. One per stream window, across its
/// sessions; any thread.</summary>
public sealed class MacSound : IDisposable
{
    // The receiver, the buffer and the flags: the reader's thread, the sound thread's pulls, the UI's mute.
    readonly object gate = new();
    // Opening and closing the card, one at a time, never under `gate`: closing joins the sound thread,
    // which may be waiting for `gate` in its pull.
    readonly object cardGate = new();
    readonly AudioReceiver receiver = new();
    JitterBuffer? buffer;
    WaveOutDevice? device;
    bool muted;
    PcmShape? opened;

    /// <summary>A line for the log when something changes (the card opened, closed or failed).</summary>
    public event Action<string>? Note;

    public bool Muted
    {
        get { lock (gate) return muted; }
        set
        {
            lock (gate)
            {
                if (muted == value) return;
                muted = value;
            }
            Refresh();
        }
    }

    /// <summary>A kind 43 payload, on the session's reader thread.</summary>
    public void Receive(ReadOnlySpan<byte> payload)
    {
        switch (AudioMessage.Parse(payload))
        {
            case AudioMessage.Format f:
                bool changed, played;
                lock (gate)
                {
                    changed = receiver.Apply(f.Value);
                    played = receiver.Shape != null;
                }
                Note?.Invoke($"sound: format {f.Value.Codec ?? "?"} {f.Value.SampleRate} Hz ×{f.Value.Channels}, {f.Value.FramesPerPacket} frames a packet, epoch {f.Value.Epoch}"
                             + (played ? "" : " (not played here)"));
                if (changed) Refresh();
                break;
            case AudioMessage.Packets p:
                lock (gate)
                {
                    if (buffer == null || muted) return;
                    foreach (var pcm in receiver.Packets(p.Value)) buffer.Push(pcm);
                }
                break;
        }
    }

    /// <summary>The session ended (or a new one starts): nothing plays until the next format.</summary>
    public void Stop()
    {
        lock (gate) receiver.Reset();
        Refresh();
    }

    /// <summary>For the log: the shape, how much waits, and what was dropped or ran dry.</summary>
    public string? Figures
    {
        get
        {
            lock (gate)
            {
                if (buffer is not { } b) return null;
                return $"sound {b.SampleRate} Hz ×{b.Channels}, {b.BufferedMs:0} ms buffered, {b.Underruns} ran dry, {b.DroppedFrames * 1000.0 / b.SampleRate:0} ms dropped, {receiver.Gaps} gaps";
            }
        }
    }

    /// <summary>The card as the format and the mute say: opened for the shape in force, or closed.</summary>
    void Refresh()
    {
        lock (cardGate)
        {
            PcmShape? want;
            lock (gate) want = muted ? null : receiver.Shape;
            if (want == opened && (want == null || device != null)) return;
            WaveOutDevice? old;
            lock (gate)
            {
                old = device;
                device = null;
                buffer = null;
            }
            old?.Dispose();
            opened = want;
            if (want is not { } shape)
            {
                if (old != null) Note?.Invoke("sound: stopped");
                return;
            }
            lock (gate) buffer = new JitterBuffer(shape.Rate, shape.Channels);
            var next = WaveOutDevice.Open(shape, Pull);
            lock (gate)
            {
                device = next;
                if (next == null) buffer = null;
            }
            Note?.Invoke(next != null ? $"sound: playing {shape.Rate} Hz ×{shape.Channels}, {shape.FramesPerPacket} frames a buffer" : $"sound: {WaveOutDevice.Problem}");
        }
    }

    void Pull(Span<byte> dest)
    {
        lock (gate)
        {
            if (buffer == null) dest.Clear();
            else buffer.Pull(dest);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            receiver.Reset();
            muted = true;
        }
        Refresh();
    }
}
