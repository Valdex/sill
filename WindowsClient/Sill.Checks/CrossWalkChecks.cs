using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sill.Windows.Coherence;
using Sill.Windows.Protocol;

namespace Sill.Checks;

/// <summary>Coherence's semantics across the two languages (docs/coherence-protocol.md), where
/// <see cref="SwiftWireChecks"/> checks only that each side reads and writes the other's JSON. Two
/// directions, each a checked-in fixture (Tests/checks/coherence-walks/main.swift has the Mac's half):
/// <list type="bullet">
/// <item>"swift-walks", Mac → Windows: Fixtures/swift-walks.jsonl, written by Tests/checks/coherence-walks
///   from seeded walks of a simulated Mac through the host's real kind 45 code, each message with the set
///   a receiver holds after it by the contract. This client's real <see cref="WindowSet"/> applies them in
///   order (stale and repeated ones too, taken-back windows, windows stopped and shown again at once,
///   new sessions) and must hold that set after every one: each window's fields but `controls` (this
///   client draws its own frame), Secure Input, the windows that need a stream of their own, and the
///   entries it keeps out of sight for the windows it stopped showing.</item>
/// <item>"kind29-walks", Windows → Mac: this client's real <see cref="CoherenceController"/> and
///   <see cref="SideStreamPlan"/> on a simulated clock, driven through seeded random states (kind 45s naming
///   windows and their children, activations, windows minimized here, stopped, added, apps, scale, screen,
///   rates, kind 41, leaving; windows stopped and shown again at once, the Mac's removals crossing the
///   kind 29 that asks again; more windows than the Mac streams), write each kind 29 they send with what
///   this client means by it (which windows stream, at which rate, which are in front and which hidden,
///   each child's owner as the Mac named it). The walk also holds the client to the race's rules: no kind 29
///   lists a window it stopped, none drops one it shows, and every window the Mac counts as this client's
///   is one it holds, so the Mac never leaves out a window this client shows. Run with
///   `--write-kind29 PATH` it writes Tests/checks/coherence-walks/kind29-walks.jsonl, which the Mac's check
///   reads as the host does; as a group it fails when the client no longer writes that file.</item>
/// </list></summary>
static class CrossWalkChecks
{
    // MARK: kind 45, from the Mac

    public static void SwiftWalks()
    {
        var path = Environment.GetEnvironmentVariable("SILL_SWIFT_WALKS") ?? Path.Combine(AppContext.BaseDirectory, "Fixtures", "swift-walks.jsonl");
        Check.That(File.Exists(path), "the Mac's kind 45 walks are there");
        if (!File.Exists(path)) return;
        var set = new WindowSet();
        int walk = -1, lines = 0, messages = 0, stale = 0, compared = 0, stops = 0, shownFromKept = 0;
        var walks = new List<int>();
        var failed = new Dictionary<int, string>();
        foreach (var raw in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            lines++;
            var node = JsonNode.Parse(raw)!.AsObject();
            int w = (int)node["walk"]!, step = (int)node["step"]!;
            var ev = (string)node["event"]!;
            if (w != walk) { walk = w; walks.Add(w); set = new WindowSet(); }
            string? problem = null;
            switch (ev)
            {
                case "start":
                    set = new WindowSet();
                    break;
                case "session":
                    set.ResetGeneration();
                    break;
                case "45":
                {
                    var m = Json.Decode<WindowState>(Encoding.UTF8.GetBytes(node["json"]!.ToJsonString()));
                    if (m == null) { problem = "the message does not decode"; break; }
                    messages++;
                    var u = set.Apply(m);
                    if (node["applied"] is JsonValue applied)
                    {
                        bool want = applied.GetValue<bool>();
                        if (!want) stale++;
                        if (u.Applied != want) { problem = want ? "a newer message dropped" : $"an older generation ({m.Gen}) applied"; break; }
                        if (!want) break;
                        if (u.Full != node["full"]!.GetValue<bool>()) problem = $"full {u.Full}";
                        int? answering = node["answering"]?.GetValue<int>();
                        if (u.Answering != answering) problem = $"answering {u.Answering}, the Mac answered {answering}";
                    }
                    break;
                }
                case "41":
                {
                    var tb = Json.Decode<TakenBack>(Encoding.UTF8.GetBytes(node["json"]!.ToJsonString()));
                    if (tb == null) { problem = "kind 41 does not decode"; break; }
                    set.TakeBack(tb.Windows);
                    break;
                }
                case "forget":
                    set.Forget(node["window"]!.GetValue<uint>());
                    stops++;
                    break;
                case "show":
                    // A window stopped, shown again: from its entry if a kind 45 has not removed it.
                    bool fromKept = set.Holds(node["window"]!.GetValue<uint>());
                    set.Show(node["window"]!.GetValue<uint>());
                    if (fromKept) shownFromKept++;
                    break;
                default:
                    problem = $"an event this check does not know: {ev}";
                    break;
            }
            if (problem == null && node["expect"] is JsonObject expect) { problem = Compare(set, expect); compared++; }
            if (problem != null && !failed.ContainsKey(w)) failed[w] = $"step {step} ({ev}): {problem}\n     line {raw[..Math.Min(raw.Length, 400)]}";
        }
        foreach (var w in walks)
            Check.That(!failed.ContainsKey(w), failed.TryGetValue(w, out var p) ? $"walk {w}: {p}" : $"kind 45 walk {w}: after every message the set is the one the contract gives");
        Check.That(walks.Count >= 10 && messages >= 200 && stale >= 10 && compared >= 250 && stops >= 10 && shownFromKept >= 5,
                   $"the Mac's walks read whole: {lines} lines, {walks.Count} walks, {messages} messages ({stale} stale), {compared} sets compared, "
                   + $"{stops} windows stopped, {shownFromKept} shown again from the entry kept");
    }

    /// <summary>The set against what the contract gives, or what differs: the windows shown, and those
    /// stopped and kept out of sight (`stopped`, none when absent).</summary>
    static string? Compare(WindowSet set, JsonObject expect)
    {
        if (CompareWindows(set.All.ToList(), expect["windows"]!.AsArray(), "windows") is { } shown) return shown;
        if (CompareWindows(set.Kept.ToList(), expect["stopped"]?.AsArray() ?? new JsonArray(), "kept out of sight") is { } kept) return kept;
        bool secure = expect["secureInput"]!.GetValue<bool>();
        if (secure != set.SecureInput) return $"Secure Input {set.SecureInput} (want {secure})";
        var streamed = expect["streamed"]!.AsArray().Select(n => n!.GetValue<uint>()).OrderBy(x => x).ToList();
        var have = set.Streamed.Select(x => x.Id).OrderBy(x => x).ToList();
        if (!streamed.SequenceEqual(have)) return $"streamed [{string.Join(",", have)}], the contract gives [{string.Join(",", streamed)}]";
        return null;
    }

    static string? CompareWindows(List<MacWindow> windows, JsonArray expected, string what)
    {
        var want = expected.Select(n => n!.AsObject()).ToList();
        var wantIds = want.Select(o => o["id"]!.GetValue<uint>()).OrderBy(x => x).ToList();
        var haveIds = windows.Select(x => x.Id).OrderBy(x => x).ToList();
        if (!wantIds.SequenceEqual(haveIds)) return $"{what} [{string.Join(",", haveIds)}], the contract gives [{string.Join(",", wantIds)}]";
        foreach (var o in want)
        {
            var w = windows.First(x => x.Id == o["id"]!.GetValue<uint>());
            var diff = new List<string>();
            void Str(string key, string? have) { var e = (string?)o[key]; if (e != have) diff.Add($"{key} {have ?? "-"} (want {e ?? "-"})"); }
            void Arr(string key, double[]? have)
            {
                var e = o[key]?.AsArray().Select(x => x!.GetValue<double>()).ToArray();
                bool same = e == null ? have == null : have != null && e.SequenceEqual(have);
                if (!same) diff.Add($"{key} [{(have == null ? "-" : string.Join(",", have))}] (want [{(e == null ? "-" : string.Join(",", e))}])");
            }
            void Flag(string key, bool have) { bool e = o[key]?.GetValue<bool>() ?? false; if (e != have) diff.Add($"{key} {have} (want {e})"); }
            Str("app", w.App); Str("appName", w.AppName); Str("title", w.Title); Str("role", w.RawRole);
            Arr("size", w.Size); Arr("min", w.Min); Arr("max", w.Max); Arr("rect", w.Rect);
            bool? resizable = o["resizable"]?.GetValue<bool>();
            if (resizable != w.Resizable) diff.Add($"resizable {w.Resizable} (want {resizable})");
            Flag("focused", w.Focused); Flag("minimized", w.Minimized); Flag("fullScreen", w.FullScreen); Flag("modal", w.Modal); Flag("joined", w.Joined);
            double titleBar = o["titleBar"]?.GetValue<double>() ?? 0;
            if (titleBar != w.TitleBar) diff.Add($"titleBar {w.TitleBar} (want {titleBar})");
            uint? owner = o["owner"]?.GetValue<uint>();
            if (owner != w.Owner) diff.Add($"owner {w.Owner} (want {owner})");
            bool? inCrop = o["inCrop"]?.GetValue<bool>();
            if (inCrop != w.InCrop) diff.Add($"inCrop {w.InCrop} (want {inCrop})");
            if (diff.Count > 0) return $"{what}: window {w.Id}: {string.Join("; ", diff)}";
        }
        return null;
    }

    // MARK: kind 29, to the Mac

    public static void Kind29Walks()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "kind29-walks.jsonl");
        var lines = Kind29Lines(out var summary, out var run);
        Check.That(lines.Count >= 100, $"the client's kind 29 walks: {summary}");
        Check.That(run.Problems.Count == 0, run.Problems.Count == 0
            ? "against the walks' Mac: an entry for every window it counts as this PC's; no kind 29 lists a window stopped here or drops one not stopped"
            : $"against the walks' Mac: {string.Join("; ", run.Problems.Take(3))}");
        Check.That(run.Stopped >= 10 && run.ShownFromKept >= 5 && run.Crossed >= 1 && run.HostCounted >= 500,
                   $"the walks stop and show windows again: {run.Stopped} stopped, {run.ShownFromKept} shown again from the entry kept, {run.Crossed} asked for again after a crossing removal");
        Check.That(File.Exists(path), "the kind 29 walks the Mac's check reads are there");
        if (!File.Exists(path)) return;
        var have = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        int first = Enumerable.Range(0, Math.Min(have.Count, lines.Count)).FirstOrDefault(i => have[i] != lines[i], -1);
        bool same = have.Count == lines.Count && first < 0;
        Check.That(same, same ? $"Tests/checks/coherence-walks/kind29-walks.jsonl is what this client sends ({lines.Count} lines)"
            : $"this client's kind 29s differ from Tests/checks/coherence-walks/kind29-walks.jsonl (line {(first < 0 ? Math.Min(have.Count, lines.Count) + 1 : first + 1)}): "
              + "run `dotnet run --project WindowsClient/Sill.Checks -- --write-kind29 Tests/checks/coherence-walks/kind29-walks.jsonl`, then Tests/checks/coherence-walks/run.sh");
    }

    /// <summary>`--write-kind29 PATH`: the walks, written for the Mac's check.</summary>
    public static int WriteKind29(string path)
    {
        var lines = Kind29Lines(out var summary, out var run);
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        Console.WriteLine($"wrote {path}: {summary}");
        foreach (var p in run.Problems) Console.WriteLine($"FAIL {p}");
        return run.Problems.Count == 0 ? 0 : 1;
    }

    /// <summary>SplitMix64: the same numbers on every system.</summary>
    sealed class Rand(ulong seed)
    {
        ulong state = seed;
        public ulong Next()
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        public int Int(int n) => n <= 1 ? 0 : (int)(Next() % (ulong)n);
        public bool OneIn(int n) => Int(n) == 0;
        public T? Pick<T>(IReadOnlyList<T> a) => a.Count == 0 ? default : a[Int(a.Count)];
    }

    /// <summary>Each kind 29 as it went, with what the client meant by it and the windows the walk had stopped
    /// then: taken as it goes, since one call can send a request and change the state after it (a window
    /// stopped and shown again at once).</summary>
    sealed class Recorder : ICoherenceLink
    {
        public sealed record Sent(byte[] Payload, HashSet<uint> Stopped, (JsonObject meaning, JsonArray owners, bool focusChild, int hiddenKids)? Meaning);
        public readonly List<Sent> Requests = new();
        public Func<(HashSet<uint>, (JsonObject, JsonArray, bool, int))>? Capture;
        public bool Send(Kind kind, byte[] payload)
        {
            if (kind != Kind.SideStreams) return true;
            var now = Capture?.Invoke();
            Requests.Add(new Sent(payload, now?.Item1 ?? new(), now?.Item2));
            return true;
        }
    }

    /// <summary>The Mac as the walk's kind 45s describe it, to this one device: the top-level windows the
    /// device asked for (or that joined), their children (owned by a top-level window, as the host names
    /// them), each message's generation.</summary>
    sealed class Host
    {
        public sealed record Child(uint Id, uint Owner, string Role, bool? InCrop);
        public readonly List<uint> Tops = new();                 // every Mac window, listed or not
        public readonly Dictionary<uint, Child> Children = new();
        public readonly HashSet<uint> Joined = new();
        public readonly HashSet<uint> Refused = new();           // taken back
        public HashSet<uint> Asked = new();                       // the device's last kind 29
        readonly Dictionary<uint, string> sent = new();           // what each listed window was last sent as
        /// <summary>The windows the Mac counts as this device's: sent and not removed since.</summary>
        public IEnumerable<uint> SentIds => sent.Keys;
        long gen;
        uint nextId = 100;
        public uint NewTop() { var id = nextId++; Tops.Add(id); return id; }
        public uint NewChild(uint owner, string role, bool? inCrop) { var id = nextId++; Children[id] = new Child(id, owner, role, inCrop); return id; }

        public IEnumerable<uint> ListedTops => Tops.Where(id => !Refused.Contains(id) && (Asked.Contains(id) || Joined.Contains(id)));

        WindowState.Window Entry(uint id) => Children.TryGetValue(id, out var c)
            ? new WindowState.Window { Id = id, App = "com.apple.Safari", Role = c.Role, Owner = c.Owner, Rect = new double[] { 10, 20, 200, 100 }, InCrop = c.InCrop }
            : new WindowState.Window { Id = id, App = "com.apple.Safari", AppName = "Safari", Title = $"w{id}", Role = WindowState.Roles.Window,
                                       Size = new double[] { 800, 600 }, Focused = false, Minimized = false, TitleBar = 28, Joined = Joined.Contains(id) ? true : null };

        /// <summary>The next kind 45: every window the device should have (`full`), else the whole entry of
        /// each that is new or changed, and `removed`.</summary>
        public WindowState Message(bool full)
        {
            var tops = ListedTops.ToList();
            var now = new Dictionary<uint, string>();
            var entries = new List<WindowState.Window>();
            foreach (var id in tops.Concat(Children.Values.Where(c => tops.Contains(c.Owner)).Select(c => c.Id)))
            {
                var e = Entry(id);
                var json = Encoding.UTF8.GetString(Json.Encode(e));
                now[id] = json;
                if (full || !sent.TryGetValue(id, out var was) || was != json) entries.Add(e);
            }
            var removed = sent.Keys.Where(id => !now.ContainsKey(id)).OrderBy(x => x).ToList();
            sent.Clear();
            foreach (var (id, json) in now) sent[id] = json;
            return new WindowState
            {
                Gen = ++gen, Full = full ? true : null, Windows = entries.Count > 0 ? entries : null,
                Removed = !full && removed.Count > 0 ? removed : null,
            };
        }

        /// <summary>A new session: generations start again.</summary>
        public void NewSession() { gen = 0; sent.Clear(); }
    }

    static readonly string[] Bundles = { "com.apple.Safari", "com.apple.TextEdit", "com.apple.Notes" };
    static readonly double[] Scales = { 1.0, 1.25, 1.5, 1.75, 2.0, 2.25, 2.5, 3.0, 3.5 };
    /// <summary>The most windows the walks let this client show: more than the six the Mac streams, so the
    /// Mac's check reads which six it picks (docs/coherence-protocol.md, kind 29).</summary>
    const int Crowd = 9;

    /// <summary>What the walks went through, and what they found wrong.</summary>
    sealed class Kind29Run
    {
        public readonly List<string> Problems = new();
        public int Stopped, ShownFromKept, Crossed, HostCounted;
    }

    /// <summary>The walks: each kind 29 this client sends, with what it means. Checked as they go, against
    /// the walk's Mac (whose view of this PC is the last kind 29 it read, which the coalescer can hold back):
    /// this PC holds an entry for every window the Mac counts as its own (a stopped one kept out of sight); a
    /// kind 29 never lists a window this PC stopped, and never drops one it did not stop that the Mac still
    /// has (a removal written before the Mac read the kind 29 asking for a window again leaves it asked for).</summary>
    static List<string> Kind29Lines(out string summary) => Kind29Lines(out summary, out _);

    static List<string> Kind29Lines(out string summary, out Kind29Run run)
    {
        var lines = new List<string>();
        int requests = 0, leaves = 0, childFocus = 0, hiddenChildren = 0;
        var found = run = new Kind29Run();
        const int Walks = 12, Steps = 36;
        for (int walk = 1; walk <= Walks; walk++)
        {
            var r = new Rand(0x29_0000UL + (ulong)walk * 104729UL);
            double t = 0;
            var link = new Recorder();
            var c = new CoherenceController(() => t);
            var host = new Host();
            int step = 0, seen = 0;
            var stoppedHere = new HashSet<uint>();   // stopped here, not shown again
            var shownAgain = new HashSet<uint>();    // shown again from the entry kept, until the Mac removes it or this PC stops it
            List<uint>? previous = null;             // the windows of the last kind 29
            link.Capture = () => (new HashSet<uint>(stoppedHere), Meaning(c));
            c.Attach(link);

            void Problem(string what) { if (found.Problems.Count < 20) found.Problems.Add($"walk {walk} step {step}: {what}"); }

            void Message(bool full)
            {
                var m = host.Message(full);
                c.Apply(m);
                found.HostCounted += host.SentIds.Count();
                var missing = host.SentIds.Where(id => !c.Set.Holds(id)).ToList();
                if (missing.Count > 0) Problem($"the Mac counts window {string.Join(",", missing)} as this PC's, and this PC has no entry");
                foreach (var id in shownAgain.ToList())
                    if (!c.Set.Holds(id)) { shownAgain.Remove(id); if (c.Plan.Asked.Contains(id)) found.Crossed++; }
            }

            // The walk's intent first, so a request sent inside the call is judged by it.
            void Stop(uint id)
            {
                stoppedHere.Add(id);
                shownAgain.Remove(id);
                c.StopShowing(id);
                found.Stopped++;
            }

            void Show(uint id)
            {
                stoppedHere.Remove(id);
                var u = c.Add(id);
                if (u.Added.Any(w => w.Id == id)) { found.ShownFromKept++; shownAgain.Add(id); }
            }

            void Record(bool leaving = false)
            {
                for (; seen < link.Requests.Count; seen++)
                {
                    var sent = link.Requests[seen];
                    var payload = sent.Payload;
                    var req = Json.Decode<SideStreamRequest>(payload)!;
                    var stoppedThen = sent.Stopped;
                    host.Asked = new HashSet<uint>(req.Windows);
                    var line = new JsonObject { ["walk"] = walk, ["step"] = step, ["request"] = JsonNode.Parse(payload) };
                    if (leaving) { line["meaning"] = new JsonObject { ["leave"] = true }; leaves++; previous = null; }
                    else
                    {
                        foreach (var id in req.Windows.Where(stoppedThen.Contains)) Problem($"kind 29 asks for window {id}, which this PC stopped");
                        foreach (var id in previous ?? new())
                            if (!stoppedThen.Contains(id) && host.Tops.Contains(id) && !host.Refused.Contains(id) && !req.Windows.Contains(id))
                                Problem($"kind 29 drops window {id}, which this PC did not stop and the Mac still has");
                        previous = req.Windows.ToList();
                        var (meaning, owners, focusChild, hiddenKids) = sent.Meaning ?? Meaning(c);
                        if (owners.Count > 0) line["owners"] = owners;
                        line["meaning"] = meaning;
                        requests++;
                        if (focusChild) childFocus++;
                        hiddenChildren += hiddenKids;
                    }
                    lines.Add(line.ToJsonString());
                }
            }

            // A few Mac windows; the device enters Coherence with some of them. Every other walk is crowded: it
            // shows more windows than the six the Mac streams, so the Mac's check reads which it picks.
            bool crowded = walk % 2 == 0;
            if (crowded) { int n = 7 + r.Int(3); for (int i = 0; i < n; i++) host.NewTop(); }
            else for (int i = 0; i < 2 + r.Int(3); i++) host.NewTop();
            var first = crowded ? host.Tops.ToList() : host.Tops.Where(_ => r.Int(3) > 0).ToList();
            if (first.Count == 0) first.Add(host.Tops[0]);
            bool all = r.OneIn(5) || walk % 4 == 0;   // every app's new windows join, in a few walks at least
            var apps = r.OneIn(3) ? new[] { Bundles[r.Int(3)] } : null;
            c.Enter(first, apps, all, r.OneIn(2) ? first[0] : null);
            Record();

            for (step = 1; step <= Steps; step++)
            {
                int shown = c.Plan.Windows(c.Set).Count;
                switch (r.Int(18))
                {
                    case 0: case 1: case 2: case 14: case 15:
                    {
                        // The Mac changes, and its kind 45 says so.
                        var listed = host.ListedTops.ToList();
                        switch (r.Int(7))
                        {
                            case 0: case 6:
                                if (listed.Count > 0 && shown < Crowd)
                                {
                                    var role = r.Pick(new[] { "menu", "popover", "tooltip", "sheet", "dialog", "panel" })!;
                                    host.NewChild(r.Pick(listed), role, r.Int(3) switch { 0 => true, 1 => false, _ => null });
                                }
                                break;
                            case 1:
                                if (r.Pick(host.Children.Values.ToList()) is { } k)
                                    host.Children[k.Id] = k with { InCrop = shown < Crowd ? !(k.InCrop ?? true) : true };
                                break;
                            case 2:
                                if (r.Pick(host.Children.Values.ToList()) is { } gone) host.Children.Remove(gone.Id);
                                break;
                            case 3:
                                if (r.Pick(host.Children.Values.ToList()) is { } moved && r.Pick(listed) is var to && to != 0)
                                    host.Children[moved.Id] = moved with { Owner = to };
                                break;
                            case 4:
                                if (r.Pick(listed) is var closed && closed != 0)
                                {
                                    host.Tops.Remove(closed);
                                    foreach (var k2 in host.Children.Values.Where(k2 => k2.Owner == closed).ToList()) host.Children.Remove(k2.Id);
                                }
                                break;
                            default:
                                // A new window that joins by itself (the device asked for its app, or every app's).
                                if ((c.Plan.All || c.Plan.Apps.Count > 0) && shown < Crowd - 1) host.Joined.Add(host.NewTop());
                                else host.NewTop();
                                break;
                        }
                        Message(r.OneIn(5));
                        break;
                    }
                    case 3: case 16:
                    {
                        // Activated here: a window, or a sheet, dialog or panel spilling out of its owner (popups never activate).
                        var owned = c.Set.Streamed.Where(w => c.Set.PresentationOf(w) == Presentation.OwnedWindow).ToList();
                        var windows = c.Set.Streamed.Where(w => c.Set.PresentationOf(w) == Presentation.Window).ToList();
                        if (r.Pick(owned.Count > 0 && r.OneIn(2) ? owned : windows) is { } act) c.Activated(act.Id);
                        break;
                    }
                    case 4: case 5:
                        if (r.Pick(c.Set.Streamed.Where(w => c.Set.PresentationOf(w) == Presentation.Window).ToList()) is { } min)
                            c.SetMinimized(min.Id, !c.Plan.IsMinimized(min.Id));
                        break;
                    case 6:
                        if (r.Pick(c.Set.Streamed.Where(w => w.Owner == null).ToList()) is { } stop)
                        {
                            Stop(stop.Id);
                            if (r.OneIn(3)) Show(stop.Id);   // shown again at once: the Mac may never learn of the stop
                        }
                        break;
                    case 7:
                        if (shown < Crowd - 1)
                        {
                            // Often one stopped a moment ago (a removal may cross the kind 29 asking for it again).
                            var again = stoppedHere.Where(id => host.Tops.Contains(id) && !host.Refused.Contains(id)).ToList();
                            var other = host.Tops.Where(id => !c.Set.Contains(id) && !host.Refused.Contains(id)).ToList();
                            if (again.Count > 0 && r.OneIn(2)) Show(r.Pick(again));
                            else Show(other.Count > 0 && !r.OneIn(4) ? r.Pick(other) : 9000u + (uint)r.Int(50));   // or one the Mac never lists
                        }
                        break;
                    case 8:
                        c.AddApp(Bundles[r.Int(Bundles.Length)]);
                        break;
                    case 9:
                        c.SetScale(Scales[r.Int(Scales.Length)]);
                        break;
                    case 10:
                        c.SetScreen(new[] { 1920, 2560, 3840, 1366 }[r.Int(4)], new[] { 1080, 1440, 2160, 768 }[r.Int(4)]);
                        break;
                    case 11:
                        c.SetRates(new[] { 0, 10, 15, 24, 30, 45 }[r.Int(6)], new[] { 0, 30, 60, 90, 120, 240 }[r.Int(6)]);
                        break;
                    case 12:
                        if (r.Pick(host.ListedTops.ToList()) is var back && back != 0)
                        {
                            // Taken back at the Mac (kind 41): out of every message to this device from now on.
                            host.Refused.Add(back);
                            c.TakenBack(new TakenBack { Windows = new() { back } });
                            stoppedHere.Remove(back);
                            shownAgain.Remove(back);
                        }
                        break;
                    case 13:
                        // A new session (a reconnect): the set asked for again at once, then the Mac's first, full kind 45.
                        if (r.OneIn(3)) { host.NewSession(); c.Attach(link); Record(); Message(true); }
                        break;
                    default:
                        break;
                }
                Record();
                t += new[] { 0.03, 0.08, 0.15, 0.4, 2.5 }[r.Int(5)];
                c.Tick();
                Record();
            }
            if (walk % 3 == 0) { c.Leave(); Record(leaving: true); }
        }
        summary = $"{lines.Count} lines in {Walks} walks, {requests} requests, {leaves} ends, {childFocus} with a child in front, {hiddenChildren} children hidden with their owner, "
                  + $"{found.Stopped} windows stopped, {found.ShownFromKept} shown again from the entry kept, {found.Crossed} asked for again after a removal that crossed";
        return lines;
    }

    /// <summary>What this client means by the kind 29 it just sent, from its own state: every window it
    /// shows that needs a stream (a top-level window, a child spilling out of its owner's picture) and every
    /// window it asked for, each at the focus rate when it is the window in front or a child of it (`front`,
    /// which streams first when there are more than the Mac streams), else at `fps`; hidden when minimized
    /// here, or a child of a window minimized here, never the window in front.
    /// The scale, the screen and the apps as set; each child's owner as the Mac named it.</summary>
    static (JsonObject meaning, JsonArray owners, bool focusChild, int hiddenChildren) Meaning(CoherenceController c)
    {
        var plan = c.Plan;
        var set = c.Set;
        var ids = set.Streamed.Select(w => w.Id).Concat(plan.Asked.Where(id => !set.Contains(id))).Distinct().OrderBy(x => x).ToList();
        uint? focus = plan.Focus is { } f && ids.Contains(f) ? f : null;
        int fps = Math.Clamp(plan.Fps, 1, SideStreamRequest.MaxFPS), focusFps = Math.Clamp(plan.FocusFps, 1, SideStreamRequest.MaxFocusFPS);
        var windows = new JsonArray();
        bool focusChild = false;
        int hiddenChildren = 0;
        foreach (var id in ids)
        {
            uint? owner = set[id] is { } w ? set.TopOwner(w)?.Id : null;
            bool front = id == focus || (owner != null && owner == focus);
            bool hidden = id != plan.Focus && (plan.IsMinimized(id) || (owner is { } o && o != plan.Focus && plan.IsMinimized(o)));
            if (owner != null && id == focus) focusChild = true;
            if (owner != null && hidden) hiddenChildren++;
            var entry = new JsonObject { ["id"] = id, ["fps"] = front ? focusFps : fps, ["hidden"] = hidden };
            if (front) entry["front"] = true;   // streamed first when there are more than the Mac streams
            windows.Add(entry);
        }
        var owners = new JsonArray();
        foreach (var w in set.All) if (set.TopOwner(w) is { } top) owners.Add(new JsonArray(w.Id, top.Id));
        var meaning = new JsonObject
        {
            ["windows"] = windows, ["focus"] = focus, ["fps"] = fps, ["focusFPS"] = focusFps,
            ["scale"] = plan.Scale, ["screen"] = new JsonArray(Math.Round(plan.Screen.w), Math.Round(plan.Screen.h)),
            ["apps"] = new JsonArray(plan.Apps.Distinct().Select(a => (JsonNode?)a).ToArray()), ["all"] = plan.All,
        };
        if (focus == null) meaning.Remove("focus");
        return (meaning, owners, focusChild, hiddenChildren);
    }
}
