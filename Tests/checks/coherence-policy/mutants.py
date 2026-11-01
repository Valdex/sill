"""Mutants of Coherence's rules on the host (Sources/SillHost/CoherencePolicy.swift): each must compile
with the check and make it fail. usage: mutants.py ROOT"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import module

P = "Sources/SillHost/CoherencePolicy.swift"
MUTANTS = [
    ("a kind 29 without coherence read as one", P, "guard r.coherence == true else { return nil }", "guard r.coherence != false else { return nil }"),
    ("the focus rate unheld", P, "min(max(asked ?? SideStreamRequest.defaultFocusFPS, 1), SideStreamRequest.maxFocusFPS)", "max(asked ?? SideStreamRequest.defaultFocusFPS, 1)"),
    ("a focus outside the windows kept", P, "focus = r.focus.flatMap { listed.contains($0) ? $0 : nil }", "focus = r.focus"),
    ("the focus may be hidden", P, "hidden = Set((r.hidden ?? []).filter { $0 != front })", "hidden = Set(r.hidden ?? [])"),
    ("captures at the others' rate", P, "var captureFPS: Int { max(fps, focusFPS) }", "var captureFPS: Int { fps }"),
    ("a child never at its owner's rate", P, "let top = s.owners[id] ?? id", "let top = id"),
    ("hidden where any device hides it", P, "gates[id] = CoherenceGate(cap: max(g.cap, cap), hidden: g.hidden && hidden)", "gates[id] = CoherenceGate(cap: max(g.cap, cap), hidden: g.hidden || hidden)"),
    ("the last device's cap, not the most", P, "gates[id] = CoherenceGate(cap: max(g.cap, cap), hidden: g.hidden && hidden)", "gates[id] = CoherenceGate(cap: cap, hidden: g.hidden && hidden)"),
    ("no floor under the bitrate", P, "max(2_000_000, per60 * max(cap, 1) / 60)", "per60 * max(cap, 1) / 60"),
    ("the device's scale unheld below", P, ".map { min(max($0, least), most) }", ".map { min($0, most) }"),
    ("more than the knob", P, "return min(knob, CGFloat(largest))", "return CGFloat(largest)"),
    ("no lead before an app's ask", P, "if let since = appSince[c.bundle], c.firstSeen >= since - Self.lead { join(c.id); continue }", "if let since = appSince[c.bundle], c.firstSeen >= since { join(c.id); continue }"),
    ("a window joins again", P, "guard !listed.contains(id), !ever.contains(id), !joined.contains(id) else { return }", "guard !listed.contains(id), !joined.contains(id) else { return }"),
    ("pending forgotten at once", P, "for (id, at) in pending where ask.windows.contains(id) || now - at >= Self.grace { pending[id] = nil }", "for (id, _) in pending { pending[id] = nil }"),
    ("a launch's front window at once", P, "guard now - at >= Self.launchWait else { continue }", ""),
    ("a dialog is a window", P, 'case "AXDialog", "AXSystemDialog": return WindowStateEntry.dialog', 'case "AXSystemDialog": return WindowStateEntry.dialog'),
    ("the menu bar a child", P, "default: return nil\n        }\n    }\n\n    /// The children", "default: return WindowStateEntry.panel\n        }\n    }\n\n    /// The children"),
    ("no submenu by its menu", P, "owner = found.first { $0.pid == w.pid && $0.frame.insetBy(dx: -reach, dy: -reach).intersects(w.frame) }?.owner", "owner = nil"),
    ("an invisible child", P, "pids.contains(w.pid), w.onScreen, w.alpha > 0,", "pids.contains(w.pid), w.onScreen,"),
    ("any app's lone window owns a status window", P, "if owner == nil, mine.count == 1, role == WindowStateEntry.menu || role == WindowStateEntry.dialog { owner = mine[0].id }", "if owner == nil, mine.count == 1 { owner = mine[0].id }"),
    ("no sheet by Accessibility", P, "if ax.sheets.contains(where: { same($0, w.frame) }) { matched = WindowStateEntry.sheet }", "if false { matched = WindowStateEntry.sheet }"),
    ("a title bar with a toolbar", P, "guard !toolbar, !fullScreen, let close else { return 0 }", "guard !fullScreen, let close else { return 0 }"),
    ("the title bar is the button's bottom", P, "let h = (2 * (close.midY - frame.minY)).rounded()", "let h = (close.maxY - frame.minY).rounded()"),
    ("in the crop without slack", P, "case .withApp(let rect): return rect.insetBy(dx: -1, dy: -1).contains(frame)", "case .withApp(let rect): return rect.contains(frame)"),
    ("a picture of the window alone shows its children", P, "case .alone: return false", "case .alone: return true"),
    ("the key window shown or not", P, "r.focus = read.keyWindow.flatMap { shownTops.contains($0) ? $0 : nil }", "r.focus = read.keyWindow"),
    ("a small window joins", P, "w.layer == 0 && w.frame.width > 100 && w.frame.height > 100", "w.layer == 0 && w.frame.width > 10 && w.frame.height > 10"),
    ("no removed", P, "if !removed.isEmpty { m.removed = removed }", ""),
    ("only what changed of an entry", P, "let changed = now.entries.filter { sent[$0.id] != $0 || $0.id == answering?.window }", "let changed = now.entries.filter { sent[$0.id] != $0 }"),
    ("an answer only when something changed", P, "if nothing && answering == nil && !owesFull { return nil }", "if nothing && !owesFull { return nil }"),
    ("never full after a silence", P, "if owesFull || lastAt.map({ time - $0 >= Self.gapAfter }) ?? true {", "if owesFull || lastAt == nil {"),
    ("gen not rising", P, "        gen += 1\n        sent =", "        sent ="),
    ("40 a second", P, "static let perSecond = 20", "static let perSecond = 40"),
    ("a least kept below a most", P, "if let h = hi, h < t { hi = nil }", ""),
    ("a stale least kept", P, "if let l = lo, a < l - tolerance { lo = nil }", ""),
    ("the unknown least is 0", P, "[minW ?? 1, minH ?? 1]", "[minW ?? 0, minH ?? 0]"),
    ("no room", P, "return CGSize(width: min(max(CGFloat(size[0]).rounded(), 1), min(w, most)),", "return CGSize(width: min(max(CGFloat(size[0]).rounded(), 1), most),"),
    ("the oldest held, not the newest", P, "            held[p.id] = p\n            return .held", "            if held[p.id] == nil { held[p.id] = p }\n            return .held"),
    ("a move needs its window key", P, "case .pointer(let action, _, _): return action.isButtonDown", "case .pointer: return true"),
    ("activation throttled globally", P, "if let last, last.window == window, now - last.at < Self.retry, now >= last.at { return false }", "if let last, now - last.at < Self.retry, now >= last.at { return false }"),
    ("icons unheld", P, "return min(max(asked, least), largest)", "return max(asked, least)"),
]
module.mutate("coherence-policy", MUTANTS, sys.argv[1])
