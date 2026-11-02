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
///   order (stale and repeated ones too, taken-back and stopped windows, new sessions) and must hold that
///   set after every one: each window's fields but `controls` (this client draws its own frame), Secure
///   Input, and the windows that need a stream of their own.</item>
/// <item>"kind29-walks", Windows → Mac: this client's real <see cref="CoherenceController"/> and
///   <see cref="SideStreamPlan"/> on a simulated clock, driven through seeded random states (kind 45s naming
///   windows and their children, activations, windows minimized here, stopped, added, apps, scale, screen,
///   rates, kind 41, leaving), write each kind 29 they send with what this client means by it (which windows
///   stream, at which rate, which are hidden, each child's owner as the Mac named it). Run with
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
        int walk = -1, lines = 0, messages = 0, stale = 0, compared = 0;
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
        Check.That(walks.Count >= 10 && messages >= 200 && stale >= 10 && compared >= 250,
                   $"the Mac's walks read whole: {lines} lines, {walks.Count} walks, {messages} messages ({stale} stale), {compared} sets compared");
    }

    /// <summary>The set against what the contract gives, or what differs.</summary>
    static string? Compare(WindowSet set, JsonObject expect)
    {
        var want = expect["windows"]!.AsArray().Select(n => n!.AsObject()).ToList();
        var wantIds = want.Select(o => o["id"]!.GetValue<uint>()).OrderBy(x => x).ToList();
        var haveIds = set.All.Select(x => x.Id).OrderBy(x => x).ToList();
        if (!wantIds.SequenceEqual(haveIds)) return $"windows [{string.Join(",", haveIds)}], the contract gives [{string.Join(",", wantIds)}]";
        foreach (var o in want)
        {
            var w = set[o["id"]!.GetValue<uint>()]!;
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
            if (diff.Count > 0) return $"window {w.Id}: {string.Join("; ", diff)}";
        }
        bool secure = expect["secureInput"]!.GetValue<bool>();
        if (secure != set.SecureInput) return $"Secure Input {set.SecureInput} (want {secure})";
        var streamed = expect["streamed"]!.AsArray().Select(n => n!.GetValue<uint>()).OrderBy(x => x).ToList();
        var have = set.Streamed.Select(x => x.Id).OrderBy(x => x).ToList();
        if (!streamed.SequenceEqual(have)) return $"streamed [{string.Join(",", have)}], the contract gives [{string.Join(",", streamed)}]";
        return null;
    }

    // MARK: kind 29, to the Mac

    public static void Kind29Walks()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "kind29-walks.jsonl");
        var lines = Kind29Lines(out var summary);
        Check.That(lines.Count >= 100, $"the client's kind 29 walks: {summary}");
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
        var lines = Kind29Lines(out var summary);
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        Console.WriteLine($"wrote {path}: {summary}");
        return 0;
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

    sealed class Recorder : ICoherenceLink
    {
        public readonly List<byte[]> Requests = new();
        public bool Send(Kind kind, byte[] payload)
        {
            if (kind == Kind.SideStreams) Requests.Add(payload);
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

    /// <summary>The walks: each kind 29 this client sends, with what it means.</summary>
    static List<string> Kind29Lines(out string summary)
    {
        var lines = new List<string>();
        int requests = 0, leaves = 0, childFocus = 0, hiddenChildren = 0;
        const int Walks = 12, Steps = 36;
        for (int walk = 1; walk <= Walks; walk++)
        {
            var r = new Rand(0x29_0000UL + (ulong)walk * 104729UL);
            double t = 0;
            var link = new Recorder();
            var c = new CoherenceController(() => t);
            var host = new Host();
            int step = 0, seen = 0;
            c.Attach(link);

            void Record(bool leaving = false)
            {
                for (; seen < link.Requests.Count; seen++)
                {
                    var payload = link.Requests[seen];
                    var req = Json.Decode<SideStreamRequest>(payload)!;
                    host.Asked = new HashSet<uint>(req.Windows);
                    var line = new JsonObject { ["walk"] = walk, ["step"] = step, ["request"] = JsonNode.Parse(payload) };
                    if (leaving) { line["meaning"] = new JsonObject { ["leave"] = true }; leaves++; }
                    else
                    {
                        var (meaning, owners, focusChild, hiddenKids) = Meaning(c);
                        if (owners.Count > 0) line["owners"] = owners;
                        line["meaning"] = meaning;
                        requests++;
                        if (focusChild) childFocus++;
                        hiddenChildren += hiddenKids;
                    }
                    lines.Add(line.ToJsonString());
                }
            }

            // A few Mac windows; the device enters Coherence with some of them.
            for (int i = 0; i < 2 + r.Int(3); i++) host.NewTop();
            var first = host.Tops.Where(_ => r.Int(3) > 0).ToList();
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
                                if (listed.Count > 0 && shown < 6)
                                {
                                    var role = r.Pick(new[] { "menu", "popover", "tooltip", "sheet", "dialog", "panel" })!;
                                    host.NewChild(r.Pick(listed), role, r.Int(3) switch { 0 => true, 1 => false, _ => null });
                                }
                                break;
                            case 1:
                                if (r.Pick(host.Children.Values.ToList()) is { } k)
                                    host.Children[k.Id] = k with { InCrop = shown < 6 ? !(k.InCrop ?? true) : true };
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
                                if ((c.Plan.All || c.Plan.Apps.Count > 0) && shown < 5) host.Joined.Add(host.NewTop());
                                else host.NewTop();
                                break;
                        }
                        c.Apply(host.Message(r.OneIn(5)));
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
                        if (r.Pick(c.Set.Streamed.Where(w => w.Owner == null).ToList()) is { } stop) c.StopShowing(stop.Id);
                        break;
                    case 7:
                        if (shown < 5)
                        {
                            var other = host.Tops.Where(id => !c.Set.Contains(id) && !host.Refused.Contains(id)).ToList();
                            c.Add(other.Count > 0 && !r.OneIn(4) ? r.Pick(other) : 9000u + (uint)r.Int(50));   // or one the Mac never lists
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
                        }
                        break;
                    case 13:
                        // A new session (a reconnect): the set asked for again at once, then the Mac's first, full kind 45.
                        if (r.OneIn(3)) { host.NewSession(); c.Attach(link); Record(); c.Apply(host.Message(true)); }
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
        summary = $"{lines.Count} lines in {Walks} walks, {requests} requests, {leaves} ends, {childFocus} with a child in front, {hiddenChildren} children hidden with their owner";
        return lines;
    }

    /// <summary>What this client means by the kind 29 it just sent, from its own state: every window it
    /// shows that needs a stream (a top-level window, a child spilling out of its owner's picture) and every
    /// window it asked for, each at the focus rate when it is the window in front or a child of it, else at
    /// `fps`; hidden when minimized here, or a child of a window minimized here, never the window in front.
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
            windows.Add(new JsonObject { ["id"] = id, ["fps"] = front ? focusFps : fps, ["hidden"] = hidden });
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
