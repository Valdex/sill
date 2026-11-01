using System.Runtime.InteropServices;
using Sill.Windows.Data;
using Sill.Windows.Net;
using Sill.Windows.Protocol;
using IComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Sill.Windows.DragDrop;

/// <summary>Drag and drop for any window that shows a Mac window's picture: the classic stream window
/// (its window ID null, the picked window) and each Coherence window (its Mac window's ID). It takes
/// the window's handle, a mapper from a point in the window's client area to fractions of the picture,
/// and the Mac window's ID, and does the rest:
/// <list type="bullet">
/// <item>this PC's files dragged over it (an IDropTarget registered on the handle): sent as kind 34
/// parts at once, the drag followed live (kind 38) to a host that says `liveDrag` 1, else one kind 35 at
/// the drop (LiveDragMachine); DROPEFFECT_COPY, or none to a Mac that takes no files;</item>
/// <item>the Mac's files dragged out of it (kind 36 while the button is held here): Windows' own drag with
/// them as virtual files once the pointer leaves the window (DragOutMachine, MacFilesDataObject).</item>
/// </list>
/// The window tells it about its left button (<see cref="ButtonDown"/>, <see cref="PointerMoved"/>,
/// <see cref="ButtonUp"/>), and hears <see cref="MacButtonReleased"/> when a drag out let the Mac's button
/// go (the window then forgets its own). Lives on the window's UI thread (an STA).</summary>
public sealed class DragDropHost : IDisposable
{
    const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, SM_SWAPBUTTON = 23;
    const ushort EscapeUsage = 0x29;
    static uint nextDrag = (uint)Random.Shared.Next(1, int.MaxValue);

    // The drag out under way (one at a time on a PC), so any Sill window it comes back over can tell it
    // is the Mac's own drag and follow it there; and where it was let go, when on one of them.
    static MacFilesDataObject? activeDragOut;
    static (double x, double y, uint? window)? selfDrop;

    readonly IntPtr hwnd;
    readonly Func<Point, (double x, double y)> toFraction;
    readonly Func<uint?> windowId;
    readonly SynchronizationContext? ui;
    readonly Target target;
    readonly DragOutMachine dragOut = new();
    readonly System.Windows.Forms.Timer heldPoll = new() { Interval = 100 };
    Session? session;
    FileService? files;
    bool registered, disposed;
    (double x, double y) last = (0.5, 0.5);

    /// <summary>A drag out let the Mac's left button go (or the drag of its files was dropped here).</summary>
    public event Action? MacButtonReleased;
    /// <summary>A line for the window to show (a drop refused, and why).</summary>
    public event Action<string>? Hint;

    public DragDropHost(IntPtr hwnd, Func<Point, (double x, double y)> toFraction, Func<uint?> windowId)
    {
        this.hwnd = hwnd;
        this.toFraction = toFraction;
        this.windowId = windowId;
        ui = SynchronizationContext.Current;
        Ole.OleInitialize(IntPtr.Zero);   // counted: S_FALSE when WinForms did it already
        target = new Target(this);
        int hr = Ole.RegisterDragDrop(hwnd, target);
        registered = hr >= 0;
        Log(registered ? "drop target registered" : $"RegisterDragDrop failed (0x{hr:X8})");
        heldPoll.Tick += (_, _) => PollHeld();
    }

    static void Log(string line) => TestArguments.Log($"drag: {line}");

    /// <summary>The session this window shows now (a reconnect brings a new one).</summary>
    public void Attach(Session? s)
    {
        if (files != null) files.DragOffered -= OnOffer;
        session = s;
        files = s == null ? null : FileService.Attach(s);
        if (files != null) files.DragOffered += OnOffer;
    }

    void OnOffer(DragOffer offer) => Post(() =>
    {
        dragOut.Offered(offer);
        if (offer.Id != 0) Log($"the Mac offers {offer.Files.Count} file(s) under the held button (offer {offer.Id})");
    });

    void Post(Action a)
    {
        if (ui != null) ui.Post(_ => { if (!disposed) a(); }, null);
        else a();
    }

    // MARK: the window's left button

    public void ButtonDown(Point client)
    {
        last = toFraction(client);
        dragOut.ButtonDown();
    }

    /// <summary>A move with or without the left button. True when it started a drag out (which ran
    /// to its end inside this call, as DoDragDrop does).</summary>
    public bool PointerMoved(Point client, bool leftHeld)
    {
        if (!leftHeld || disposed) return false;
        bool inside = Inside(client);
        if (inside) last = toFraction(client);
        if (dragOut.Pointer(inside) is not { Act: DragOutMachine.Act.StartDrag }) return false;
        RunDragOut();
        return true;
    }

    public void ButtonUp(Point client)
    {
        foreach (var step in dragOut.ButtonUp(Inside(client), last)) Perform(step);
    }

    bool Inside(Point client)
    {
        GetClientRect(hwnd, out var r);
        return client.X >= 0 && client.Y >= 0 && client.X < r.Right && client.Y < r.Bottom;
    }

    // MARK: the Mac's files out

    void RunDragOut()
    {
        var offer = dragOut.Offer;
        var service = files;
        if (offer == null || service == null) { dragOut.Ended(false, null, ButtonHeld(), last); return; }
        var obj = new MacFilesDataObject(service, offer.Id, offer.Files);
        Log($"dragging the Mac's {offer.Files.Count} file(s) out of the window");
        activeDragOut = obj;
        selfDrop = null;
        int hr, effect;
        try { hr = Ole.DoDragDrop(obj, new DropSource(), Ole.DROPEFFECT_COPY, out effect); }
        finally { activeDragOut = null; }
        var onSelf = selfDrop;
        selfDrop = null;
        bool dropped = hr == Ole.DRAGDROP_S_DROP && (effect != Ole.DROPEFFECT_NONE || onSelf != null);
        bool held = ButtonHeld();
        Log($"the drag out ended: {(dropped ? onSelf != null ? "dropped on a Sill window" : "dropped" : "cancelled")} (0x{hr:X8}, effect {effect}){(held ? ", the button still held" : "")}");
        foreach (var step in dragOut.Ended(dropped, onSelf, held, last)) Perform(step);
        if (dragOut.Now == DragOutMachine.State.HeldAfterCancel) heldPoll.Start();
    }

    /// <summary>After a drag out cancelled with the button held: when it comes up outside the window
    /// (no mouse-up reaches it there), the Mac's drag is given up.</summary>
    void PollHeld()
    {
        if (dragOut.Now != DragOutMachine.State.HeldAfterCancel) { heldPoll.Stop(); return; }
        if (ButtonHeld()) return;
        heldPoll.Stop();
        GetCursorPos(out var p);
        var pt = new Point(p.X, p.Y);
        ScreenToClient(hwnd, ref pt);
        if (Inside(pt)) return;   // the window's own up comes, and lands the drag there
        foreach (var step in dragOut.ButtonUp(false, last)) Perform(step);
    }

    static bool ButtonHeld() => (GetAsyncKeyState(GetSystemMetrics(SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON) & 0x8000) != 0;

    void Perform(DragOutMachine.Step step)
    {
        switch (step.Act)
        {
            case DragOutMachine.Act.Move:
                SendInput(InputEvents.Pointer(PointerAction.Move, step.X, step.Y), step.Window);
                break;
            case DragOutMachine.Act.Up:
                SendInput(InputEvents.Pointer(PointerAction.Move, step.X, step.Y), step.Window);
                SendInput(InputEvents.Pointer(PointerAction.LeftUp, step.X, step.Y), step.Window);
                MacButtonReleased?.Invoke();
                break;
            case DragOutMachine.Act.EscAndUp:
                // #66's viewer: Esc cancels the Mac's drag, then its button lets go where it is.
                SendInput(InputEvents.Key(EscapeUsage, true, 0), windowId());
                SendInput(InputEvents.Key(EscapeUsage, false, 0), windowId());
                SendInput(InputEvents.Pointer(PointerAction.LeftUp, step.X, step.Y), windowId());
                MacButtonReleased?.Invoke();
                break;
        }
    }

    void SendInput(byte[] json, uint? window)
    {
        var s = session;
        if (s == null) return;
        s.SendInput(window is { } w && (s.Windows?.WindowInput ?? 0) >= Capabilities.WindowInputEvery ? InputEvents.InWindow(json, w) : json);
    }

    (double x, double y) Fraction(long pt)
    {
        var (sx, sy) = Ole.Point(pt);
        var p = new Point(sx, sy);
        ScreenToClient(hwnd, ref p);
        return toFraction(p);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        heldPoll.Stop();
        heldPoll.Dispose();
        target.Stop();
        if (registered) Ole.RevokeDragDrop(hwnd);
        registered = false;
        if (files != null) files.DragOffered -= OnOffer;
    }

    // MARK: this PC's files in

    /// <summary>The window's IDropTarget: OLE calls it on the UI thread.</summary>
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class Target : IOleDropTarget
    {
        readonly DragDropHost host;
        readonly System.Windows.Forms.Timer keepAlive = new() { Interval = 1000 };
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        LiveDragMachine? live;
        UploadBatch? batch;
        FileService? service;
        IComDataObject? pendingVirtual;
        bool self, refused;
        int keys;
        (double x, double y) at;

        internal Target(DragDropHost host)
        {
            this.host = host;
            // OLE calls DragOver as the pointer moves; one resting over the window still has to be heard
            // by the host within its 10 s (LiveDragMachine.KeepAlive).
            keepAlive.Tick += (_, _) => { if (live != null && !live.Finished) Send(live.Move(host.windowId(), at.x, at.y, Mods(keys), Now)); };
        }

        double Now => clock.Elapsed.TotalSeconds;
        static ulong Mods(int keyState) => LiveDragMachine.Modifiers(keyState, Settings.Current.CtrlIsCommand);

        public int DragEnter(IComDataObject pDataObj, int grfKeyState, long pt, ref int pdwEffect)
        {
            at = host.Fraction(pt);
            keys = grfKeyState;
            // The Mac's own drag coming back over a window of its session: its pointer follows here.
            if (pDataObj is MacFilesDataObject own && own == activeDragOut && own.Service.Session == host.session)
            {
                self = true;
                Follow();
                pdwEffect = Ole.DROPEFFECT_COPY;
                return Ole.S_OK;
            }
            self = false;
            refused = false;
            service = host.files;
            if (service == null || !service.HostTakesFiles) return Refuse(ref pdwEffect, service == null ? null : $"{service.MacName} doesn’t take files (update Sill on the Mac).");
            List<string> paths;
            bool virtualFiles;
            try
            {
                paths = DataObjectReader.Paths(pDataObj);
                virtualFiles = paths.Count == 0 && DataObjectReader.HasVirtual(pDataObj);
            }
            catch (COMException) { return Refuse(ref pdwEffect, null); }
            if (paths.Count == 0 && !virtualFiles) return Refuse(ref pdwEffect, null);
            live = new LiveDragMachine(Interlocked.Increment(ref nextDrag), service.HostLiveDrag);
            Send(live.Enter(host.windowId(), at.x, at.y, Mods(grfKeyState), Now));
            if (paths.Count > 0) StartUpload(paths, null);
            else pendingVirtual = pDataObj;   // an app's virtual files are read at the drop, as such apps expect
            if (live.Cancelled) return Refuse(ref pdwEffect, null);
            keepAlive.Start();
            pdwEffect = Ole.DROPEFFECT_COPY;
            return Ole.S_OK;
        }

        public int DragOver(int grfKeyState, long pt, ref int pdwEffect)
        {
            at = host.Fraction(pt);
            keys = grfKeyState;
            if (self) { Follow(); pdwEffect = Ole.DROPEFFECT_COPY; return Ole.S_OK; }
            if (refused || live == null || live.Cancelled) { pdwEffect = Ole.DROPEFFECT_NONE; return Ole.S_OK; }
            Send(live.Move(host.windowId(), at.x, at.y, Mods(grfKeyState), Now));
            pdwEffect = Ole.DROPEFFECT_COPY;
            return Ole.S_OK;
        }

        public int DragLeave()
        {
            keepAlive.Stop();
            if (self) { self = false; return Ole.S_OK; }
            if (live != null)
            {
                Send(live.Leave());
                batch?.Cancel();
            }
            Reset();
            return Ole.S_OK;
        }

        public int Drop(IComDataObject pDataObj, int grfKeyState, long pt, ref int pdwEffect)
        {
            keepAlive.Stop();
            at = host.Fraction(pt);
            if (self)
            {
                self = false;
                selfDrop = (at.x, at.y, host.windowId());
                pdwEffect = Ole.DROPEFFECT_COPY;
                return Ole.S_OK;
            }
            if (refused || live == null || live.Cancelled) { Reset(); pdwEffect = Ole.DROPEFFECT_NONE; return Ole.S_OK; }
            Send(live.Drop(host.windowId(), at.x, at.y, Mods(grfKeyState)));
            if (pendingVirtual is { } d)
            {
                var got = DataObjectReader.Files(d, FileParts.MaxDrop, out var why);
                if (got is { } f) StartUpload(f.paths, f.temporary);
                else { Send(live.Failed()); host.Hint?.Invoke($"Couldn’t send those files: {why}."); }
            }
            pendingVirtual = null;
            pdwEffect = Ole.DROPEFFECT_COPY;
            // The machine lives on until its upload finishes (StartUpload's continuation sends the rest).
            live = null; batch = null; service = null;
            return Ole.S_OK;
        }

        void StartUpload(List<string> paths, HashSet<string>? temporary)
        {
            var machine = live!;
            var svc = service!;
            var b = svc.Upload(paths, out var refusal, temporary);
            if (b == null)
            {
                Send(machine.Failed(), svc);
                if (refusal != null) host.Hint?.Invoke(refusal);
                return;
            }
            batch = b;
            Log($"sending {b.Files.Count} dropped file(s), {b.TotalBytes:N0} bytes, {(svc.HostLiveDrag ? "followed live" : "dropped at the end")}");
            b.Completion.ContinueWith(t => host.Post(() =>
            {
                if (t.IsCompletedSuccessfully) Send(machine.Uploaded(b.Ids, b.FolderIds), svc);
                else
                {
                    Send(machine.Failed(), svc);
                    if (!t.IsCanceled) host.Hint?.Invoke($"Couldn’t send the files: {t.Exception?.InnerException?.Message}");
                }
            }), TaskScheduler.Default);
        }

        void Follow()
        {
            if (host.dragOut.OverSelf(at.x, at.y, host.windowId()) is { } step) host.Perform(step);
            else if (activeDragOut != null)
                host.SendInput(InputEvents.Pointer(PointerAction.Move, at.x, at.y), host.windowId());
        }

        void Send(List<LiveDragMachine.Out> outs, FileService? svc = null)
        {
            svc ??= service;
            if (svc == null) return;
            foreach (var o in outs)
            {
                if (o.Live != null) svc.Send(Kind.LiveDrag, o.Live);
                if (o.Drop != null) svc.Send(Kind.FileDrop, o.Drop);
                Log(o.Live != null ? $"live drag {o.Live.Id}: {o.Live.Phase}" : $"file drop of {o.Drop!.Files.Count} file(s)");
            }
        }

        int Refuse(ref int effect, string? why)
        {
            refused = true;
            effect = Ole.DROPEFFECT_NONE;
            if (why != null) host.Hint?.Invoke(why);
            return Ole.S_OK;
        }

        void Reset() { live = null; batch = null; service = null; pendingVirtual = null; refused = false; }

        internal void Stop() { keepAlive.Stop(); keepAlive.Dispose(); }
    }

    /// <summary>The drag out's IDropSource: drop when the button comes up, cancel at Esc.</summary>
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class DropSource : IOleDropSource
    {
        public int QueryContinueDrag(int fEscapePressed, int grfKeyState) =>
            fEscapePressed != 0 ? Ole.DRAGDROP_S_CANCEL : (grfKeyState & Ole.MK_LBUTTON) == 0 ? Ole.DRAGDROP_S_DROP : Ole.S_OK;

        public int GiveFeedback(int dwEffect) => Ole.DRAGDROP_S_USEDEFAULTCURSORS;
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd, ref Point p);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
}
