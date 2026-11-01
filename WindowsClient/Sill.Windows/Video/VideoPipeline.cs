using System.Collections.Concurrent;
using System.Diagnostics;
using Sill.Windows.Interop;
using Sill.Windows.Protocol;

namespace Sill.Windows.Video;

/// <summary>The video thread: parameter sets and frames in from the network, decoded and drawn into
/// one window as soon as each completes. Owns the Direct3D device, the decoder and the renderer;
/// every other thread only queues work. A queue that runs behind (the decoder slower than the
/// stream) is cut back to its newest keyframe, so lateness never piles up.</summary>
public sealed class VideoPipeline : IDisposable
{
    abstract record Work;
    sealed record SetsWork(ParameterSets Sets) : Work;
    sealed record FrameWork(byte[] Data, bool Key, double Timestamp, long ArrivedTicks) : Work;
    sealed record ResizeWork(int Width, int Height) : Work;
    sealed record RedrawWork : Work;
    sealed record ClearWork : Work;
    sealed record CaptureWork(Action<System.Drawing.Bitmap?> Done) : Work;
    sealed record StopWork : Work;

    readonly BlockingCollection<Work> queue = new(new ConcurrentQueue<Work>());
    readonly Thread thread;
    readonly IntPtr hwnd;
    int width, height;
    int framesQueued;

    /// <summary>The picture's size (the SPS's), on the video thread when it changes.</summary>
    public event Action<int, int>? PictureSizeChanged;
    /// <summary>The first picture after the start or a Clear is on screen (video thread).</summary>
    public event Action? PictureShown;
    /// <summary>A line for the user: no decoder, or the decoder failing.</summary>
    public event Action<string>? Problem;

    // Counters the session reads once a second (any thread).
    long decoded, dropped;
    public long Decoded => Interlocked.Read(ref decoded);
    public long Dropped => Interlocked.Read(ref dropped);
    public string DecoderName { get; private set; } = "";
    /// <summary>Arrival to on screen, the last frame's, in ms.</summary>
    public double LastLatencyMs { get; private set; }

    public VideoPipeline(IntPtr hwnd, int width, int height)
    {
        this.hwnd = hwnd; this.width = width; this.height = height;
        thread = new Thread(Run) { IsBackground = true, Name = "Sill video", Priority = ThreadPriority.AboveNormal };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    public void Submit(ParameterSets sets) => queue.Add(new SetsWork(sets));

    public void Submit(byte[] frame, bool key, double timestamp)
    {
        Interlocked.Increment(ref framesQueued);
        queue.Add(new FrameWork(frame, key, timestamp, Stopwatch.GetTimestamp()));
    }

    public void Resize(int w, int h) => queue.Add(new ResizeWork(w, h));
    public void Redraw() => queue.Add(new RedrawWork());
    /// <summary>Nothing streams: black until the next picture.</summary>
    public void Clear() => queue.Add(new ClearWork());
    /// <summary>The picture on screen at its own size, handed to <paramref name="done"/> on the video
    /// thread (null before the first picture, or if the GPU could not read it back).</summary>
    public void Capture(Action<System.Drawing.Bitmap?> done) => queue.Add(new CaptureWork(done));

    void Run()
    {
        Com.CoInitializeEx(IntPtr.Zero, Com.COINIT_MULTITHREADED);
        IntPtr device = IntPtr.Zero, context = IntPtr.Zero;
        Renderer? renderer = null;
        HevcDecoder? decoder = null;
        ParameterSets? sets = null;
        (int w, int h)? picture = null;
        bool needKey = true;
        bool decoderFailed = false;
        bool shown = false;
        try
        {
            (device, context, bool video) = D3D.CreateDevice();
            renderer = new Renderer(device, context, hwnd, width, height);
            renderer.Draw();
            while (true)
            {
                var work = queue.Take();
                switch (work)
                {
                    case StopWork:
                        return;
                    case ResizeWork r:
                        // Only the newest resize matters.
                        renderer.Resize(r.Width, r.Height);
                        renderer.Draw();
                        break;
                    case RedrawWork:
                        renderer.Draw();
                        break;
                    case CaptureWork c:
                        System.Drawing.Bitmap? shot = null;
                        // A failed read-back is only no screenshot (the caller says so); the stream goes on.
                        try { shot = renderer.Capture(); }
                        catch (Exception e) when (e is ComException or ArgumentException or OutOfMemoryException) { }
                        c.Done(shot);
                        break;
                    case ClearWork:
                        // A new session: its parameter sets start a decoder afresh, and its first picture is announced.
                        renderer.ClearPicture();
                        renderer.Draw();
                        needKey = true;
                        sets = null;
                        picture = null;
                        shown = false;
                        break;
                    case SetsWork s:
                        if (s.Sets.SameAs(sets)) break;
                        sets = s.Sets;
                        var size = Hevc.Sps(sets) is { } sps ? Hevc.PictureSize(sps) : null;
                        decoder?.Dispose();
                        decoder = HevcDecoder.Create(video ? device : IntPtr.Zero, size?.width ?? 0, size?.height ?? 0);
                        if (decoder == null && video)
                            decoder = HevcDecoder.Create(IntPtr.Zero, size?.width ?? 0, size?.height ?? 0);
                        DecoderName = decoder == null ? "" : decoder.Name + (decoder.OnGpu ? " (GPU)" : " (software)");
                        if (decoder == null) Problem?.Invoke(HevcDecoder.Problem ?? "No HEVC decoder.");
                        decoderFailed = false;
                        needKey = true;
                        if (size != picture)
                        {
                            picture = size;
                            renderer.PictureWidth = size?.width ?? 0;
                            renderer.PictureHeight = size?.height ?? 0;
                            if (size is { } sz) PictureSizeChanged?.Invoke(sz.width, sz.height);
                        }
                        break;
                    case FrameWork f:
                        int left = Interlocked.Decrement(ref framesQueued);
                        if (decoder == null || sets == null) { Interlocked.Increment(ref dropped); break; }
                        if (needKey && !f.Key) { Interlocked.Increment(ref dropped); break; }
                        // Behind: skip to the newest keyframe still queued, if there is one.
                        if (left > 4 && !f.Key && NewestKeyQueued()) { Interlocked.Increment(ref dropped); break; }
                        if (decoderFailed)
                        {
                            if (!f.Key) { Interlocked.Increment(ref dropped); break; }
                            decoder.Dispose();
                            var sz2 = Hevc.Sps(sets) is { } sps2 ? Hevc.PictureSize(sps2) : null;
                            decoder = HevcDecoder.Create(video ? device : IntPtr.Zero, sz2?.width ?? 0, sz2?.height ?? 0)
                                      ?? HevcDecoder.Create(IntPtr.Zero, sz2?.width ?? 0, sz2?.height ?? 0);
                            if (decoder == null) { Problem?.Invoke(HevcDecoder.Problem ?? "No HEVC decoder."); break; }
                            decoderFailed = false;
                        }
                        needKey = false;
                        var annexB = Hevc.ToAnnexB(f.Data, sets.NalLengthSize, f.Key ? sets : null);
                        bool drew = false;
                        bool ok = decoder.Decode(annexB, frame =>
                        {
                            renderer.Upload(frame);
                            drew = true;
                        });
                        if (!ok)
                        {
                            decoderFailed = true;
                            needKey = true;
                            Problem?.Invoke($"The decoder stopped ({HevcDecoder.Problem}); waiting for the next keyframe.");
                            break;
                        }
                        if (drew)
                        {
                            Interlocked.Increment(ref decoded);
                            if (!shown) { shown = true; PictureShown?.Invoke(); }
                            // Draw only the newest: when more frames wait, they replace this one at once.
                            if (Volatile.Read(ref framesQueued) == 0)
                            {
                                renderer.WaitForSlot();
                                renderer.Draw();
                            }
                            LastLatencyMs = (Stopwatch.GetTimestamp() - f.ArrivedTicks) * 1000.0 / Stopwatch.Frequency;
                        }
                        break;
                }
            }
        }
        catch (Exception e)
        {
            Problem?.Invoke($"Video stopped: {e.Message}");
        }
        finally
        {
            decoder?.Dispose();
            renderer?.Dispose();
            Com.Release(context);
            Com.Release(device);
        }
    }

    bool NewestKeyQueued()
    {
        foreach (var w in queue)
            if (w is FrameWork { Key: true }) return true;
        return false;
    }

    public void Dispose()
    {
        queue.Add(new StopWork());
        thread.Join(2000);
    }
}
