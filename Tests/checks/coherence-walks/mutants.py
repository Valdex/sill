"""Mutants of both sides of Coherence's walks (Tests/checks/coherence-walks): each must make a check fail.
- The host's Swift (CoherencePolicy.swift, SideStreamPolicy.swift), with this check's own Swift side: what each
  kind 45 carries, which listed window is a child, the limits it sends, and how kind 29 is read (its gates,
  rates and scale, and which six of more it streams).
- The host's kind 45 code again, seen from the Windows client: the mutant's walks are written and the
  client's real WindowSet (Sill.Checks, group "swift-walks") must part from what they say a device holds.
- The Windows client's C#: its WindowSet (kind 45's reducer, a stopped window's entry kept out of sight), with
  Sill.Checks' "swift-walks" group; its rules around a stop that the walks' kind 45s alone do not show (a kind 45
  that still brings a stopped window, a removal crossing the kind 29 that asks again), with Sill.Checks' unit
  groups and the kind 29 walks' checks against their own Mac; its SideStreamPlan (kind 29), whose walks are
  written again (`--write-kind29`) and read by this check's Swift side.
The repository is never changed: sources are copied to a temporary directory. Needs swiftc and dotnet.
usage: mutants.py ROOT"""
import os, shutil, subprocess, sys, tempfile
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import module

CHECK = "coherence-walks"
P = "Sources/SillHost/CoherencePolicy.swift"
SP = "Sources/SillHost/SideStreamPolicy.swift"
WS ="WindowsClient/Sill.Windows/Coherence/WindowSet.cs"
PROTO = "WindowsClient/Sill.Windows/Protocol/Coherence.cs"
PLAN = "WindowsClient/Sill.Windows/Coherence/SideStreamPlan.cs"

SWIFT = [
    ("no removed", P, "if !removed.isEmpty { m.removed = removed }", ""),
    ("never full after a silence", P, "if owesFull || lastAt.map({ time - $0 >= Self.gapAfter }) ?? true {", "if owesFull || lastAt == nil {"),
    ("gen not rising", P, "        gen += 1\n        sent =", "        sent ="),
    ("Secure Input only in a full message", P, "if secureInput != sentSecure { m.secureInput = secureInput }", ""),
    ("a listed child a window of its own", P, "        if w.layer != 0 { return true }\n        return listed.contains", "        if w.layer != 0 { return false }\n        return listed.contains"),
    ("a forgotten most left out", P, "(knewMax ? [Self.unknown, Self.unknown] : nil)", "nil"),
    ("kind 29: a child never at its owner's rate", P, "let top = s.owners[id] ?? id", "let top = id"),
    ("kind 29: a child in front not the focus", P, "let front = s.ask.focus == id || s.ask.focus == top", "let front = s.ask.focus == top"),
    ("kind 29: hidden only in front", P, "let hidden = !front && (s.ask.hidden.contains(id)", "let hidden = front && (s.ask.hidden.contains(id)"),
    ("kind 29: the device's focus rate ignored", P, "focusFPS = Self.focusRate(asked: r.focusFPS)", "focusFPS = Self.focusRate(asked: nil)"),
    ("kind 29: more than the knob", P, "return min(knob, CGFloat(largest))", "return CGFloat(largest)"),
    ("kind 29: every app's windows never", P, "all = r.all == true", "all = false"),
    ("kind 29: beyond six, the first listed whatever their place", SP,
     "let queue = asked.enumerated().sorted { (pick[$0.element]!, $0.offset) < (pick[$1.element]!, $1.offset) }.map(\\.element)", "let queue = asked"),
    ("kind 29: beyond six, hidden windows among the shown", P, "if v.hidden { hidden.insert(id) }", ""),
    ("kind 29: beyond six, the focus not first", SP, "front.contains(id) ? .front : hidden", "front.contains(id) ? .shown : hidden"),
]
# Host mutants whose walks the Windows client's WindowSet must reject.
CROSS = [
    ("no removed", P, "if !removed.isEmpty { m.removed = removed }", ""),
    ("gen not rising", P, "        gen += 1\n        sent =", "        sent ="),
    ("Secure Input only in a full message", P, "if secureInput != sentSecure { m.secureInput = secureInput }", ""),
]
REDUCER = [
    ("a full message keeps what it leaves out", WS, "        if (u.Full) foreach (var id in windows.Keys) if (!listed.Contains(id)) gone.Add(id);\n", ""),
    ("children stay when their owner goes", WS, "bool more = true;", "bool more = false;"),
    ("an inCrop left out cleared", WS, "if (e.InCrop != null && e.InCrop != InCrop)", "if (e.InCrop != InCrop)"),
    ("an older generation applied", PROTO, "lastApplied is not { } last || gen > last", "lastApplied is not { } last || gen != last"),
    ("an inCrop left out counted as outside", WS, "if (w.InCrop != false) return Presentation.None;", "if (w.InCrop == true) return Presentation.None;"),
    ("Secure Input ignored", WS, "        if (m.SecureInput is { } secure) SecureInput = secure;\n", ""),
    ("a stopped window's entry forgotten at once (before the contract said otherwise)", WS,
     "        stopped.Add(id);\n        Restate(u, shownBefore);", "        RemoveWithChildren(new HashSet<uint> { id }, u);\n        Restate(u, shownBefore);"),
    ("a stopped window's children still shown", WS, "if (cur.Owner is not { } o || !seen.Add(cur.Id)) return false;", "if (cur.Owner is not { } o || seen.Add(cur.Id)) return false;"),
    ("a window shown again stays out of sight", WS, "if (stopped.Remove(id)) Restate(u, shownBefore);", "if (stopped.Contains(id)) Restate(u, shownBefore);"),
]
CTRL = "WindowsClient/Sill.Windows/Coherence/CoherenceController.cs"
# The client's rules around a stop that kind 45s alone do not show (the walks' Mac reads each kind 29 at once):
# judged by Sill.Checks' unit groups and the kind 29 walks' checks against their Mac, whose view lags by the
# coalescer (not by the kind 29 file, which any change to the client's requests changes).
CONTROLLER = [
    ("a kind 45 that still brings a stopped window shows it again", WS, "            listed.Add(e.Id);\n", "            listed.Add(e.Id);\n            stopped.Remove(e.Id);\n"),
    ("a removal that crossed the kind 29 asking again ends the wish", CTRL, "        foreach (var id in again) if (!Set.Holds(id)) Plan.Ask(id, now);\n", ""),
    ("no crossing noted as a kind 29 leaves the stopped windows out", CTRL, "        crossing.UnionWith(Set.Stopped);\n", ""),
    ("a crossing forgotten while its window is out of sight", CTRL,
     "crossing.RemoveWhere(id => !Set.Holds(id) && !Plan.Asked.Contains(id) && !Set.Stopped.Contains(id));", "crossing.RemoveWhere(id => !Set.Contains(id));"),
    ("a stopped window whose entry went not asked for again", CTRL,
     "        var u = Set.Show(id);\n        if (!Set.Contains(id)) Plan.Ask(id, clock());",
     "        if (!Set.Contains(id) && !Set.Stopped.Contains(id)) Plan.Ask(id, clock());\n        var u = Set.Show(id);"),
]
PLANS = [
    # Not here: a child left out of the hidden list while its owner is in it, since the host hides a child with
    # its owner too (CoherenceGates.make), so the two sides still agree on what streams.
    ("the hidden list left out", PLAN, "Hidden = hidden.Count > 0 ? hidden : null,", "Hidden = null,"),
    ("the focus left out", PLAN, "uint? focus = Focus is { } f && windows.Contains(f) ? f : null;", "uint? focus = Focus is { } f && !windows.Contains(f) ? f : null;"),
    ("windows asked for left out", PLAN, "        foreach (var id in asked.OrderBy(a => a.Value).Select(a => a.Key)) if (seen.Add(id)) ids.Add(id);\n", ""),
    ("the scale in whole pixels", PLAN, "Scale = Math.Round(Math.Clamp(Scale, 0.5, 4), 3),", "Scale = Math.Round(Math.Clamp(Scale, 0.5, 4)),"),
    ("fps from the focus rate", PLAN, "Fps = Math.Clamp(Fps, 1, SideStreamRequest.MaxFPS),", "Fps = Math.Clamp(FocusFps, 1, SideStreamRequest.MaxFPS),"),
]


def mutated(root, rel, old, new):
    src = open(os.path.join(root, rel)).read()
    if src.count(old) != 1:
        return None
    return src.replace(old, new)


def swift_run(exe, env):
    r = subprocess.run([exe], capture_output=True, text=True, timeout=300, env={**os.environ, **env})
    return r.returncode, r.stdout + r.stderr


def tree(root, t, rel=None, text=None):
    """The Windows client and the kind 29 walks copied to `t` (one file replaced), for dotnet to build."""
    shutil.copytree(os.path.join(root, "WindowsClient"), os.path.join(t, "WindowsClient"), ignore=shutil.ignore_patterns("bin", "obj"))
    os.makedirs(os.path.join(t, "Tests/checks/coherence-walks"))
    shutil.copy(os.path.join(root, "Tests/checks/coherence-walks/kind29-walks.jsonl"), os.path.join(t, "Tests/checks/coherence-walks"))
    if rel:
        open(os.path.join(t, rel), "w").write(text)


def dotnet_build(t):
    r = subprocess.run(["dotnet", "build", os.path.join(t, "WindowsClient/Sill.Checks/Sill.Checks.csproj"), "-c", "Release",
                        "-o", os.path.join(t, "out"), "-nologo", "-v", "q"], capture_output=True, text=True, timeout=600)
    return r.returncode == 0, r.stdout + r.stderr


def dotnet_run(t, args, env=None):
    r = subprocess.run(["dotnet", os.path.join(t, "out", "SillChecks.dll"), *args], capture_output=True, text=True, timeout=600,
                       env={**os.environ, **(env or {})})
    return r.returncode, r.stdout + r.stderr


def first_fail(out):
    return next((l for l in out.splitlines() if l.startswith("FAIL")), "")[:120]


def main(root):
    dotnet = shutil.which("dotnet") is not None
    if not dotnet:
        print(f"dotnet is not on PATH: the {len(CROSS) + len(REDUCER) + len(CONTROLLER) + len(PLANS)} mutants that need the Windows client's "
              "checks are skipped (export DOTNET_ROOT=… PATH=… to run them)", flush=True)
    caught = total = 0

    def judge(name, hit, why):
        nonlocal caught, total
        total += 1
        caught += hit
        print(f"{'caught' if hit else 'MISSED'} {name}{': ' + why if why else ''}", flush=True)

    with tempfile.TemporaryDirectory() as t:
        base_swift = os.path.join(t, "base")
        ok, log = module.build(CHECK, root, base_swift)
        if not ok:
            raise SystemExit("the check does not build:\n" + log[:2000])
        base_cs = os.path.join(t, "cs-base")
        if dotnet:
            tree(root, base_cs)
            ok, log = dotnet_build(base_cs)
            if not ok:
                raise SystemExit("Sill.Checks does not build:\n" + log[:2000])
        k29 = os.path.join(root, "Tests/checks/coherence-walks/kind29-walks.jsonl")
        walks = os.path.join(t, "walks.jsonl")

        for name, rel, old, new in SWIFT:
            text = mutated(root, rel, old, new)
            if text is None:
                print(f"NOT APPLIED {name}", flush=True); total += 1; continue
            exe = os.path.join(t, "m")
            ok, log = module.build(CHECK, root, exe, {rel: text})
            if not ok:
                print(f"DOES NOT COMPILE {name}: {log[:300]}", flush=True); total += 1; continue
            code, out = swift_run(exe, {"WALKS_OUT": walks, "WALKS_KIND29": k29})
            judge(name, code != 0, first_fail(out))

        for name, rel, old, new in ([] if not dotnet else CROSS):
            text = mutated(root, rel, old, new)
            if text is None:
                print(f"NOT APPLIED {name} (the client)", flush=True); total += 1; continue
            exe = os.path.join(t, "m")
            ok, log = module.build(CHECK, root, exe, {rel: text})
            if not ok:
                print(f"DOES NOT COMPILE {name}: {log[:300]}", flush=True); total += 1; continue
            swift_run(exe, {"WALKS_OUT": walks})
            code, out = dotnet_run(base_cs, ["swift-walks"], {"SILL_SWIFT_WALKS": walks})
            judge(f"{name}, seen by the client's WindowSet", code != 0, first_fail(out))

        for name, rel, old, new in ([] if not dotnet else REDUCER):
            text = mutated(root, rel, old, new)
            if text is None:
                print(f"NOT APPLIED {name}", flush=True); total += 1; continue
            m = os.path.join(t, "cs-m")
            shutil.rmtree(m, ignore_errors=True)
            tree(root, m, rel, text)
            ok, log = dotnet_build(m)
            if not ok:
                print(f"DOES NOT COMPILE {name}: {log[-400:]}", flush=True); total += 1; continue
            code, out = dotnet_run(m, ["swift-walks"])
            judge(f"the client: {name}", code != 0, first_fail(out))

        for name, rel, old, new in ([] if not dotnet else CONTROLLER):
            text = mutated(root, rel, old, new)
            if text is None:
                print(f"NOT APPLIED {name}", flush=True); total += 1; continue
            m = os.path.join(t, "cs-m")
            shutil.rmtree(m, ignore_errors=True)
            tree(root, m, rel, text)
            ok, log = dotnet_build(m)
            if not ok:
                print(f"DOES NOT COMPILE {name}: {log[-400:]}", flush=True); total += 1; continue
            code, out = dotnet_run(m, ["window-state", "coherence-controller", "kind29-walks"])
            fails = [l for l in out.splitlines() if l.startswith("FAIL") and "differ from Tests/checks" not in l]
            judge(f"the client: {name}", bool(fails), fails[0][:120] if fails else "")

        for name, rel, old, new in ([] if not dotnet else PLANS):
            text = mutated(root, rel, old, new)
            if text is None:
                print(f"NOT APPLIED {name}", flush=True); total += 1; continue
            m = os.path.join(t, "cs-m")
            shutil.rmtree(m, ignore_errors=True)
            tree(root, m, rel, text)
            ok, log = dotnet_build(m)
            if not ok:
                print(f"DOES NOT COMPILE {name}: {log[-400:]}", flush=True); total += 1; continue
            mine = os.path.join(t, "k29.jsonl")
            dotnet_run(m, ["--write-kind29", mine])
            code, out = swift_run(base_swift, {"WALKS_OUT": walks, "WALKS_KIND29": mine})
            judge(f"the client's kind 29: {name}, read by the host", code != 0, first_fail(out))

    print(f"{caught} of {total} mutants caught")
    return caught == total


if __name__ == "__main__":
    sys.exit(0 if main(sys.argv[1]) else 1)
