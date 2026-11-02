using System.Drawing;
using System.Text;
using System.Text.Json.Nodes;
using Sill.Windows.Coherence;
using Sill.Windows.Protocol;

namespace Sill.Checks;

// Coherence (Coherence/*.cs): kind 45's reducer, kind 29's content and coalescing, the hidden list,
// the geometry between points, pictures and windows, kind 46's throttle, and the controller end to
// end against a recording session on a simulated clock.
static class CoherenceChecks
{
    static byte[] U(string s) => Encoding.UTF8.GetBytes(s);
    static WindowState WS(string json) => Json.Decode<WindowState>(U(json))!;

    // MARK: kind 45

    public static void Kind45()
    {
        var set = new WindowSet();
        var u = set.Apply(WS("""
            {"gen":10,"full":true,"windows":[
              {"id":1,"app":"com.apple.Safari","appName":"Safari","title":"Apple","role":"window","size":[1280,800],"min":[500,300],"resizable":true,"focused":true,"titleBar":28},
              {"id":2,"app":"com.apple.TextEdit","appName":"TextEdit","title":"Readme","size":[800,500]},
              {"id":3,"app":"com.apple.Safari","role":"menu","owner":1,"rect":[40,22,220,310],"inCrop":true}]}
            """));
        Check.That(u.Applied && u.Full && u.Added.Count == 3 && set.Count == 3, "a full kind 45: three windows added");
        Check.That(set[1]!.Focused && set[1]!.TitleBar == 28 && set[1]!.SizePoints == (1280, 800) && set[1]!.Min![0] == 500, "every field read: focus, the title bar, the size, the limits");
        Check.Equal("window", set[2]!.Role, "no role: a window");
        Check.Equal(Presentation.Window, set.PresentationOf(set[1]!), "a top-level window: a window of its own");
        Check.Equal(Presentation.None, set.PresentationOf(set[3]!), "a menu inside its owner's picture: nothing of its own");
        Check.That(set.HasOpenMenu && set.OpenMenuOwner == 1, "a menu is open, and belongs to window 1");

        Check.That(!set.Apply(WS("""{"gen":9,"windows":[{"id":1,"title":"Old"}]}""")).Applied && set[1]!.Title == "Apple", "an older generation is dropped whole");
        Check.That(!set.Apply(WS("""{"gen":10,"windows":[{"id":1,"title":"Same gen"}]}""")).Applied, "the same generation again is dropped");

        u = set.Apply(WS("""{"gen":11,"windows":[{"id":1,"title":"Apple — Start"}]}"""));
        Check.That(u.Applied && !u.Full && u.Changed.Count == 1 && u.Changed[0].change == WindowChange.Title, "a partial update: only the title changed");
        Check.That(set[1]!.SizePoints == (1280, 800) && set[1]!.TitleBar == 28 && set[1]!.Focused, "a partial entry keeps the fields it leaves out");
        Check.That(set.Count == 3, "a partial update removes nothing it does not name");

        u = set.Apply(WS("""{"gen":12,"windows":[{"id":2,"size":[900,600],"min":[300,200]}],"focus":2}"""));
        var c2 = u.Changed.Single(c => c.window.Id == 2).change;
        Check.That(c2.HasFlag(WindowChange.Size) && c2.HasFlag(WindowChange.Limits) && c2.HasFlag(WindowChange.Focus), $"size, limits and focus changed on window 2 ({c2})");
        Check.That(set[2]!.Focused && !set[1]!.Focused && u.Changed.Any(c => c.window.Id == 1 && c.change == WindowChange.Focus),
                   "the message's focus clears the window that lost it, though only the new one is listed");

        u = set.Apply(WS("""{"gen":13,"removed":[1]}"""));
        Check.That(u.Removed.Select(w => w.Id).OrderBy(x => x).SequenceEqual(new uint[] { 1, 3 }) && set.Count == 1, "a removed window takes its child (the menu) with it");
        Check.That(!set.HasOpenMenu, "with its owner gone, no menu is open");

        u = set.Apply(WS("""{"gen":14,"windows":[{"id":4,"app":"com.apple.Safari","title":"New","joined":true,"size":[640,480]}]}"""));
        Check.That(u.Added.Single().Id == 4 && set[4]!.Joined && set.Count == 2, "a partial with an unknown window adds it (joined)");

        u = set.Apply(WS("""{"gen":15,"full":true,"windows":[{"id":4}]}"""));
        Check.That(u.Full && u.Removed.Single().Id == 2 && set.Count == 1 && set[4]!.Title == "New", "a full message removes what it leaves out, and merges what it lists");

        // Children that spill out.
        set.Apply(WS("""
            {"gen":16,"windows":[{"id":5,"role":"menu","owner":4,"rect":[10,20,200,300],"inCrop":false},
            {"id":6,"role":"sheet","owner":4,"rect":[0,28,400,200],"inCrop":false},
            {"id":7,"role":"tooltip","owner":99,"inCrop":false},
            {"id":8,"role":"popover","owner":4},
            {"id":9,"role":"menu"},
            {"id":10,"role":"menu","owner":5,"rect":[200,0,150,100],"inCrop":false},
            {"id":11,"role":"dialog","size":[300,200]},
            {"id":12,"role":"inspector","size":[300,200]}]}
            """));
        Check.Equal(Presentation.Popup, set.PresentationOf(set[5]!), "a menu that spills out: a popup");
        Check.Equal(Presentation.OwnedWindow, set.PresentationOf(set[6]!), "a sheet that spills out: an owned window");
        Check.Equal(Presentation.None, set.PresentationOf(set[7]!), "a child whose owner this PC does not have: nothing");
        Check.Equal(Presentation.None, set.PresentationOf(set[8]!), "a child with no inCrop counts as inside its owner");
        Check.Equal(Presentation.None, set.PresentationOf(set[9]!), "a menu with no owner: nothing (no place to put it)");
        Check.Equal(Presentation.Popup, set.PresentationOf(set[10]!), "a submenu owned by a popup: a popup");
        Check.That(set.TopOwner(set[10]!)?.Id == 4, "a submenu's top owner is the window under its menu");
        Check.Equal(Presentation.Window, set.PresentationOf(set[11]!), "a dialog with no owner: a window of its own");
        Check.That(set[12]!.Role == "window" && set.PresentationOf(set[12]!) == Presentation.Window, "a role this build does not know: a window");
        Check.That(set.Streamed.Select(w => w.Id).SequenceEqual(new uint[] { 4, 5, 6, 10, 11, 12 }), $"streamed: every window shown, in the order seen ({string.Join(",", set.Streamed.Select(w => w.Id))})");

        u = set.Apply(WS("""{"gen":17,"windows":[{"id":5,"inCrop":true}]}"""));
        Check.That(u.Changed.Single().change == WindowChange.Placement && set.PresentationOf(set[5]!) == Presentation.None, "a popup back inside its owner: placement changed, nothing of its own");
        Check.That(set.PresentationOf(set[10]!) == Presentation.Popup, "its submenu still spills out");

        u = set.Apply(WS("""{"gen":18,"windows":[{"id":13,"owner":13,"size":[10,10]}]}"""));
        Check.That(set[13]!.Owner == null && set.PresentationOf(set[13]!) == Presentation.Window, "a window that names itself its owner has none");

        u = set.TakeBack(new uint[] { 4 });
        Check.That(u.Removed.Select(w => w.Id).OrderBy(x => x).SequenceEqual(new uint[] { 4, 5, 6, 8, 10 }), "kind 41 takes a window back with every child, and theirs");

        set.ResetGeneration();
        Check.That(set.Apply(WS("""{"gen":1,"windows":[{"id":11,"title":"after a reconnect"}]}""")).Applied, "a new session's generations start again");

        // A window this PC stops showing keeps its entry, out of sight, until a kind 45 removes it: the Mac
        // learns of the stop only from a later kind 29 (docs/coherence-protocol.md, kind 45).
        var kept = new WindowSet();
        kept.Apply(WS("""{"gen":1,"full":true,"windows":[{"id":1,"title":"One","size":[800,600]},{"id":2,"title":"Two"},{"id":3,"role":"sheet","owner":1,"rect":[0,28,400,700],"inCrop":false}]}"""));
        u = kept.Forget(1);
        Check.That(u.Removed.Select(w => w.Id).OrderBy(x => x).SequenceEqual(new uint[] { 1, 3 }) && kept.Count == 1 && !kept.Contains(1) && kept[3] == null,
                   "stopped: the window and its sheet leave what this PC shows");
        Check.That(kept.Holds(1) && kept.Holds(3) && kept.Kept.Select(w => w.Id).SequenceEqual(new uint[] { 1, 3 }) && kept.Streamed.Select(w => w.Id).SequenceEqual(new uint[] { 2 }),
                   "their entries are kept out of sight, and need no stream");
        u = kept.Apply(WS("""{"gen":2,"windows":[{"id":1,"title":"One — edited"},{"id":4,"role":"menu","owner":1,"inCrop":false}],"focus":1}"""));
        Check.That(!u.Any && kept.Count == 1 && kept.Kept.Count() == 3,
                   "a kind 45 the Mac wrote before it read the stop changes nothing shown: the stopped window's fields, focus and new child stay out of sight");
        u = kept.Show(1);
        Check.That(u.Added.Select(w => w.Id).OrderBy(x => x).SequenceEqual(new uint[] { 1, 3, 4 }) && kept[1]!.Title == "One — edited" && kept[1]!.Focused
                   && kept[1]!.SizePoints == (800, 600) && kept.Kept.Count() == 0,
                   "shown again before the Mac read the stop: at once, from the entry kept and kept up to date, with its children");
        kept.Forget(2);
        u = kept.Apply(WS("""{"gen":3,"removed":[2]}"""));
        Check.That(!u.Any && !kept.Holds(2) && kept.Stopped.Contains(2), "a kind 45 removes a stopped window: its entry goes, nothing shown changes, the stop stays");
        u = kept.Apply(WS("""{"gen":4,"windows":[{"id":2,"title":"Two"}]}"""));
        Check.That(!u.Any && kept.Holds(2) && !kept.Contains(2), "a kind 45 that brings it again (written before the Mac read the stop): kept, not shown");
        u = kept.Apply(WS("""{"gen":5,"full":true,"windows":[{"id":1},{"id":3},{"id":4}]}"""));
        Check.That(!u.Any && !kept.Holds(2), "a full message that leaves a stopped window out removes it too");
        u = kept.Show(2);
        Check.That(!u.Any && !kept.Stopped.Contains(2) && !kept.Holds(2), "shown again after its removal: nothing yet (the controller asks for it)");
        kept.Forget(1);
        u = kept.TakeBack(new uint[] { 1 });
        Check.That(!u.Any && !kept.Holds(1) && !kept.Holds(3) && !kept.Holds(4) && kept.Stopped.Count == 0, "kind 41 takes a stopped window back with its children, and the stop with it");
        var moved = new WindowSet();
        moved.Apply(WS("""{"gen":1,"full":true,"windows":[{"id":1},{"id":2},{"id":5,"role":"menu","owner":1,"inCrop":false}]}"""));
        moved.Forget(1);
        u = moved.Apply(WS("""{"gen":2,"windows":[{"id":5,"owner":2}]}"""));
        Check.That(u.Added.Single().Id == 5 && moved.Contains(5) && moved.PresentationOf(moved[5]!) == Presentation.Popup, "a menu moved from a stopped window to a shown one comes into sight");
        u = moved.Apply(WS("""{"gen":3,"windows":[{"id":5,"owner":1}]}"""));
        Check.That(u.Removed.Single().Id == 5 && !moved.Contains(5) && moved.Holds(5), "and back under the stopped one, out of sight again");

        // A random walk: the set never holds a child whose owner it does not have after a removal, and
        // never applies an older message.
        var rng = new Random(45);
        var walk = new WindowSet();
        long gen = 0;
        bool ok = true;
        for (int i = 0; i < 3000; i++)
        {
            var m = new WindowState { Gen = rng.Next(6) == 0 ? gen - rng.Next(3) : ++gen, Full = rng.Next(10) == 0 };
            m.Windows = new();
            for (int k = rng.Next(4); k > 0; k--)
            {
                uint id = (uint)rng.Next(1, 20);
                m.Windows.Add(new WindowState.Window { Id = id, Owner = rng.Next(3) == 0 ? (uint)rng.Next(1, 20) : null, Title = $"t{i}" });
            }
            if (rng.Next(3) == 0) m.Removed = new() { (uint)rng.Next(1, 20) };
            long? before = walk.LastGen;
            var r = walk.Apply(m);
            if (r.Applied != WindowState.Newer(m.Gen, before)) ok = false;
            foreach (var removedId in r.Removed.Select(w => w.Id))
                if (walk.All.Any(w => w.Owner == removedId)) ok = false;
            if (r.Applied && m.Full == true && walk.All.Any(w => m.Windows.All(e => e.Id != w.Id))) ok = false;
        }
        Check.That(ok, "3,000 random kind 45s: older ones dropped, no orphan of a removed owner, a full leaves only what it lists");
    }

    // MARK: kind 29

    public static void SideStreams()
    {
        var set = new WindowSet();
        var plan = new SideStreamPlan { Scale = 1.25, Screen = (2048, 1152), Fps = 30, FocusFps = 60 };
        plan.Ask(11, 0); plan.Ask(12, 0);
        plan.Focus = 11;
        var r = plan.Request(set);
        Check.SameJson("""{"windows":[11,12],"fps":30,"wholeApp":true,"screen":[2048,1152],"coherence":true,"focus":11,"focusFPS":60,"scale":1.25}""",
                       Json.Encode(r), "before kind 45: the windows asked for, the focus, the scale and the screen; no apps, no hidden");

        set.Apply(WS("""{"gen":1,"full":true,"windows":[{"id":11,"size":[800,600]},{"id":12,"size":[640,480]},{"id":13,"role":"menu","owner":11,"inCrop":false,"rect":[0,0,10,10]},{"id":14,"role":"menu","owner":12,"inCrop":true}]}"""));
        plan.Listed(set, true, 0.5);
        Check.That(plan.Asked.Count == 0, "windows the Mac lists are no longer pending");
        r = plan.Request(set);
        Check.That(r.Windows.SequenceEqual(new uint[] { 11, 12, 13 }), "kind 29 asks for each window shown and the popup that spills out, not the one inside");

        plan.SetMinimized(12, true);
        Check.That(plan.Request(set).Hidden!.SequenceEqual(new uint[] { 12 }), "a minimized window is hidden");
        plan.SetMinimized(11, true);
        Check.That(plan.Hidden(set).SequenceEqual(new uint[] { 12 }), "the focused window is never hidden, minimized or not");
        plan.Focus = 12;
        Check.That(plan.Hidden(set).SequenceEqual(new uint[] { 11, 13 }), "a popup is hidden with its owner");
        plan.SetMinimized(11, false); plan.SetMinimized(12, false);
        Check.That(plan.Request(set).Hidden == null, "nothing hidden: the field left out");

        plan.Apps.Add("com.apple.Safari"); plan.All = true;
        var json = JsonNode.Parse(Json.Encode(plan.Request(set)))!;
        Check.That(json["apps"]![0]!.GetValue<string>() == "com.apple.Safari" && json["all"]!.GetValue<bool>(), "apps and all go when set");
        plan.Focus = 99;
        Check.That(plan.Request(set).Focus == null, "a focus that is not in the set is not sent");
        plan.Focus = 11;

        plan.Ask(20, 1.0);
        set.Apply(WS("""{"gen":2,"full":true,"windows":[{"id":11},{"id":12}]}"""));
        plan.Listed(set, true, 1.5);
        Check.That(plan.Asked.Contains(20u) && plan.Windows(set).Contains(20u), "a window asked for 0.5 s before a full that leaves it out stays asked (the full may predate the ask)");
        set.Apply(WS("""{"gen":3,"full":true,"windows":[{"id":11},{"id":12}]}"""));
        plan.Listed(set, true, 3.1);
        Check.That(!plan.Asked.Contains(20u), "after its grace, a full that leaves it out drops it");
        plan.Ask(21, 4); set.Apply(WS("""{"gen":4,"windows":[{"id":11,"title":"x"}]}""")); plan.Listed(set, false, 10);
        Check.That(plan.Asked.Contains(21u), "a partial never drops a window asked for");

        Check.SameJson("""{"windows":[]}""", Json.Encode(SideStreamPlan.Stop()), "leaving Coherence: no window, no coherence");
        Check.That(new SideStreamPlan { Scale = 9, Fps = 500, FocusFps = 1000 }.Request(new WindowSet()) is { Scale: 4, Fps: 30, FocusFPS: 120 }, "the rates and the scale held to the contract's limits");

        // The coalescer.
        var co = new RequestCoalescer { Interval = 0.1 };
        Check.That(!co.Due(0), "nothing marked: nothing due");
        co.Mark();
        Check.That(co.Due(0) && co.Offer(U("a"), 0), "the first change goes at once");
        co.Mark();
        Check.That(!co.Due(0.05) && co.NextAt == 0.1, "a second change within 100 ms waits for the interval's end");
        Check.That(co.Due(0.1) && co.Offer(U("b"), 0.1), "and goes then");
        co.Mark();
        Check.That(co.Due(0.3) && !co.Offer(U("b"), 0.3) && !co.Dirty, "the same request again is not sent, and nothing stays owed");
        co.Mark();
        Check.That(co.Due(0.31) && co.Offer(U("c"), 0.31), "an unsent repeat does not hold the next change back");
        co.Reset(); co.Mark();
        Check.That(co.Due(0.32) && co.Offer(U("c"), 0.32), "a new session sends whatever went before");

        // Random marks: never two sends within the interval, and the last change always goes.
        var rng = new Random(29);
        var c2 = new RequestCoalescer { Interval = 0.1 };
        double last = double.NegativeInfinity, t = 0;
        bool spaced = true;
        string current = "0", sentLast = "";
        for (int i = 0; i < 5000; i++)
        {
            t += rng.NextDouble() * 0.06;
            if (rng.Next(3) == 0) { current = rng.Next(5).ToString(); c2.Mark(); }
            if (c2.Due(t) && c2.Offer(U(current), t)) { if (t - last < 0.1 - 1e-9) spaced = false; last = t; sentLast = current; }
            else if (!c2.Dirty) { }
        }
        t += 1; if (c2.Due(t) && c2.Offer(U(current), t)) sentLast = current;
        Check.That(spaced && sentLast == current, "5,000 random changes: never two kind 29s within 100 ms, and the last state always went");
    }

    // MARK: geometry and kind 46

    public static void Geometry()
    {
        Check.Equal((double?)2.0, ProxyGeometry.CaptureScale(2560, 1600, 1280, 800, null), "a 1280×800 pt window in a 2560×1600 picture: 2 px a point");
        Equal(1.25, ProxyGeometry.CaptureScale(1000, 626, 800, 500, null)!.Value, 0.01, "HEVC's even sides: 1.25 within the tolerance");
        Check.Equal((double?)1.5, ProxyGeometry.CaptureScale(2560, 1000, 1280, 800, 1.5), "sides that disagree (a picture of the old size): the scale before");
        Check.Equal((double?)null, ProxyGeometry.CaptureScale(0, 0, 800, 500, null), "no picture: none");

        Check.Equal(56, ProxyGeometry.CropPixels(28, 2, 1600), "a 28 pt title bar at 2 px a point: 56 rows");
        Check.Equal(35, ProxyGeometry.CropPixels(28, 1.25, 626), "at 1.25: 35 rows");
        Check.Equal(0, ProxyGeometry.CropPixels(0, 2, 1600), "no title bar to crop (a unified toolbar): none");
        Check.Equal(99, ProxyGeometry.CropPixels(500, 2, 100), "never the whole picture");
        Equal(0.035, ProxyGeometry.CropFraction(56, 1600), 1e-9, "the crop as a fraction");

        Check.Equal(new Size(1600, 965), ProxyGeometry.SurfaceSize(1280, 800, 28, 1.25), "the surface: 1280×(800−28) pt at 125 %");
        Check.Equal(new Size(800, 500), ProxyGeometry.SurfaceSize(800, 500, 500, 1), "a title bar as tall as the window is none");
        Check.Equal((1280.0, 800.0), ProxyGeometry.MacSize(1600, 965, 28, 1.25), "and back: the Mac's size in whole points");
        Check.Equal((1281.0, 802.0), ProxyGeometry.MacSize(1601, 967, 28, 1.25), "a surface a pixel off rounds to whole points (1280.8, 801.6)");
        bool roundTrip = true;
        foreach (var s in new[] { 1.0, 1.25, 1.5, 1.75, 2, 2.25, 2.5, 3 })
            for (int w = 300; w < 2000; w += 37)
            {
                var px = ProxyGeometry.SurfaceSize(w, w * 0.7, 28, s);
                var back = ProxyGeometry.MacSize(px.Width, px.Height, 28, s);
                if (Math.Abs(back.w - w) > 0.5 || Math.Abs(back.h - Math.Round(w * 0.7)) > 1) roundTrip = false;
            }
        Check.That(roundTrip, "points → surface → points comes back within a point at every common scale");

        var (x0, y0) = ProxyGeometry.PointerFraction(0, 0, 1600, 965, 0.035);
        Check.That(Math.Abs(x0 - 0.5 / 1600) < 1e-9 && y0 > 0.035 && y0 < 0.0356, $"the surface's top-left pixel is just under the title bar ({y0:0.00000})");
        var (x1, y1) = ProxyGeometry.PointerFraction(1599, 964, 1600, 965, 0.035);
        Check.That(x1 < 1 && y1 < 1 && x1 > 0.999 && y1 > 0.999, "its bottom-right pixel is the picture's last");
        Check.That(ProxyGeometry.PointerFraction(-50, -50, 1600, 965, 0.035) == (0, 0.035), "a point above the surface (a captured drag) holds to the crop's edge");
        Check.That(ProxyGeometry.PointerFraction(5000, 5000, 1600, 965, 0) == (1, 1), "past the bottom-right: 1, 1");
        var mid = ProxyGeometry.PointerFraction(799.5, 482, 1600, 965, 0.2);
        Check.That(Math.Abs(mid.x - 0.5) < 1e-3 && Math.Abs(mid.y - (0.2 + 0.5 * 0.8)) < 1e-3, "the middle of the surface is the middle of what it shows");

        var w1 = new MacWindow { Id = 1, Size = new double[] { 800, 600 }, Min = new double[] { 400, 300 }, Max = new double[] { 1200, 900 }, TitleBar = 28, Resizable = true };
        var chrome = new Size(16, 63);
        var (min, max) = ProxyGeometry.TrackLimits(w1, 1.5, chrome);
        Check.Equal(new Size(600 + 16, (int)Math.Round((300 - 28) * 1.5) + 63), min!.Value, "the least track size: min less the title bar, at 150 %, plus the frame and menu");
        Check.Equal(new Size(1800 + 16, (int)Math.Round((900 - 28) * 1.5) + 63), max!.Value, "the most: max likewise");
        w1.Resizable = false;
        var (fmin, fmax) = ProxyGeometry.TrackLimits(w1, 1, chrome);
        Check.That(fmin == fmax && fmin == new Size(816, 635), "a window that does not resize keeps its size");
        var w2 = new MacWindow { Id = 2, Size = new double[] { 800, 600 }, Min = new double[] { 500, 400 }, Max = new double[] { 300, 200 } };
        var (m2, x2) = ProxyGeometry.TrackLimits(w2, 1, Size.Empty);
        Check.That(x2!.Value.Width >= m2!.Value.Width && x2.Value.Height >= m2.Value.Height, "a max under the min never wins over it");
        var (n3, x3) = ProxyGeometry.TrackLimits(new MacWindow { Id = 3, Size = new double[] { 800, 600 } }, 1, chrome);
        Check.That(n3 == null && x3 == null, "no limits from the Mac: none here");
        Check.Equal(new Size(616, 471), ProxyGeometry.Clamp(new Size(100, 2000), new Size(616, 300), new Size(5000, 471)), "a size held to the limits, side by side");

        var origin = ProxyGeometry.WindowOrigin(new Point(200, 300), 28, 1.5);
        Check.Equal(new Point(200, 258), origin, "a proxy's Mac window starts the cropped title bar above its surface");
        Check.Equal(new Rectangle(260, 291, 330, 465), ProxyGeometry.ChildBounds(origin, new double[] { 40, 22, 220, 310 }, 1.5), "a popup at its owner's top-left plus its rect, at 150 %");
        Check.Equal(new Rectangle(1700, 900, 300, 200), ProxyGeometry.KeepWithin(new Rectangle(1800, 950, 300, 200), new Rectangle(0, 0, 2000, 1100)), "a popup hanging past the screen moves back onto it");
        Check.Equal(new Rectangle(0, 0, 2000, 1100), ProxyGeometry.KeepWithin(new Rectangle(-50, -50, 3000, 3000), new Rectangle(0, 0, 2000, 1100)), "a popup larger than the screen is cut to it");

        var area = new Rectangle(0, 0, 1920, 1040);
        Check.Equal(new Point(100, 100), ProxyGeometry.Cascade(0, new Point(100, 100), new Size(800, 600), area, 32), "the first window opens at the start");
        Check.Equal(new Point(164, 164), ProxyGeometry.Cascade(2, new Point(100, 100), new Size(800, 600), area, 32), "each next a step down and right");
        bool inside = Enumerable.Range(0, 40).All(i => area.Contains(new Rectangle(ProxyGeometry.Cascade(i, new Point(900, 300), new Size(1000, 700), area, 32), new Size(1000, 700))));
        Check.That(inside, "forty cascaded windows all stay on the screen");

        // Kind 46's throttle.
        var t = new PlaceThrottle();
        Check.Equal((1000.0, 700.0), t.Change(1000.2, 699.6, 0, final: false), "the first size of a drag goes at once, in whole points");
        t.Sent(1);
        Check.Equal(null, t.Change(1010, 700, 0.01, false), "the next within 50 ms is held");
        Check.Equal(null, t.Change(1020, 705, 0.03, false), "and replaced by the newer one");
        Check.Equal((1020.0, 705.0), t.Tick(0.05), "the newest goes when the 50 ms are over");
        t.Sent(2);
        Check.Equal(null, t.Tick(0.2), "nothing held, nothing to tick");
        Check.Equal(null, t.Change(1020.4, 705.4, 0.2, false), "the size already sent is not sent again");
        Check.Equal(null, t.Change(1021, 706, 0.3, true), "nor one within a point (a DPI change's rounding)");
        Check.Equal((1030.0, 710.0), t.Change(1030, 710, 0.06, true), "the end of the drag goes at once, inside the interval");
        t.Sent(3);
        Check.That(!t.TakesMacSize(null, resizing: false, 0.07), "with a size out, the Mac's own sizes wait");
        Check.That(!t.TakesMacSize(2, false, 0.08), "an answer to an older size does not apply");
        Check.That(t.TakesMacSize(3, false, 0.09) && t.Outstanding == null, "the answer to the last size applies");
        Check.That(!t.TakesMacSize(null, resizing: true, 0.1), "nothing applies during a drag");
        Check.That(t.TakesMacSize(null, false, 0.1), "with nothing out, the Mac's size applies (an app resized itself)");
        t.Change(900, 600, 1, true); t.Sent(4);
        Check.That(!t.TakesMacSize(null, false, 2.5), "1.5 s after a size went, its answer is still awaited");
        Check.That(t.TakesMacSize(null, false, 3.1), "an answer that never comes is given up on after 2 s");
        t.Settled(800, 500);
        Check.Equal(null, t.Change(800, 500, 5, true), "after taking the Mac's size, that size is not sent back");
        t.Change(850, 520, 6, true); t.Sent(5); t.NewSession();
        Check.That(t.Outstanding == null && t.TakesMacSize(null, false, 6.01), "a new session: no answer is awaited");

        // Random drags: never two sizes within 50 ms but the last, and the last size sent is the drag's end.
        var rng = new Random(46);
        bool spaced = true, endSent = true;
        for (int run = 0; run < 200; run++)
        {
            var th = new PlaceThrottle();
            double now = run * 100, lastAt = double.NegativeInfinity;
            (double, double)? lastSent = null;
            double w = 800, h = 600;
            int n = rng.Next(5, 80);
            for (int i = 0; i < n; i++)
            {
                now += rng.NextDouble() * 0.03;
                w += rng.Next(-10, 11); h += rng.Next(-10, 11);
                bool final = i == n - 1;
                var s = th.Change(w, h, now, final) ?? th.Tick(now);
                if (s is { } sent)
                {
                    if (!final && now - lastAt < PlaceWindow.MinInterval - 1e-9) spaced = false;
                    lastAt = now; lastSent = sent;
                }
            }
            if (lastSent is not { } ls || Math.Abs(ls.Item1 - Math.Round(w)) > 1 || Math.Abs(ls.Item2 - Math.Round(h)) > 1) endSent = false;
        }
        Check.That(spaced && endSent, "200 random drags: at most one size every 50 ms, and the drag's end always sent (within a point)");
    }

    // MARK: the controller

    sealed class Link : ICoherenceLink
    {
        public readonly List<(Kind kind, string json)> Sent = new();
        public bool Send(Kind kind, byte[] payload) { Sent.Add((kind, Encoding.UTF8.GetString(payload))); return true; }
        public List<string> Of(Kind k) => Sent.Where(s => s.kind == k).Select(s => s.json).ToList();
        public void Clear() => Sent.Clear();
    }

    sealed class Video : IWindowVideo
    {
        public readonly List<string> Got = new();
        public void Submit(ParameterSets sets) => Got.Add("sets");
        public void Submit(byte[] frame, bool key, double timestamp) => Got.Add(key ? "key" : "frame");
    }

    public static void Controller()
    {
        double now = 0;
        var c = new CoherenceController(() => now) { WindowInput = 2 };
        var link = new Link();
        c.Attach(link);
        c.Plan.Scale = 1.5;
        c.Enter(new uint[] { 101, 102 }, new[] { "com.apple.TextEdit" }, all: false, focus: 101);
        var first = link.Of(Kind.SideStreams);
        Check.That(first.Count == 1, "entering sends kind 29 at once");
        Check.SameJson("""{"windows":[101,102],"fps":30,"wholeApp":true,"screen":[1920,1080],"coherence":true,"focus":101,"focusFPS":60,"apps":["com.apple.TextEdit"],"scale":1.5}""",
                       U(first[0]), "its content: the windows, the focus and its rate, the app, the scale");

        now = 0.02;
        var u = c.Apply(WS("""{"gen":1,"full":true,"windows":[{"id":101,"title":"Readme","size":[800,500],"titleBar":28,"focused":true},{"id":102,"title":"Downloads","size":[900,560]}]}"""));
        Check.That(u.Added.Count == 2 && link.Of(Kind.SideStreams).Count == 1, "kind 45's windows added; the same set again sends no kind 29");

        // Video routing.
        var v101 = new Video();
        var sets = new ParameterSets(4, new List<byte[]> { new byte[] { 0x40, 1, 2 }, new byte[] { 0x42, 1, 2 }, new byte[] { 0x44, 1, 2 } });
        c.Route(Kind.SideParameterSets, SideStream.Pack(101, sets.Encode()), false, 0);
        c.Route(Kind.SideFrame, SideStream.Pack(101, new byte[] { 0, 0, 0, 1, 0x26 }), true, 0);
        var (counts, dropped) = c.TakeFrameCounts();
        Check.That(dropped == 1 && counts.Count == 0, "a frame for a window with no video yet is dropped");
        c.AttachVideo(101, v101);
        Check.That(v101.Got.SequenceEqual(new[] { "sets" }), "its parameter sets were kept and go first when its video is ready");
        c.Route(Kind.SideFrame, SideStream.Pack(101, new byte[] { 0, 0, 0, 1, 0x02 }), false, 0);
        c.Route(Kind.SideFrame, SideStream.Pack(102, new byte[] { 0, 0, 0, 1, 0x02 }), false, 0);
        c.Route(Kind.SideFrame, new byte[] { 1, 2 }, false, 0);
        Check.That(v101.Got.Count == 2 && v101.Got[1] == "frame", "kind 31 goes to its window's video by ID, never another's");
        (counts, dropped) = c.TakeFrameCounts();
        Check.That(counts.GetValueOrDefault(101u) == 1 && dropped == 1, "counted per window; a payload shorter than an ID is nothing");

        // Kind 44 per window.
        link.Clear();
        now = 1;
        c.Interrupted(101);
        Check.That(link.Of(Kind.KeyframeRequest).SequenceEqual(new[] { """{"window":101}""" }), "an interrupted picture asks for its window's keyframe at once");
        now = 1.5; c.Tick();
        Check.Equal(1, link.Of(Kind.KeyframeRequest).Count, "one ask per interruption while its keyframe is due");
        now = 5.1; c.Tick();
        Check.Equal(2, link.Of(Kind.KeyframeRequest).Count, "none came within 4 s: asked again");
        c.PictureBack(101);
        now = 20; c.Tick();
        Check.Equal(2, link.Of(Kind.KeyframeRequest).Count, "the picture back: nothing more");
        c.SetMinimized(101, true);
        c.Interrupted(101);
        Check.Equal(2, link.Of(Kind.KeyframeRequest).Count, "a hidden window's picture asks nothing (its frames are dropped on the Mac)");
        c.SetMinimized(101, false);
        now = 20.3; c.Tick();
        Check.Equal(3, link.Of(Kind.KeyframeRequest).Count, "shown again: its keyframe is asked for");

        // Focus, activation and the hidden list.
        link.Clear();
        now = 30;
        c.Activated(102);
        Check.That(link.Sent[0].kind == Kind.WindowCommand && link.Sent[0].json.Contains("\"activate\"") && link.Sent[0].json.Contains("102"), "activation sends kind 15 activate");
        var k29 = link.Of(Kind.SideStreams);
        Check.That(k29.Count == 1 && JsonNode.Parse(k29[0])!["focus"]!.GetValue<uint>() == 102, "and kind 29 with the new focus");
        now = 30.02;
        c.SetMinimized(101, true);
        Check.That(link.Of(Kind.SideStreams).Count == 1, "a second change within 100 ms waits");
        now = 30.1; c.Tick();
        k29 = link.Of(Kind.SideStreams);
        Check.That(k29.Count == 2 && JsonNode.Parse(k29[1])!["hidden"]![0]!.GetValue<uint>() == 101, "and goes at the interval's end, with 101 hidden");
        c.SetMinimized(101, true);
        now = 31; c.Tick();
        Check.Equal(2, link.Of(Kind.SideStreams).Count, "the same minimize again changes nothing");
        c.SetMinimized(102, true);
        now = 31.2; c.Tick();
        var last = JsonNode.Parse(link.Of(Kind.SideStreams)[^1])!;
        Check.That(last["focus"] == null && last["hidden"]!.AsArray().Count == 2, "minimizing the focused window drops the focus; both hidden");
        c.SetMinimized(101, false); c.SetMinimized(102, false);
        c.SetScale(2); now = 32; c.Tick();
        Check.That(JsonNode.Parse(link.Of(Kind.SideStreams)[^1])!["scale"]!.GetValue<double>() == 2, "a DPI change sends the new scale");

        // Kind 46.
        link.Clear();
        now = 40;
        c.Resized(101, 850, 520, final: false);
        c.Resized(101, 860, 530, final: false);
        now = 40.06; c.Tick();
        c.Resized(101, 900, 560, final: true);
        var places = link.Of(Kind.PlaceWindow);
        Check.That(places.Count == 3 && places[0].Contains("[850,520]") && places[1].Contains("[860,530]") && places[2].Contains("[900,560]"), $"a drag's sizes: the first, the newest after 50 ms, the end ({string.Join(" ", places)})");
        Check.That(places.Select(p => JsonNode.Parse(p)!["token"]!.GetValue<int>()).SequenceEqual(new[] { 1, 2, 3 }), "each with the next token");
        Check.That(!c.TakesMacSize(101, 2, resizing: false), "an answer to an older size is not applied");
        Check.That(c.TakesMacSize(101, 3, false), "the answer to the end of the drag is");
        c.Resized(999, 100, 100, true);
        Check.Equal(3, link.Of(Kind.PlaceWindow).Count, "no size for a window this PC does not have");

        // Close, take-back, removal.
        link.Clear();
        c.Close(102);
        Check.That(link.Sent.Single().json.Contains("\"close\"") && c.Set.Contains(102), "close sends kind 15 close; the window stays until the Mac drops it");
        now = 50;
        u = c.Apply(WS("""{"gen":2,"windows":[{"id":150,"role":"menu","owner":101,"rect":[10,10,200,300],"inCrop":false}]}"""));
        Check.That(u.Added.Single().Id == 150, "a popup that spills out");
        now = 50.2; c.Tick();
        Check.That(JsonNode.Parse(link.Of(Kind.SideStreams)[^1])!["windows"]!.AsArray().Select(n => n!.GetValue<uint>()).SequenceEqual(new uint[] { 101, 102, 150 }), "is added to kind 29's windows");
        u = c.TakenBack(new TakenBack { Windows = { 101 } });
        Check.That(u.Removed.Select(w => w.Id).OrderBy(x => x).SequenceEqual(new uint[] { 101, 150 }), "kind 41 removes the window and its popup");
        now = 50.4; c.Tick();
        Check.That(JsonNode.Parse(link.Of(Kind.SideStreams)[^1])!["windows"]!.AsArray().Count == 1, "kind 29 follows");
        c.Route(Kind.SideFrame, SideStream.Pack(101, new byte[] { 1 }), false, 0);
        Check.That(v101.Got.Count == 2, "a removed window's video gets nothing more");

        // A reconnect.
        var link2 = new Link();
        now = 60;
        c.Attach(link2);
        Check.That(link2.Of(Kind.SideStreams).Count == 1, "a new session in Coherence asks for the set at once");
        Check.That(c.Apply(WS("""{"gen":1,"windows":[{"id":102,"title":"again"}]}""")).Applied, "its kind 45s start again at any generation");

        // Leaving.
        link2.Clear();
        var shown = c.Leave();
        Check.That(shown.Count == 1 && link2.Sent.Single().json == """{"windows":[]}""" && !c.Active, "leaving sends kind 29 with no window and forgets the set");
        Check.That(!c.Apply(WS("""{"gen":5,"windows":[{"id":1}]}""")).Applied && c.Set.Count == 0, "kind 45 outside Coherence changes nothing");
        c.Route(Kind.SideParameterSets, SideStream.Pack(7, sets.Encode()), false, 0);
        c.AttachVideo(7, new Video());
        Check.That(true, "kind 30 outside Coherence is not kept");

        StopAndShowAgain();

        // Aiming input.
        var move = InputEvents.Pointer(PointerAction.Move, 0.5, 0.25);
        var click = InputEvents.Pointer(PointerAction.LeftDown, 0.5, 0.25);
        var key = InputEvents.Key(4, true, 0);
        Check.SameJson("""{"pointer":{"_0":"leftDown","x":0.5,"y":0.25},"window":7}""", CoherenceController.Aim(click, 7, InputKind.Button, 2, false).payload, "windowInput 2: a click carries its window");
        Check.That(!CoherenceController.Aim(click, 7, InputKind.Button, 2, false).activateFirst, "and needs no activate first");
        Check.SameJson("""{"key":{"hidUsage":4,"down":true,"modifiers":0},"window":7}""", CoherenceController.Aim(key, 7, InputKind.Key, 2, true).payload, "a key carries its window");
        Check.SameJson("""{"pointer":{"_0":"move","x":0.5,"y":0.25},"window":7}""", CoherenceController.Aim(move, 7, InputKind.Move, 1, false).payload, "windowInput 1: a move carries its window");
        var gen1Click = CoherenceController.Aim(click, 7, InputKind.Button, 1, false);
        Check.That(Encoding.UTF8.GetString(gen1Click.payload) == Encoding.UTF8.GetString(click) && gen1Click.activateFirst, "windowInput 1: a click goes as before, after an activate of its window");
        Check.That(!CoherenceController.Aim(click, 7, InputKind.Button, 1, true).activateFirst, "… unless it is the focus already");
        Check.That(Encoding.UTF8.GetString(CoherenceController.Aim(key, 7, InputKind.Key, 1, true).payload) == Encoding.UTF8.GetString(key), "windowInput 1: a key goes as before (to the key window)");
    }

    static uint[] Windows(string kind29) => JsonNode.Parse(kind29)!["windows"]!.AsArray().Select(n => n!.GetValue<uint>()).ToArray();

    /// <summary>A window stopped and shown again in every order against the Mac's kind 45s (the contract,
    /// kind 45): this PC never shows a window without its entry, never undoes its own stop, and a removal
    /// that crossed the kind 29 asking for a window again leaves it asked for.</summary>
    static void StopAndShowAgain()
    {
        double t = 0;
        var c = new CoherenceController(() => t) { WindowInput = 2 };
        var link = new Link();
        c.Attach(link);
        c.Enter(new uint[] { 1, 2 }, focus: 1);
        c.Apply(WS("""{"gen":1,"full":true,"windows":[{"id":1,"title":"One"},{"id":2,"title":"Two","size":[640,480]}]}"""));
        Check.That(link.Of(Kind.SideStreams).Count == 1, "two windows shown");

        // Within 100 ms of the last kind 29: stopped and shown again, and the coalescer sends nothing, so the
        // Mac never learns of the stop and sends nothing more of the window.
        t = 1; c.SetScale(2);
        int sent = link.Of(Kind.SideStreams).Count;
        t = 1.03;
        var u = c.StopShowing(2);
        Check.That(u.Removed.Single().Id == 2 && !c.Set.Contains(2) && link.Of(Kind.SideStreams).Count == sent, "stopped within 100 ms of the last kind 29: closed here, kind 29 held");
        t = 1.06;
        u = c.Add(2);
        Check.That(u.Added.Single().Id == 2 && c.Set[2]!.Title == "Two" && c.Set[2]!.SizePoints == (640, 480), "shown again at once, from the entry kept");
        t = 1.2; c.Tick();
        Check.That(link.Of(Kind.SideStreams).Count == sent, "the request is the one the Mac has: nothing goes, and the Mac never saw the stop");
        c.Apply(WS("""{"gen":2,"windows":[{"id":1,"title":"One!"}]}"""));
        Check.That(c.Set.Contains(2) && c.Request.Windows.Contains(2u), "the Mac's next kind 45 says nothing of it, and it stays shown and asked for");

        // Stopped, kind 29 without it gone, shown again; then the removal the Mac wrote for the stop arrives
        // after the kind 29 that asks for it again went out: it stays asked for, and comes back whole.
        t = 2;
        c.StopShowing(2);
        var k29 = link.Of(Kind.SideStreams);
        Check.That(!Windows(k29[^1]).Contains(2u), "stopped: kind 29 without it at once");
        t = 2.02;
        u = c.Add(2);
        Check.That(u.Added.Single().Id == 2, "shown again from the entry kept");
        t = 2.15; c.Tick();
        Check.That(Windows(link.Of(Kind.SideStreams)[^1]).Contains(2u), "kind 29 asks for it again");
        u = c.Apply(WS("""{"gen":3,"removed":[2]}"""));
        Check.That(u.Removed.Single().Id == 2 && !c.Set.Holds(2) && c.Plan.Asked.Contains(2u),
                   "the removal written before the Mac read that kind 29: the entry goes, the window stays asked for");
        t = 2.3; c.Tick();
        Check.That(Windows(link.Of(Kind.SideStreams)[^1]).Contains(2u) && c.Request.Windows.Contains(2u), "and its kind 29s go on listing it");
        u = c.Apply(WS("""{"gen":4,"windows":[{"id":2,"title":"Two","size":[640,480]}]}"""));
        Check.That(u.Added.Single().Id == 2 && c.Set.Contains(2) && !c.Plan.Asked.Contains(2u), "the Mac reads it and sends the whole entry: shown again");

        // Stopped; a kind 45 the Mac wrote before it read the stop still brings the window: it stays stopped.
        t = 3;
        c.StopShowing(2);
        int before = link.Of(Kind.SideStreams).Count;
        u = c.Apply(WS("""{"gen":5,"full":true,"windows":[{"id":1},{"id":2,"title":"Two (edited)"}]}"""));
        t = 3.2; c.Tick();
        Check.That(!u.Any && !c.Set.Contains(2) && link.Of(Kind.SideStreams).Count == before && !Windows(link.Of(Kind.SideStreams)[^1]).Contains(2u),
                   "a full kind 45 written before the Mac read the stop shows nothing again, and asks for nothing");
        u = c.Apply(WS("""{"gen":6,"removed":[2]}"""));
        Check.That(!u.Any && !c.Set.Holds(2) && !c.Plan.Asked.Contains(2u), "its removal: the entry kept goes, nothing asked");
        t = 3.4;
        c.Add(2);
        Check.That(c.Plan.Asked.Contains(2u) && !c.Set.Contains(2), "shown again after its removal: asked for as any window");
        c.Apply(WS("""{"gen":7,"windows":[{"id":2,"title":"Two (edited)"}]}"""));
        Check.That(c.Set[2]?.Title == "Two (edited)", "and shown when the Mac lists it");

        // Stopped, shown again, stopped again, each kind 29 sent; the Mac's answers to all three arrive after
        // them: the removal for the first stop, the entry for the show, the removal for the second stop. Shown
        // again once more before that last removal arrives, the window stays asked for.
        t = 5; c.StopShowing(2);
        t = 5.12; c.Add(2);
        t = 5.24; c.StopShowing(2);
        Check.That(link.Of(Kind.SideStreams).TakeLast(3).Select(k => Windows(k).Contains(2u)).SequenceEqual(new[] { false, true, false }), "three kind 29s: without it, with it, without it");
        u = c.Apply(WS("""{"gen":9,"removed":[2]}"""));
        Check.That(!u.Any && !c.Set.Holds(2), "the first stop's removal: the entry kept goes");
        u = c.Apply(WS("""{"gen":10,"windows":[{"id":2,"title":"Two (again)"}]}"""));
        Check.That(!u.Any && c.Set.Holds(2) && !c.Set.Contains(2), "the show's entry: kept out of sight, the window is stopped again");
        t = 5.36;
        u = c.Add(2);
        Check.That(u.Added.Single().Id == 2 && c.Set[2]!.Title == "Two (again)", "shown again from it");
        u = c.Apply(WS("""{"gen":11,"removed":[2]}"""));
        Check.That(u.Removed.Single().Id == 2 && c.Plan.Asked.Contains(2u) && c.Request.Windows.Contains(2u),
                   "the second stop's removal, after this PC showed it again: still asked for");

        // A window this PC never stopped, removed by the Mac (closed there): not asked for again.
        t = 6;
        u = c.Apply(WS("""{"gen":12,"removed":[1]}"""));
        t = 6.2; c.Tick();
        Check.That(u.Removed.Single().Id == 1 && !c.Plan.Asked.Contains(1u) && !Windows(link.Of(Kind.SideStreams)[^1]).Contains(1u),
                   "a window closed on the Mac: gone from kind 29");
    }

    public static void Equal(double expected, double actual, double tolerance, string what) => Check.That(Math.Abs(expected - actual) <= tolerance, $"{what} ({actual})");
}

/// <summary>Coherence/Occlusion.cs: a window wholly under others is paused; any sliver keeps it.</summary>
static class OcclusionChecks
{
    public static void Run()
    {
        var B = (int l, int t, int r, int b) => new Sill.Windows.Coherence.Occlusion.Box(l, t, r, b);
        var target = B(100, 100, 500, 400);
        Check.That(!Sill.Windows.Coherence.Occlusion.Covered(target, Array.Empty<Sill.Windows.Coherence.Occlusion.Box>()), "nothing above: shown");
        Check.That(Sill.Windows.Coherence.Occlusion.Covered(target, new[] { B(0, 0, 1000, 1000) }), "one window over all of it: covered");
        Check.That(Sill.Windows.Coherence.Occlusion.Covered(target, new[] { B(100, 100, 500, 400) }), "exactly its size: covered");
        Check.That(!Sill.Windows.Coherence.Occlusion.Covered(target, new[] { B(101, 100, 500, 400) }), "a one-pixel column left: shown");
        Check.That(Sill.Windows.Coherence.Occlusion.Covered(target, new[] { B(0, 0, 300, 1000), B(300, 0, 600, 1000) }), "two side by side, meeting: covered");
        Check.That(!Sill.Windows.Coherence.Occlusion.Covered(target, new[] { B(0, 0, 299, 1000), B(300, 0, 600, 1000) }), "two side by side, a gap of one: shown");
        Check.That(Sill.Windows.Coherence.Occlusion.Covered(target, new[] { B(0, 0, 1000, 250), B(0, 250, 200, 400), B(150, 240, 520, 420) }), "three overlapping pieces: covered");
        Check.That(!Sill.Windows.Coherence.Occlusion.Covered(target, new[] { B(0, 0, 1000, 250), B(0, 250, 200, 400) }), "the bottom right uncovered: shown");
        Check.That(!Sill.Windows.Coherence.Occlusion.Covered(target, new[] { B(600, 600, 700, 700) }), "a window elsewhere: shown");
        Check.That(!Sill.Windows.Coherence.Occlusion.Covered(B(10, 10, 10, 50), new[] { B(0, 0, 100, 100) }), "an empty window is never covered");
        Check.That(!Sill.Windows.Coherence.Occlusion.Covered(target, new[] { B(200, 200, 100, 100) }), "an empty box above counts for nothing");
        // A grid of tiles covering it exactly, in shuffled order.
        var rnd = new Random(7);
        var tiles = new List<Sill.Windows.Coherence.Occlusion.Box>();
        for (int x = 100; x < 500; x += 40) for (int y = 100; y < 400; y += 30) tiles.Add(B(x, y, x + 40, y + 30));
        var shuffled = tiles.OrderBy(_ => rnd.Next()).ToList();
        Check.That(Sill.Windows.Coherence.Occlusion.Covered(target, shuffled), $"a grid of {tiles.Count} tiles in any order: covered");
        shuffled.RemoveAt(rnd.Next(shuffled.Count));
        Check.That(!Sill.Windows.Coherence.Occlusion.Covered(target, shuffled), "the grid less one tile: shown");
        Check.That(!Sill.Windows.Coherence.Occlusion.Hide(true, 1) && Sill.Windows.Coherence.Occlusion.Hide(true, 2) && !Sill.Windows.Coherence.Occlusion.Hide(false, 5),
            "paused after two covered looks in a row, never when not covered");
    }
}
