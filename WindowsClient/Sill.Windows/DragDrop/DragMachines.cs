using Sill.Windows.Protocol;

namespace Sill.Windows.DragDrop;

// Drag and drop's two directions as state machines, pure (no OLE, no window, a clock the caller gives)
// so WindowsClient/Sill.Checks runs every order of events through them; DragDropHost.cs only feeds them
// OLE's calls and sends what they return.

/// <summary>A drag of this PC's files over a Sill window (kinds 38 and 35, #66's LiveDrags on the host).
/// The files go as kind 34 parts as soon as the drag comes over the window, and to a host whose list says
/// `liveDrag` 1 the drag is followed live: `start` at once (so the host's app can show where they would
/// land once they are there), `move` as the pointer goes (at most 60 a second, and every
/// <see cref="KeepAlive"/> while it rests, since the host gives up a drag it hears nothing of for 10 s),
/// `files` once every part is sent, then `drop` or `cancel`. A drop before the files are all sent is
/// sent as it happens: the host holds it until they are (its `dropWanted`). To a host with `fileDrop`
/// but not `liveDrag`, nothing goes until both the drop and the last part, then one kind 35 at the drop's
/// point. After `drop` or `cancel` nothing more goes, and a drag that leaves or whose upload fails is
/// cancelled once.</summary>
public sealed class LiveDragMachine
{
    public const double MoveInterval = 1.0 / 60, KeepAlive = 2.0;

    readonly bool live;
    bool started, uploaded, dropped, ended;
    uint? window;
    double x, y, lastMoveAt = double.NegativeInfinity;
    ulong modifiers;
    (uint? window, double x, double y, ulong modifiers)? lastSent;
    List<uint> files = new(), folders = new();

    public uint Id { get; }
    /// <summary>Nothing more will be sent: dropped with every file sent, or cancelled.</summary>
    public bool Finished => ended || (dropped && uploaded);
    /// <summary>Cancelled (left, or the upload failed): the caller cancels the upload.</summary>
    public bool Cancelled { get; private set; }
    public bool Dropped => dropped;

    public LiveDragMachine(uint id, bool hostLiveDrag) { Id = id; live = hostLiveDrag; }

    /// <summary>A message to send: one of these is set.</summary>
    public sealed record Out(LiveDrag? Live = null, FileDrop? Drop = null);

    LiveDrag Phase(string phase) => new()
    {
        Id = Id, Phase = phase, Window = window, X = Round(x), Y = Round(y),
        Modifiers = modifiers == 0 ? null : modifiers,
    };

    static double Round(double v) => Math.Round(Math.Clamp(v, 0, 1), 5);

    void At(uint? w, double px, double py, ulong mods) { window = w; x = px; y = py; modifiers = mods; }

    public List<Out> Enter(uint? w, double px, double py, ulong mods, double now)
    {
        At(w, px, py, mods);
        if (!live || started || ended) return new();
        started = true;
        lastMoveAt = now;
        lastSent = (w, x, y, mods);
        return new() { new(Live: Phase(LiveDrag.Phases.Start)) };
    }

    public List<Out> Move(uint? w, double px, double py, ulong mods, double now)
    {
        if (ended || dropped) return new();
        At(w, px, py, mods);
        if (!live || !started) return new();
        bool changed = lastSent != (w, x, y, mods);
        if ((changed && now - lastMoveAt >= MoveInterval) || now - lastMoveAt >= KeepAlive)
        {
            lastMoveAt = now;
            lastSent = (w, x, y, mods);
            return new() { new(Live: Phase(LiveDrag.Phases.Move)) };
        }
        return new();
    }

    /// <summary>Every part is sent: the transfers, and those that are zipped folders.</summary>
    public List<Out> Uploaded(IEnumerable<uint> transferIds, IEnumerable<uint> folderIds)
    {
        if (ended || uploaded) return new();
        uploaded = true;
        files = transferIds.ToList();
        folders = folderIds.ToList();
        if (live)
        {
            var p = Phase(LiveDrag.Phases.FilesSent);
            p.Files = files;
            p.Folders = folders.Count > 0 ? folders : null;
            return new() { new(Live: p) };
        }
        return dropped ? new() { new(Drop: DropMessage()) } : new();
    }

    public List<Out> Drop(uint? w, double px, double py, ulong mods)
    {
        if (ended || dropped) return new();
        At(w, px, py, mods);
        dropped = true;
        if (live) return started ? new() { new(Live: Phase(LiveDrag.Phases.Drop)) } : new();
        return uploaded ? new() { new(Drop: DropMessage()) } : new();
    }

    /// <summary>The drag left every Sill window (or Esc): given up.</summary>
    public List<Out> Leave()
    {
        if (ended || dropped) return new();
        return Cancel();
    }

    /// <summary>The upload failed (too large, a file unreadable, the session gone).</summary>
    public List<Out> Failed()
    {
        if (ended || (dropped && uploaded)) return new();
        return Cancel();
    }

    List<Out> Cancel()
    {
        ended = true;
        Cancelled = true;
        return live && started ? new() { new(Live: Phase(LiveDrag.Phases.Cancel)) } : new();
    }

    FileDrop DropMessage() => new()
    {
        Files = files, X = Round(x), Y = Round(y), Window = window, Folders = folders.Count > 0 ? folders : null,
    };

    /// <summary>OLE's key state as the Mac's modifier bits (kind 8's): Shift, Alt as ⌥, Ctrl as ⌘ while
    /// Ctrl acts as ⌘ (the setting), else as ⌃. Finder reads them at a drop: ⌥ copies, ⌥⌘ makes an alias.</summary>
    public static ulong Modifiers(int keyState, bool ctrlIsCommand)
    {
        const int MK_SHIFT = 4, MK_CONTROL = 8, MK_ALT = 0x20;   // oleidl.h's key state
        ulong m = 0;
        if ((keyState & MK_SHIFT) != 0) m |= (ulong)MacModifiers.Shift;
        if ((keyState & MK_ALT) != 0) m |= (ulong)MacModifiers.Option;
        if ((keyState & MK_CONTROL) != 0) m |= (ulong)(ctrlIsCommand ? MacModifiers.Command : MacModifiers.Control);
        return m;
    }
}

/// <summary>A drag out of a Mac window onto this PC (kind 36, #66's DragOut on the host). While this PC
/// holds the left button over the picture, the host watches its drag pasteboard and offers the files an
/// app there drags (kind 36; an offer of id 0 when the drag ended on the Mac). When the pointer leaves the
/// window with an offer in hand, Windows' own drag starts here (DoDragDrop) with the Mac's files as
/// virtual files, while the Mac's button stays down. How it ends:
/// <list type="bullet">
/// <item>dropped on another app: the files are fetched (kind 37) by that app as it reads them, and the
/// Mac's drag is cancelled, as #66's viewer does: Esc on the Mac, then its button let go;</item>
/// <item>dropped back on a Sill window of the same session: the Mac's own drag lands there (its button let
/// go at that point), nothing copied; while over it, the Mac's pointer follows;</item>
/// <item>cancelled here with the button still held (Esc): the Mac's drag goes on; when the button comes up
/// it is let go on the Mac too, where the pointer is if over the window, else after an Esc;</item>
/// <item>cancelled with the button up (dropped where nothing takes files): Esc, then the button, on the
/// Mac, so nothing lands at the window's edge where the pointer left it.</item>
/// </list></summary>
public sealed class DragOutMachine
{
    public enum State { Idle, Held, Offered, Dragging, HeldAfterCancel }
    public enum Act { StartDrag, EscAndUp, Up, Move }
    public readonly record struct Step(Act Act, double X = 0, double Y = 0, uint? Window = null);

    public State Now { get; private set; } = State.Idle;
    public DragOffer? Offer { get; private set; }

    public void ButtonDown()
    {
        if (Now == State.Dragging) return;
        Now = State.Held;
        Offer = null;
    }

    /// <summary>Kind 36: kept while the button is held here; id 0 (or no files) withdraws it.</summary>
    public void Offered(DragOffer offer)
    {
        if (Now is not (State.Held or State.Offered)) return;
        if (offer.Id == 0 || offer.Files.Count == 0) { Offer = null; Now = State.Held; return; }
        Offer = offer;
        Now = State.Offered;
    }

    /// <summary>The pointer moved with the button held: out of the window with an offer starts the drag.</summary>
    public Step? Pointer(bool inside)
    {
        if (Now == State.Offered && !inside && Offer != null) { Now = State.Dragging; return new Step(Act.StartDrag); }
        return null;
    }

    /// <summary>During the drag, over a Sill window of this session: the Mac's pointer follows there.</summary>
    public Step? OverSelf(double x, double y, uint? window) => Now == State.Dragging ? new Step(Act.Move, x, y, window) : null;

    /// <summary>DoDragDrop returned. <paramref name="selfPoint"/>: the point (and window) of a drop on a
    /// Sill window of this session; <paramref name="last"/>: where the Mac's pointer was last sent.</summary>
    public List<Step> Ended(bool dropped, (double x, double y, uint? window)? selfPoint, bool buttonDown, (double x, double y) last)
    {
        if (Now != State.Dragging) return new();
        Offer = null;
        if (dropped && selfPoint is { } p) { Now = State.Idle; return new() { new Step(Act.Up, p.x, p.y, p.window) }; }
        if (dropped) { Now = State.Idle; return new() { new Step(Act.EscAndUp, last.x, last.y) }; }
        if (buttonDown) { Now = State.HeldAfterCancel; return new(); }
        Now = State.Idle;
        return new() { new Step(Act.EscAndUp, last.x, last.y) };
    }

    /// <summary>The button came up outside a drag. After a cancelled drag: over the window the window's
    /// own up lands the Mac's drag there (nothing to add); outside it, Esc and the up. Otherwise the
    /// window sends its up as for any click.</summary>
    public List<Step> ButtonUp(bool inside, (double x, double y) last)
    {
        var was = Now;
        if (was == State.Dragging) return new();
        Now = State.Idle;
        Offer = null;
        return was == State.HeldAfterCancel && !inside ? new() { new Step(Act.EscAndUp, last.x, last.y) } : new();
    }
}
