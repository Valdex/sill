using System.Collections.Concurrent;
using System.Diagnostics;
using Sill.Windows.Interop;
using Sill.Windows.Protocol;

namespace Sill.Windows.Video;

/// <summary>The video thread: parameter sets and frames in from the network, decoded and drawn into
/// one window as soon as each completes. Owns the Direct3D device, the decoder and the renderer;
/// every other thread only queues work. A queue that runs behind (the decoder slower than the
/// stream) is cut back to its newest keyframe, so lateness never piles up. A picture that has to
/// wait for a keyframe (frames before their parameter sets or while the decoder waits for one, the
/// decoder failing, the Direct3D device lost and made again) says so once (<see cref="Interrupted"/>),
/// and again when a picture is drawn (<see cref="PictureBack"/>): the session asks the Mac for a
/// keyframe in between (KeyframeRecovery), which a still window never sends by itself.</summary>
public sealed class VideoPipeline : IDisposable
{
    abstract record Work;
    sealed record SetsWork(ParameterSets Sets) : Work;
    sealed record FrameWork(byte[] Data, bool Key, double Timestamp, long ArrivedTicks) : Work;
    sealed record ResizeWork(int Width, int Height) : Work;
    sealed record RedrawWork : Work;
    sealed record ClearWork : Work;
    sealed record CaptureWork(Action<System.Drawing.Bitmap?> Done) : Work;
    sealed record CropWork(int Rows) : Work;
    sealed record StopWork : Work;

    readonly BlockingCollection<Work> queue = new(new ConcurrentQueue<Work>());
    readonly Thread thread;
    readonly IntPtr hwnd;
    int width, height;
    int framesQueued;
    // A Coherence window's: the picture fills the window, its Mac title bar's rows left out.
    readonly bool fill;
    int cropTop;
    // The picture waits for a keyframe (video thread): Interrupted said so, PictureBack not yet.
    bool waiting;

    /// <summary>The picture's size (the SPS's), on the video thread when it changes.</summary>
    public event Action<int, int>? PictureSizeChanged;
    /// <summary>The first picture after the start or a Clear is on screen (video thread).</summary>
    public event Action? PictureShown;
    /// <summary>A line for the user: no decoder, or the decoder failing.</summary>
    public event Action<string>? Problem;
    /// <summary>The picture waits for a keyframe, and why (video thread); once until <see cref="PictureBack"/>.</summary>
    public event Action<string>? Interrupted;
    /// <summary>A picture was drawn after <see cref="Interrupted"/> (video thread).</summary>
    public event Action? PictureBack;

    // Counters the session reads once a second (any thread).
    long decoded, dropped;
    public long Decoded => Interlocked.Read(ref decoded);
    public long Dropped => Interlocked.Read(ref dropped);
    public string DecoderName { get; private set; } = "";
    /// <summary>Arrival to on screen, the last frame's, in ms.</summary>
    public double LastLatencyMs { get; private set; }

    public VideoPipeline(IntPtr hwnd, int width, int height) : this(hwnd, width, height, fill: false, "Sill video") { }

    /// <summary>A pipeline whose picture fills its window (<paramref name="fill"/>: a Coherence window,
    /// one pipeline and one swap chain per Mac window), its thread named for the log.</summary>
    public VideoPipeline(IntPtr hwnd, int width, int height, bool fill, string name)
    {
        this.hwnd = hwnd; this.width = width; this.height = height; this.fill = fill;
        thread = new Thread(Run) { IsBackground = true, Name = name, Priority = ThreadPriority.AboveNormal };
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
    /// <summary>Rows at the top of the picture to leave out (a Coherence window's Mac title bar).</summary>
    public void SetCrop(int rows) => queue.Add(new CropWork(Math.Max(0, rows)));

    void Run()
    {
        Com.CoInitializeEx(IntPtr.Zero, Com.COINIT_MULTITHREADED);
        IntPtr device = IntPtr.Zero, context = IntPtr.Zero;
        bool video = false;
        Renderer? renderer = null;
        HevcDecoder? decoder = null;
        ParameterSets? sets = null;
        (int w, int h)? picture = null;
        bool needKey = true;
        bool decoderFailed = false;
        bool shown = false;
        // Devices lost since the last picture drawn: past a few, the video stops with its reason.
        int losses = 0;

        void Build()
        {
            (device, context, video) = D3D.CreateDevice();
            renderer = new Renderer(device, context, hwnd, width, height)
            {
                PictureWidth = picture?.w ?? 0, PictureHeight = picture?.h ?? 0, Fill = fill, CropTop = cropTop,
            };
            renderer.Draw();
        }

        void TearDown()
        {
            decoder?.Dispose();
            decoder = null;
            renderer?.Dispose();
            renderer = null;
            Com.Release(ref context);
            Com.Release(ref device);
        }

        // A decoder for the current parameter sets: on the GPU when the device decodes video, else in software.
        HevcDecoder? MakeDecoder()
        {
            var size = sets != null && Hevc.Sps(sets) is { } sps ? Hevc.PictureSize(sps) : null;
            var made = HevcDecoder.Create(video ? device : IntPtr.Zero, size?.width ?? 0, size?.height ?? 0);
            if (made == null && video) made = HevcDecoder.Create(IntPtr.Zero, size?.width ?? 0, size?.height ?? 0);
            return made;
        }

        void Interrupt(string why)
        {
            if (waiting) return;
            waiting = true;
            Interrupted?.Invoke(why);
        }

        try
        {
            Build();
            while (true)
            {
                var work = queue.Take();
                if (work is StopWork) return;
                try
                {
                    Handle(work, renderer!);
                }
                catch (DeviceLostException lost)
                {
                    // A driver update or reset, a TDR, the adapter gone: everything on the device is
                    // gone with it. Made again on whichever adapter is there now; the picture waits
                    // for a keyframe, which a still window never sends by itself.
                    if (++losses > 3) throw;
                    TearDown();
                    Build();
                    decoderFailed = false;
                    needKey = true;
                    if (sets != null)
                    {
                        decoder = MakeDecoder();
                        DecoderName = decoder == null ? "" : decoder.Name + (decoder.OnGpu ? " (GPU)" : " (software)");
                        Interrupt($"the Direct3D device was lost (0x{lost.HResult32:X8})");
                    }
                }
            }
        }
        catch (Exception e)
        {
            Problem?.Invoke($"Video stopped: {e.Message}");
        }
        finally
        {
            TearDown();
        }

        void Handle(Work work, Renderer renderer)
        {
            switch (work)
            {
                case ResizeWork r:
                    // Only the newest resize matters.
                    width = r.Width; height = r.Height;
                    renderer.Resize(r.Width, r.Height);
                    renderer.Draw();
                    break;
                case RedrawWork:
                    renderer.Draw();
                    break;
                case CropWork c:
                    if (c.Rows == cropTop) break;
                    cropTop = c.Rows;
                    renderer.CropTop = c.Rows;
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
                    // A new session: its parameter sets start a decoder afresh, and its first picture is
                    // announced; its own recovery starts with nothing open.
                    renderer.ClearPicture();
                    renderer.Draw();
                    needKey = true;
                    sets = null;
                    picture = null;
                    shown = false;
                    waiting = false;
                    break;
                case SetsWork s:
                    if (s.Sets.SameAs(sets)) break;
                    sets = s.Sets;
                    var size = Hevc.Sps(sets) is { } sps ? Hevc.PictureSize(sps) : null;
                    decoder?.Dispose();
                    decoder = MakeDecoder();
                    DecoderName = decoder == null ? "" : decoder.Name + (decoder.OnGpu ? " (GPU)" : " (software)");
                    if (decoder == null) Problem?.Invoke(HevcDecoder.Problem ?? "No HEVC decoder.");
                    decoderFailed = false;
                    // The Mac sends the sets before each keyframe: the keyframe after them is no interruption.
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
                    if (sets == null)
                    {
                        // Frames whose parameter sets this session never had: the next keyframe brings them.
                        Interlocked.Increment(ref dropped);
                        Interrupt("frames came before their parameter sets");
                        break;
                    }
                    // No decoder on this PC (the HEVC Video Extensions missing): a keyframe would not help.
                    if (decoder == null) { Interlocked.Increment(ref dropped); break; }
                    if (needKey && !f.Key)
                    {
                        Interlocked.Increment(ref dropped);
                        Interrupt("frames came while the decoder waited for a keyframe");
                        break;
                    }
                    // Behind: skip to the newest keyframe still queued, if there is one.
                    if (left > 4 && !f.Key && NewestKeyQueued()) { Interlocked.Increment(ref dropped); break; }
                    if (decoderFailed)
                    {
                        if (!f.Key) { Interlocked.Increment(ref dropped); break; }
                        decoder.Dispose();
                        decoder = MakeDecoder();
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
                        Interrupt("the decoder stopped");
                        break;
                    }
                    if (drew)
                    {
                        Interlocked.Increment(ref decoded);
                        losses = 0;
                        if (!shown) { shown = true; PictureShown?.Invoke(); }
                        if (waiting) { waiting = false; PictureBack?.Invoke(); }
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
