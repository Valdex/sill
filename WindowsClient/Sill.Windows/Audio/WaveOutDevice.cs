using System.Runtime.InteropServices;
using Sill.Windows.Interop;

namespace Sill.Windows.Audio;

/// <summary>The sound card through waveOut: a small ring of buffers, each one packet long (10 ms at
/// 48 kHz), all of them queued, and a thread that refills each as the card finishes it from
/// <see cref="Pull"/> (the jitter buffer, silence when it has nothing). Windows signals an event as
/// each buffer is done (CALLBACK_EVENT), so the thread sleeps between them and needs no timer.</summary>
public sealed unsafe class WaveOutDevice : IDisposable
{
    /// <summary>Fills one buffer, whole frames. On the sound thread.</summary>
    public delegate void Pull(Span<byte> dest);

    /// <summary>Buffers queued on the card: 3 of 10 ms, enough for a thread woken late now and then.</summary>
    public const int Buffers = 3;

    readonly IntPtr handle;
    readonly AutoResetEvent done;
    readonly WinMM.WAVEHDR*[] headers;
    readonly bool[] queued;
    readonly int bufferBytes;
    readonly Pull pull;
    readonly Thread thread;
    volatile bool stopping;
    int disposed;

    /// <summary>Why the last open failed.</summary>
    public static string? Problem { get; private set; }

    WaveOutDevice(IntPtr handle, AutoResetEvent done, int bufferBytes, int buffers, Pull pull)
    {
        this.handle = handle;
        this.done = done;
        this.bufferBytes = bufferBytes;
        this.pull = pull;
        headers = new WinMM.WAVEHDR*[buffers];
        queued = new bool[buffers];
        for (int i = 0; i < buffers; i++)
        {
            var h = (WinMM.WAVEHDR*)NativeMemory.AllocZeroed((nuint)sizeof(WinMM.WAVEHDR));
            h->lpData = (IntPtr)NativeMemory.AllocZeroed((nuint)bufferBytes);
            h->dwBufferLength = (uint)bufferBytes;
            WinMM.waveOutPrepareHeader(handle, h, (uint)sizeof(WinMM.WAVEHDR));
            headers[i] = h;
        }
        thread = new Thread(Run) { IsBackground = true, Name = "Sill sound", Priority = ThreadPriority.Highest };
        thread.Start();
    }

    /// <summary>The default sound device opened for 16-bit PCM of this shape, playing at once; null
    /// (and <see cref="Problem"/>) when Windows has none to give.</summary>
    public static WaveOutDevice? Open(PcmShape shape, Pull pull, int buffers = Buffers)
    {
        var format = new WinMM.WAVEFORMATEX
        {
            wFormatTag = WinMM.WAVE_FORMAT_PCM, nChannels = (ushort)shape.Channels, nSamplesPerSec = (uint)shape.Rate,
            nAvgBytesPerSec = (uint)(shape.Rate * shape.FrameBytes), nBlockAlign = (ushort)shape.FrameBytes, wBitsPerSample = 16, cbSize = 0,
        };
        var signal = new AutoResetEvent(false);
        uint r = WinMM.waveOutOpen(out var handle, WinMM.WAVE_MAPPER, format, signal.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, WinMM.CALLBACK_EVENT);
        if (r != WinMM.MMSYSERR_NOERROR)
        {
            signal.Dispose();
            Problem = $"no sound device took {shape.Rate} Hz {(shape.Channels == 2 ? "stereo" : "mono")} (waveOutOpen {r})";
            return null;
        }
        return new WaveOutDevice(handle, signal, shape.FramesPerPacket * shape.FrameBytes, buffers, pull);
    }

    void Run()
    {
        // Each buffer in turn: filled and queued when it is free (never queued yet, or done playing).
        int next = 0;
        while (!stopping)
        {
            var h = headers[next];
            if (!queued[next] || (Volatile.Read(ref h->dwFlags) & WinMM.WHDR_DONE) != 0)
            {
                pull(new Span<byte>((void*)h->lpData, bufferBytes));
                h->dwFlags &= ~WinMM.WHDR_DONE;
                if (WinMM.waveOutWrite(handle, h, (uint)sizeof(WinMM.WAVEHDR)) != WinMM.MMSYSERR_NOERROR) return;
                queued[next] = true;
                next = (next + 1) % headers.Length;
                continue;
            }
            done.WaitOne(50);
        }
    }

    /// <summary>Stops at once (what is queued is dropped) and lets go of the device. Not from the sound thread.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stopping = true;
        done.Set();
        thread.Join(1000);
        WinMM.waveOutReset(handle);   // every queued buffer comes back done
        foreach (var h in headers)
        {
            WinMM.waveOutUnprepareHeader(handle, h, (uint)sizeof(WinMM.WAVEHDR));
            NativeMemory.Free((void*)h->lpData);
            NativeMemory.Free(h);
        }
        WinMM.waveOutClose(handle);
        done.Dispose();
    }
}
