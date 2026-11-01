using Sill.Windows.DragDrop;
using Sill.Windows.Protocol;

namespace Sill.Checks;

// Drag and drop's phases: a PC drag over a Mac window (kind 38 to a host that follows it live, kind 35
// to one that does not) in every order OLE and the upload can bring, and a drag out of the Mac (kind 36)
// ending each way it can.
static class DragChecks
{
    static string Phases(IEnumerable<LiveDragMachine.Out> outs) =>
        string.Join(" ", outs.Select(o => o.Live?.Phase ?? (o.Drop != null ? "fileDrop" : "?")));

    public static void Live()
    {
        // A live host: start, moves, files, drop.
        var m = new LiveDragMachine(5, hostLiveDrag: true);
        var all = new List<LiveDragMachine.Out>();
        var start = m.Enter(4711, 0.25, 0.5, 0, 0);
        all.AddRange(start);
        Check.That(start.Count == 1 && start[0].Live is { Id: 5, Phase: "start", Window: 4711, X: 0.25, Y: 0.5, Modifiers: null }, "enter: start, with the window and point");
        Check.SameJson("{\"id\":5,\"phase\":\"start\",\"window\":4711,\"x\":0.25,\"y\":0.5}", Json.Encode(start[0].Live), "start's JSON: no modifiers when none are held, no files yet");
        Check.Equal(0, m.Move(4711, 0.26, 0.5, 0, 0.005).Count, "a move 5 ms later: held (60 a second at most)");
        var mv = m.Move(4711, 0.27, 0.5, (ulong)MacModifiers.Option, 0.02);
        all.AddRange(mv);
        Check.That(mv.Count == 1 && mv[0].Live is { Phase: "move", X: 0.27, Modifiers: 0x80000 }, "a move 20 ms in: sent, with ⌥ held");
        Check.Equal(0, m.Move(4711, 0.27, 0.5, (ulong)MacModifiers.Option, 0.5).Count, "resting: nothing new 0.5 s on");
        Check.Equal(1, m.Move(4711, 0.27, 0.5, (ulong)MacModifiers.Option, 2.1).Count, "resting 2 s: a move again, so the host (10 s of silence) keeps the drag");
        var files = m.Uploaded(new uint[] { 31, 32 }, new uint[] { 32 });
        all.AddRange(files);
        Check.That(files.Count == 1 && files[0].Live is { Phase: "files" } f && f.Files!.SequenceEqual(new uint[] { 31, 32 }) && f.Folders!.SequenceEqual(new uint[] { 32 }),
                   "every part sent: files, with the transfers and the zipped folder");
        var drop = m.Drop(4711, 0.6, 0.4, 0);
        Check.That(drop.Count == 1 && drop[0].Live is { Phase: "drop", X: 0.6, Y: 0.4 } && m.Finished, "drop: sent at its point; finished");
        Check.Equal(0, m.Leave().Count + m.Move(1, 0, 0, 0, 9).Count + m.Failed().Count, "after the drop: nothing more, whatever comes");

        // A drop before the files are all sent: drop goes as it happens, files after (the host holds the drop).
        var early = new LiveDragMachine(6, true);
        early.Enter(null, 0.5, 0.5, 0, 0);
        var d2 = early.Drop(null, 0.5, 0.5, 0);
        Check.That(Phases(d2) == "drop" && !early.Finished, "a drop while parts still go: drop now, not finished");
        Check.Equal("files", Phases(early.Uploaded(new uint[] { 7 }, Array.Empty<uint>())), "then files once the last part went");
        Check.That(early.Finished, "then finished");
        Check.SameJson("{\"id\":6,\"phase\":\"files\",\"x\":0.5,\"y\":0.5,\"files\":[7]}",
                       Json.Encode(new LiveDragMachine(6, true).Let(x => { x.Enter(null, 0.5, 0.5, 0, 0); return x.Uploaded(new uint[] { 7 }, Array.Empty<uint>())[0].Live; })),
                       "files' JSON for the picked window: no window, no empty folders");

        // Leave: cancel once, the upload given up.
        var gone = new LiveDragMachine(7, true);
        gone.Enter(9, 0.1, 0.1, 0, 0);
        Check.That(Phases(gone.Leave()) == "cancel" && gone.Cancelled, "leave: cancel");
        Check.Equal(0, gone.Leave().Count + gone.Uploaded(new uint[] { 1 }, Array.Empty<uint>()).Count + gone.Drop(9, 0, 0, 0).Count, "nothing after cancel, not even the upload's end");
        var failed = new LiveDragMachine(8, true);
        failed.Enter(9, 0.1, 0.1, 0, 0);
        failed.Drop(9, 0.2, 0.2, 0);
        Check.That(Phases(failed.Failed()) == "cancel", "the upload failing after the drop: cancel, so the host lets go of what it holds");

        // A host with fileDrop but not liveDrag: one kind 35, after both the drop and the last part, whichever is last.
        var old = new LiveDragMachine(9, hostLiveDrag: false);
        Check.Equal(0, old.Enter(4711, 0.3, 0.3, 0, 0).Count + old.Move(4711, 0.4, 0.4, 0, 1).Count, "an older host: nothing while dragging");
        Check.Equal(0, old.Uploaded(new uint[] { 41, 42 }, new uint[] { 42 }).Count, "nothing at the upload's end before the drop");
        var fd = old.Drop(4711, 0.7, 0.2, 0);
        Check.That(fd.Count == 1 && fd[0].Drop is { X: 0.7, Y: 0.2, Window: 4711 } dd && dd.Files.SequenceEqual(new uint[] { 41, 42 }) && dd.Folders!.SequenceEqual(new uint[] { 42 }) && old.Finished,
                   "the drop: kind 35 with the transfers at the drop's point");
        var old2 = new LiveDragMachine(10, false);
        old2.Enter(null, 0.3, 0.3, 0, 0);
        Check.Equal(0, old2.Drop(null, 0.9, 0.9, 0).Count, "dropped before the parts are all sent: nothing yet");
        var late = old2.Uploaded(new uint[] { 3 }, Array.Empty<uint>());
        Check.That(late.Count == 1 && late[0].Drop is { X: 0.9, Y: 0.9, Window: null }, "then kind 35, at the drop's point, not the last move's");
        Check.SameJson("{\"files\":[3],\"x\":0.9,\"y\":0.9}", Json.Encode(late[0].Drop), "kind 35's JSON for the picked window");
        var old3 = new LiveDragMachine(11, false);
        old3.Enter(null, 0.3, 0.3, 0, 0);
        Check.Equal(0, old3.Leave().Count, "an older host, left: nothing to say (the parts sent stay unclaimed on the host)");

        // The modifiers.
        Check.Equal((ulong)(MacModifiers.Command | MacModifiers.Shift), LiveDragMachine.Modifiers(8 | 4, ctrlIsCommand: true), "Ctrl+Shift with Ctrl as ⌘: ⌘⇧");
        Check.Equal((ulong)(MacModifiers.Control | MacModifiers.Option), LiveDragMachine.Modifiers(8 | 0x20, ctrlIsCommand: false), "Ctrl+Alt with Ctrl as itself: ⌃⌥");
        Check.Equal(0UL, LiveDragMachine.Modifiers(1, true), "the left button alone: no modifier");

        // Random drags: the order of phases always holds.
        var rng = new Random(13);
        int good = 0;
        for (int run = 0; run < 3000; run++)
        {
            bool liveHost = rng.Next(2) == 0;
            var mm = new LiveDragMachine((uint)run, liveHost);
            var seq = new List<string>();
            double t = 0;
            seq.AddRange(mm.Enter(1, rng.NextDouble(), rng.NextDouble(), 0, t).Select(Name));
            for (int step = 0; step < 12; step++)
            {
                t += rng.NextDouble() * 0.5;
                List<LiveDragMachine.Out> o = rng.Next(6) switch
                {
                    0 => mm.Drop(1, rng.NextDouble(), rng.NextDouble(), 0),
                    1 => mm.Leave(),
                    2 => mm.Uploaded(new uint[] { 1 }, Array.Empty<uint>()),
                    3 => mm.Failed(),
                    _ => mm.Move(1, rng.NextDouble(), rng.NextDouble(), (ulong)rng.Next(2) << 19, t),
                };
                seq.AddRange(o.Select(Name));
            }
            if (Valid(seq, liveHost)) good++;
        }
        Check.Equal(3000, good, "3,000 random drags, live and not: start first, at most one files, at most one drop or cancel and nothing after a cancel, kind 35 only to an older host and only once");
    }

    static string Name(LiveDragMachine.Out o) => o.Live?.Phase ?? "fileDrop";

    static bool Valid(List<string> seq, bool live)
    {
        if (!live) return seq.All(s => s == "fileDrop") && seq.Count <= 1;
        if (seq.Count == 0 || seq[0] != "start") return false;
        if (seq.Count(s => s == "start") != 1 || seq.Count(s => s == "files") > 1) return false;
        int ends = seq.Count(s => s is "drop" or "cancel");
        if (ends > 1 && !(ends == 2 && seq.Contains("drop") && seq[^1] == "cancel")) return false;   // a failure after a drop cancels it
        int cancel = seq.IndexOf("cancel");
        if (cancel >= 0 && cancel != seq.Count - 1) return false;
        int drop = seq.IndexOf("drop");
        if (drop >= 0 && seq.Skip(drop + 1).Any(s => s == "move" || s == "start")) return false;
        return !seq.Contains("fileDrop");
    }

    public static void Out()
    {
        var offer = new DragOffer { Id = 9, Files = new() { new OfferedFile { Name = "a.pdf", Size = 10 } } };
        // An offer while the button is held, the pointer out of the window: the drag starts.
        var m = new DragOutMachine();
        m.Offered(offer);
        Check.Equal(DragOutMachine.State.Idle, m.Now, "an offer with no button held here: ignored");
        m.ButtonDown();
        Check.That(m.Pointer(false) == null, "out of the window with no offer yet: nothing (the Mac's drag goes on there)");
        m.Offered(offer);
        Check.That(m.Now == DragOutMachine.State.Offered && m.Offer == offer, "the offer while held: kept");
        Check.That(m.Pointer(true) == null, "inside the window: nothing");
        Check.That(m.Pointer(false) is { Act: DragOutMachine.Act.StartDrag } && m.Now == DragOutMachine.State.Dragging, "out of the window: Windows' drag starts");
        Check.That(m.OverSelf(0.5, 0.5, 4711) is { Act: DragOutMachine.Act.Move, X: 0.5, Window: 4711 }, "back over a Sill window: the Mac's pointer follows there");
        var ended = m.Ended(dropped: true, selfPoint: null, buttonDown: false, last: (1, 0.3));
        Check.That(ended.Count == 1 && ended[0] is { Act: DragOutMachine.Act.EscAndUp, X: 1, Y: 0.3 } && m.Now == DragOutMachine.State.Idle,
                   "dropped on another app: Esc and the button up on the Mac, at the last point");

        // Dropped back on a Sill window: the Mac's own drop there, no Esc.
        var self = new DragOutMachine();
        self.ButtonDown(); self.Offered(offer); self.Pointer(false);
        var s = self.Ended(true, (0.2, 0.8, 4712), false, (0, 0));
        Check.That(s.Count == 1 && s[0] is { Act: DragOutMachine.Act.Up, X: 0.2, Y: 0.8, Window: 4712 }, "dropped on a Sill window: the button up there, nothing copied");

        // Cancelled with the button up: Esc and up, so nothing lands at the edge.
        var cancel = new DragOutMachine();
        cancel.ButtonDown(); cancel.Offered(offer); cancel.Pointer(false);
        Check.That(cancel.Ended(false, null, false, (0, 0.5)) is [{ Act: DragOutMachine.Act.EscAndUp }], "cancelled, the button up: Esc and up on the Mac");

        // Cancelled with the button held (Esc here): the Mac's drag goes on until the button comes up.
        var held = new DragOutMachine();
        held.ButtonDown(); held.Offered(offer); held.Pointer(false);
        Check.That(held.Ended(false, null, true, (0, 0.5)).Count == 0 && held.Now == DragOutMachine.State.HeldAfterCancel, "Esc with the button held: the Mac's drag goes on");
        Check.That(held.Pointer(false) == null, "and no second drag starts for it");
        Check.That(held.ButtonUp(inside: true, (0.4, 0.4)).Count == 0 && held.Now == DragOutMachine.State.Idle, "the button up over the window: the window's own up lands it there");
        var held2 = new DragOutMachine();
        held2.ButtonDown(); held2.Offered(offer); held2.Pointer(false); held2.Ended(false, null, true, (0, 0.5));
        Check.That(held2.ButtonUp(false, (0, 0.5)) is [{ Act: DragOutMachine.Act.EscAndUp }], "the button up outside: Esc and up");

        // The Mac's drag ended there (offer id 0) before the pointer left: no drag here.
        var withdrawn = new DragOutMachine();
        withdrawn.ButtonDown(); withdrawn.Offered(offer); withdrawn.Offered(new DragOffer { Id = 0 });
        Check.That(withdrawn.Now == DragOutMachine.State.Held && withdrawn.Pointer(false) == null, "an offer of id 0 (the drag ended on the Mac): withdrawn, no drag here");
        var plain = new DragOutMachine();
        plain.ButtonDown(); plain.Offered(offer);
        Check.That(plain.ButtonUp(true, (0.5, 0.5)).Count == 0 && plain.Now == DragOutMachine.State.Idle, "the button up inside with an offer: an ordinary click's up, nothing added");
        Check.That(plain.Ended(true, null, false, (0, 0)).Count == 0, "an end with no drag under way: nothing");
        var busy = new DragOutMachine();
        busy.ButtonDown(); busy.Offered(offer); busy.Pointer(false);
        busy.ButtonDown(); busy.Offered(new DragOffer { Id = 0 });
        Check.That(busy.Now == DragOutMachine.State.Dragging && busy.ButtonUp(true, (0, 0)).Count == 0, "while the drag runs, the window's button and new offers change nothing");
    }
}

static class LetExtension
{
    public static TResult Let<T, TResult>(this T value, Func<T, TResult> f) => f(value);
}
