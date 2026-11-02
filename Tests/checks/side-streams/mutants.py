"""Mutants of the side streams' and the desk's rules (Sources/SillHost/SideStreamPolicy.swift): each must
compile with the check and make it fail. usage: mutants.py ROOT"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import module

P = "Sources/SillHost/SideStreamPolicy.swift"
MUTANTS = [
    ("the default rate 30", P, "min(max(asked ?? SideStreamRequest.defaultFPS, 1), SideStreamRequest.maxFPS)", "min(max(asked ?? SideStreamRequest.maxFPS, 1), SideStreamRequest.maxFPS)"),
    ("no rate cap", P, "min(max(asked ?? SideStreamRequest.defaultFPS, 1), SideStreamRequest.maxFPS)", "max(asked ?? SideStreamRequest.defaultFPS, 1)"),
    ("the main window streams beside itself", P, "for id in unique(ask.windows) where id != main {", "for id in unique(ask.windows) {"),
    ("the last asker's rate, not the most", P, "fps[id] = max(fps[id] ?? 0, ask.fps)", "fps[id] = ask.fps"),
    ("a failed window tried again at once", P, "if let failed = failedAt[id], now - failed < coolDown { continue }", ""),
    ("the limit counts windows that do not stream", P, "for id in queue where plan.order.count < limit {", "for id in queue.prefix(limit) {"),
    ("beyond the limit, the first asked whatever their place", P, "let queue = asked.enumerated().sorted { (pick[$0.element]!, $0.offset) < (pick[$1.element]!, $1.offset) }.map(\\.element)", "let queue = asked"),
    ("each place last asked first", P, "(pick[$0.element]!, $0.offset) < (pick[$1.element]!, $1.offset)", "(pick[$0.element]!, -$0.offset) < (pick[$1.element]!, -$1.offset)"),
    ("the worst place any device gives", P, "pick[id] = min(pick[id] ?? .hidden, ask.pick(id))", "pick[id] = max(pick[id] ?? .front, ask.pick(id))"),
    ("the last device's place", P, "pick[id] = min(pick[id] ?? .hidden, ask.pick(id))", "pick[id] = ask.pick(id)"),
    ("the focus not first", P, "front.contains(id) ? .front : hidden", "front.contains(id) ? .shown : hidden"),
    ("hidden windows among the shown", P, "hidden.contains(id) ? .hidden : .shown }", "hidden.contains(id) ? .shown : .shown }"),
    ("hidden before in front", P, "front.contains(id) ? .front : hidden.contains(id) ? .hidden : .shown", "hidden.contains(id) ? .hidden : front.contains(id) ? .front : .shown"),
    ("an ineligible window streams", P, "            guard eligible(id) else { continue }\n", ""),
    ("a cool-down of 1 s", P, "static let coolDown: Double = 10", "static let coolDown: Double = 1"),
    ("no early frames", P, "guard t >= due - every / 4 else { return false }", "guard t >= due else { return false }"),
    ("early frames add up", P, "next = t - due > every ? t + every : due + every", "next = t + every - every / 4"),
    ("no restart after a pause", P, "next = t - due > every ? t + every : due + every", "next = due + every"),
    ("a hidden window every second", P, "static let hiddenInterval: Double = 3", "static let hiddenInterval: Double = 1"),
    ("hidden ignored", P, "var interval: Double { hidden ? Self.hiddenInterval : 1 / Double(max(cap, 1)) }", "var interval: Double { 1 / Double(max(cap, 1)) }"),
    ("a waiting client sent a delta", P, "guard isKeyframe, pending <= budget, haveSets else { return (.skip, true) }", "guard pending <= budget, haveSets else { return (.skip, true) }"),
    ("a waiting client's keyframe without its sets", P, "return (.send(parameterSets: true), false)\n        }", "return (.send(parameterSets: newSets), false)\n        }"),
    ("a delta over the budget sent", P, "if !isKeyframe, pending > budget { return (.skip, true) }", ""),
    ("a client that skipped stops waiting", P, "if !isKeyframe, pending > budget { return (.skip, true) }", "if !isKeyframe, pending > budget { return (.skip, false) }"),
    ("keyframes asked at every loss", P, "lastAsked.map { now - $0 >= keyframeEvery } ?? true", "true"),
    ("no menu bar room", P, "let want = CGSize(width: up(size.width + 16), height: up(size.height + 48))", "let want = CGSize(width: up(size.width + 16), height: up(size.height + 16))"),
    ("no flap guard", P, "return current == 1 && !fits2x ? 1 : 2", "return 2"),
    ("the memory's half", P, "static let memoryShare = 0.25", "static let memoryShare = 0.5"),
    ("another display as small as the smallest", P, "let another = max(framebuffers.max() ?? 0,", "let another = max(framebuffers.min() ?? 0,"),
    ("slots from 1", P, "        var s = 0\n        while used.contains(s)", "        var s = 1\n        while used.contains(s)"),
    ("a path kept", P, '.replacingOccurrences(of: "/", with: "-")', ""),
    ("a hidden name kept", P, 'if cleaned.isEmpty || cleaned.hasPrefix(".") { cleaned = "file" + cleaned }', 'if cleaned.isEmpty { cleaned = "file" }'),
    ("control characters kept", P, "name.unicodeScalars.filter { $0.value >= 0x20 && $0.value != 0x7F }", "name.unicodeScalars.filter { $0.value != 0x7F }"),
    ("the length in characters", P, "guard out.utf8.count + String(c).utf8.count <= maxBytes else { break }", "guard out.count < maxBytes else { break }"),
]
module.mutate("side-streams", MUTANTS, sys.argv[1])
