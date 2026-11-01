import Foundation

// Coherence's rules on the host (Sources/SillHost/CoherencePolicy.swift; M1b, docs/coherence-protocol.md):
// a kind 29 held to the contract, each window's gate (focus rate, the others' rate, hidden), the capture's
// scale, the windows that join by themselves, kind 45's entries and children, what each kind 45 carries
// (checked against a reducer written as the Windows client's WindowSet is: full replaces, partial merges
// whole entries, removed takes children), the pace of kinds 45 and 46, the sizes a window refused, which
// input needs its window key first, and kind 4's icon size. Then random runs against each rule's properties.
var failures = 0
var checks = 0
func check(_ ok: Bool, _ what: @autoclosure () -> String, line: Int = #line) {
    checks += 1
    if !ok { failures += 1; print("FAIL (line \(line)): \(what())") }
}
var rng = SystemRandomNumberGenerator()

func ask(_ windows: [UInt32], fps: Int? = nil, focus: UInt32? = nil, focusFPS: Int? = nil, hidden: [UInt32]? = nil,
         apps: [String]? = nil, all: Bool? = nil, scale: Double? = nil) -> CoherenceAsk {
    CoherenceAsk(SideStreamRequest(windows: windows, fps: fps, wholeApp: true, coherence: true, focus: focus, focusFPS: focusFPS,
                                   apps: apps, all: all, hidden: hidden, scale: scale))!
}

// MARK: - CoherenceAsk

check(CoherenceAsk(SideStreamRequest(windows: [1, 2], fps: 30)) == nil && CoherenceAsk(SideStreamRequest(windows: [])) == nil
      && CoherenceAsk(SideStreamRequest(windows: [1], coherence: false)) == nil, "no coherence, no ask (#66's kind 29, the one that ends it)")
do {
    let a = ask([3, 1, 3, 2])
    check(a.windows == [3, 1, 2] && a.fps == 15 && a.focusFPS == 60 && a.focus == nil && a.hidden.isEmpty && a.apps.isEmpty
          && !a.all && a.scale == nil, "defaults: each window once, 15 and 60 fps, nothing else: \(a)")
    check(a.captureFPS == 60, "captures at the focus rate even with no focus, so a focus never restarts one")
    let b = ask([1, 2, 3], fps: 99, focus: 2, focusFPS: 500, hidden: [2, 3, 9], apps: [" com.apple.Safari ", "", String(repeating: "x", count: 300)],
                all: true, scale: 1.25)
    check(b.fps == 30 && b.focusFPS == 120 && b.focus == 2 && b.hidden == [3, 9] && b.apps == ["com.apple.Safari"] && b.all && b.scale == 1.25,
          "held: fps 30, focusFPS 120, the focus never hidden, app IDs trimmed: \(b)")
    check(ask([1], fps: 0, focusFPS: 0).fps == 1 && ask([1], fps: 0, focusFPS: 0).focusFPS == 1 && ask([1], fps: 0, focusFPS: 0).captureFPS == 1,
          "the least rate is 1")
    check(ask([1, 2], focus: 7).focus == nil, "a focus not among the windows is none")
    check(ask([1], scale: .nan).scale == nil && ask([1], scale: -1).scale == nil && ask([1], scale: 0).scale == nil && ask([1], scale: .infinity).scale == nil,
          "a scale that is no positive number is none")
    check(ask([1], fps: 30, focusFPS: 10).captureFPS == 30, "the capture rate is the higher of the two")
}

// MARK: - CoherenceGates

typealias Shown = CoherenceGates.Shown
do {
    let a = ask([1, 2, 3], fps: 15, focus: 1, focusFPS: 60, hidden: [3])
    let g = CoherenceGates.make([Shown(ask: a, windows: [1, 2, 3])])
    check(g == [1: CoherenceGate(cap: 60, hidden: false), 2: CoherenceGate(cap: 15, hidden: false), 3: CoherenceGate(cap: 15, hidden: true)],
          "one device: its focus at 60, the others at 15, its hidden one hidden: \(g)")
    let c = CoherenceGates.make([Shown(ask: a, windows: [1, 2, 3, 10, 11], owners: [10: 1, 11: 3])])
    check(c[10] == CoherenceGate(cap: 60, hidden: false) && c[11] == CoherenceGate(cap: 15, hidden: true),
          "a child streams at its owner's rate and is hidden with it: \(c)")
    let joined = CoherenceGates.make([Shown(ask: a, windows: [1, 2, 3, 4])])
    check(joined[4] == CoherenceGate(cap: 15, hidden: false), "a joined window the device has not listed yet: the others' rate")
    let b = ask([2, 3], fps: 20, focus: 3, focusFPS: 90)
    let two = CoherenceGates.make([Shown(ask: a, windows: [1, 2, 3]), Shown(ask: b, windows: [2, 3])])
    check(two[1] == CoherenceGate(cap: 60, hidden: false) && two[2] == CoherenceGate(cap: 20, hidden: false)
          && two[3] == CoherenceGate(cap: 90, hidden: false), "two devices: the most of either; hidden only where both hide: \(two)")
    let both = CoherenceGates.make([Shown(ask: a, windows: [3]), Shown(ask: ask([3], hidden: [3]), windows: [3])])
    check(both[3]?.hidden == true, "hidden where every device that shows it hides it")
    check(CoherenceGates.make([]).isEmpty, "nobody: no gates")
    check(CoherenceGates.bitrate(per60: 15_000_000, cap: 60) == 15_000_000 && CoherenceGates.bitrate(per60: 15_000_000, cap: 15) == 3_750_000
          && CoherenceGates.bitrate(per60: 15_000_000, cap: 2) == 2_000_000 && CoherenceGates.bitrate(per60: 15_000_000, cap: 120) == 30_000_000,
          "the bitrate follows the cap, 2 Mbit/s at least")
}
for _ in 0..<1_000 {
    let devices = (0..<Int.random(in: 1...3, using: &rng)).map { _ -> Shown in
        let ws = (0..<Int.random(in: 0...5, using: &rng)).map { _ in UInt32.random(in: 1...8, using: &rng) }
        let a = ask(ws, fps: Int.random(in: 1...30, using: &rng), focus: ws.randomElement(using: &rng), focusFPS: Int.random(in: 1...120, using: &rng),
                    hidden: ws.filter { _ in Bool.random(using: &rng) })
        return Shown(ask: a, windows: ws + (Bool.random(using: &rng) ? [9] : []))
    }
    let g = CoherenceGates.make(devices)
    let all = Set(devices.flatMap(\.windows))
    var ok = Set(g.keys) == all
    for id in all {
        let caps = devices.filter { $0.windows.contains(id) }.map { $0.ask.focus == id ? $0.ask.focusFPS : $0.ask.fps }
        let hid = devices.filter { $0.windows.contains(id) }.allSatisfy { $0.ask.focus != id && $0.ask.hidden.contains(id) }
        if g[id] != CoherenceGate(cap: caps.max()!, hidden: hid) { ok = false }
        if devices.contains(where: { $0.ask.focus == id }), g[id]?.hidden != false { ok = false }
    }
    if !ok { check(false, "random gates: \(devices) → \(g)"); break }
}

// MARK: - CoherenceScale

check(CoherenceScale.knob(2, deviceScales: []) == 2 && CoherenceScale.knob(2, deviceScales: [.nan, -1]) == 2, "no device's scale: the knob")
check(CoherenceScale.knob(2, deviceScales: [1.25]) == 1.25 && CoherenceScale.knob(2, deviceScales: [1.5, 1.25]) == 1.5,
      "a 125 % device's windows at 1.25 pixels a point; two devices, the larger")
check(CoherenceScale.knob(1, deviceScales: [1.5]) == 1, "never more than the knob (Standard, the software encoder)")
check(CoherenceScale.knob(2, deviceScales: [0.5]) == 1 && CoherenceScale.knob(8, deviceScales: [9]) == 4, "a device's scale held to 1…4")

// MARK: - JoinRules

do {
    var j = JoinRules()
    let a = ask([1], apps: ["S"])
    j.asked(a, now: 100)
    let joined = j.consider([JoinCandidate(id: 5, bundle: "S", firstSeen: -.infinity), JoinCandidate(id: 6, bundle: "S", firstSeen: 101),
                             JoinCandidate(id: 7, bundle: "T", firstSeen: 101), JoinCandidate(id: 8, bundle: "S", firstSeen: 98.5),
                             JoinCandidate(id: 9, bundle: "S", firstSeen: 97.5)], fronts: [:], listed: [1], now: 101)
    check(joined == [6, 8], "a new window of an app asked for joins, one opened up to 2 s before the ask too; an old one or another app's never: \(joined)")
    check(j.effective(a) == [1, 6, 8] && j.ever == [6, 8], "the set: the device's own, then the joined in order: \(j.effective(a))")
    check(j.consider([JoinCandidate(id: 6, bundle: "S", firstSeen: 101)], fronts: [:], listed: [1], now: 102).isEmpty, "a window joins once")
    j.asked(ask([1, 6], apps: ["S"]), now: 101.5)
    check(j.pending.keys.sorted() == [8], "a joined window the device lists is no longer pending")
    j.asked(ask([1, 6], apps: ["S"]), now: 102.5)
    check(j.pending.keys.sorted() == [8], "left out within its grace: kept (the device may not have read it)")
    j.asked(ask([1, 6], apps: ["S"]), now: 103.2)
    check(j.pending.isEmpty && j.effective(ask([1, 6])) == [1, 6], "left out after its grace: gone")
    check(j.consider([JoinCandidate(id: 8, bundle: "S", firstSeen: 98.5)], fronts: [:], listed: [1, 6], now: 104).isEmpty, "and never joins again")
    j.asked(ask([1, 6]), now: 105)
    check(j.consider([JoinCandidate(id: 10, bundle: "S", firstSeen: 106)], fronts: [:], listed: [1, 6], now: 106).isEmpty, "an app no longer asked for: nothing joins")
    j.asked(ask([1, 6], apps: ["S"]), now: 200)
    check(j.consider([JoinCandidate(id: 11, bundle: "S", firstSeen: 150)], fronts: [:], listed: [], now: 200).isEmpty
          && j.consider([JoinCandidate(id: 12, bundle: "S", firstSeen: 199)], fronts: [:], listed: [], now: 200) == [12],
          "asked for again: timed from that ask")
}
do {
    var j = JoinRules()
    j.asked(ask([1], all: true), now: 10)
    check(j.consider([JoinCandidate(id: 2, bundle: "A", firstSeen: 11), JoinCandidate(id: 3, bundle: "B", firstSeen: 7)], fronts: [:], listed: [], now: 11) == [2],
          "all: any app's new window")
    j.asked(ask([1]), now: 12)
    check(j.consider([JoinCandidate(id: 4, bundle: "A", firstSeen: 13)], fronts: [:], listed: [], now: 13).isEmpty, "all turned off: nothing")
}
do {
    var j = JoinRules()
    j.asked(ask([1]), now: 0)
    j.launched("M", now: 10)
    j.launched("M", now: 10.9)
    check(j.consider([], fronts: ["M": 40], listed: [], now: 11).isEmpty, "a launch waits for a new window")
    check(j.consider([JoinCandidate(id: 41, bundle: "M", firstSeen: 11.2)], fronts: ["M": 40], listed: [], now: 11.2) == [41],
          "a launched app's new window joins, not its front one")
    check(j.consider([JoinCandidate(id: 42, bundle: "M", firstSeen: 11.5)], fronts: ["M": 40], listed: [], now: 12).isEmpty, "only its first")
    j.launched("N", now: 20)
    j.launched("N", now: 20.5)
    check(j.consider([], fronts: ["N": 50], listed: [], now: 21).isEmpty && j.consider([], fronts: ["N": 50], listed: [], now: 21.6) == [50],
          "a launched app with no new window: its front window 1.5 s after the first launch (a second one meanwhile counts not)")
    j.launched("O", now: 30)
    check(j.consider([], fronts: [:], listed: [], now: 41).isEmpty && j.consider([], fronts: ["O": 60], listed: [], now: 42).isEmpty,
          "a launch is forgotten after 10 s, or once looked at with no window")
    j.launched("P", now: 50)
    check(j.consider([], fronts: ["P": 70], listed: [70], now: 52).isEmpty, "a front window already shown does not join")
    j.forget([41])
    check(!j.pending.keys.contains(41) && j.ever.contains(41), "taken back: no longer pending, never again")
}

// MARK: - CoherenceEntries' small rules

check(CoherenceEntries.role(subrole: "AXStandardWindow") == "window" && CoherenceEntries.role(subrole: "AXDialog") == "dialog"
      && CoherenceEntries.role(subrole: "AXSystemDialog") == "dialog" && CoherenceEntries.role(subrole: "AXFloatingWindow") == "panel"
      && CoherenceEntries.role(subrole: nil) == "window" && CoherenceEntries.role(subrole: "AXUnknown") == "window", "a window's role by its subrole")
check(CoherenceEntries.childRole(layer: 101, matched: nil) == "menu" && CoherenceEntries.childRole(layer: 200, matched: nil) == "tooltip"
      && CoherenceEntries.childRole(layer: 25, matched: nil) == "tooltip" && CoherenceEntries.childRole(layer: 8, matched: nil) == "dialog"
      && CoherenceEntries.childRole(layer: 3, matched: nil) == "panel" && CoherenceEntries.childRole(layer: 19, matched: nil) == "panel"
      && CoherenceEntries.childRole(layer: 0, matched: nil) == nil && CoherenceEntries.childRole(layer: 24, matched: nil) == nil
      && CoherenceEntries.childRole(layer: 20, matched: nil) == nil && CoherenceEntries.childRole(layer: 500, matched: nil) == nil
      && CoherenceEntries.childRole(layer: 0, matched: "sheet") == "sheet", "a child's role: Accessibility's match, else its level; the menu bar, Dock and drags none")
let win = CGRect(x: 100, y: 50, width: 800, height: 600)
check(CoherenceEntries.titleBar(close: CGRect(x: 107, y: 57, width: 14, height: 14), frame: win, toolbar: false, fullScreen: false) == 28,
      "a plain title bar: twice the close button's middle (28 pt)")
check(CoherenceEntries.titleBar(close: CGRect(x: 107, y: 57, width: 14, height: 14), frame: win, toolbar: true, fullScreen: false) == 0
      && CoherenceEntries.titleBar(close: CGRect(x: 107, y: 57, width: 14, height: 14), frame: win, toolbar: false, fullScreen: true) == 0
      && CoherenceEntries.titleBar(close: nil, frame: win, toolbar: false, fullScreen: false) == 0, "none with a toolbar, in full screen, without a close button")
check(CoherenceEntries.titleBar(close: CGRect(x: 107, y: 51, width: 14, height: 4), frame: win, toolbar: false, fullScreen: false) == 0
      && CoherenceEntries.titleBar(close: CGRect(x: 107, y: 200, width: 14, height: 14), frame: win, toolbar: false, fullScreen: false) == 0,
      "a bar under 16 or over 60 points is none")
check(CoherenceEntries.relative(CGRect(x: 120.5, y: 70, width: 40, height: 20.333), to: win.origin) == [20.5, 20, 40, 20.33], "a rect from the window's top-left, to a hundredth")
check(CoherenceEntries.inCrop(CGRect(x: 110, y: 60, width: 50, height: 50), .withApp(win)) == true
      && CoherenceEntries.inCrop(CGRect(x: 99.5, y: 60, width: 50, height: 50), .withApp(win)) == true
      && CoherenceEntries.inCrop(CGRect(x: 880, y: 60, width: 50, height: 50), .withApp(win)) == false
      && CoherenceEntries.inCrop(CGRect(x: 110, y: 60, width: 50, height: 50), .alone) == false
      && CoherenceEntries.inCrop(CGRect(x: 110, y: 60, width: 50, height: 50), .unknown) == nil,
      "in the crop (a point of slack), never in a picture of the window alone, not known before it streams")
func cg(_ id: UInt32, pid: Int32 = 10, layer: Int = 0, _ r: CGRect, on: Bool = true, alpha: Double = 1, name: String? = nil) -> CGFacts {
    CGFacts(id: id, pid: pid, layer: layer, frame: r, onScreen: on, alpha: alpha, name: name)
}
check(CoherenceEntries.joinable(cg(1, win), bundle: "a", ownPID: 99) && !CoherenceEntries.joinable(cg(1, layer: 3, win), bundle: "a", ownPID: 99)
      && !CoherenceEntries.joinable(cg(1, CGRect(x: 0, y: 0, width: 100, height: 400)), bundle: "a", ownPID: 99)
      && !CoherenceEntries.joinable(cg(1, pid: 99, win), bundle: "a", ownPID: 99)
      && !CoherenceEntries.joinable(cg(1, win, alpha: 0), bundle: "a", ownPID: 99)
      && !CoherenceEntries.joinable(cg(1, win, name: "LayerProbeParent"), bundle: "a", ownPID: 99)
      && !CoherenceEntries.joinable(cg(1, win), bundle: "me.saffer.sill.client", ownPID: 99), "a regular window joins: not a panel, a small one, Sill's, an invisible or helper one")

// MARK: - CoherenceEntries.make

do {
    var r = RegistryRead()
    let a = CGRect(x: 0, y: 30, width: 1000, height: 700), b = CGRect(x: 2000, y: 30, width: 600, height: 400)
    r.windows = [1: cg(1, a, name: "Doc"), 2: cg(2, pid: 20, b),
                 // a context menu in window 1, its submenu out of it, a tooltip in it, the app's menu bar, a sheet
                 11: cg(11, layer: 101, CGRect(x: 900, y: 100, width: 200, height: 300)),
                 12: cg(12, layer: 101, CGRect(x: 1098, y: 150, width: 180, height: 200)),
                 13: cg(13, layer: 200, CGRect(x: 50, y: 60, width: 80, height: 20)),
                 14: cg(14, layer: 24, CGRect(x: 0, y: 0, width: 1500, height: 24)),
                 15: cg(15, layer: 0, CGRect(x: 200, y: 58, width: 600, height: 300)),
                 16: cg(16, layer: 0, CGRect(x: 3000, y: 30, width: 500, height: 500)),
                 17: cg(17, pid: 20, layer: 25, CGRect(x: 1400, y: 0, width: 30, height: 24)),
                 18: cg(18, pid: 30, layer: 101, CGRect(x: 100, y: 100, width: 100, height: 100)),
                 19: cg(19, layer: 101, CGRect(x: 950, y: 120, width: 100, height: 100), alpha: 0)]
    r.order = [12, 11, 13, 19, 15, 1, 14, 17, 18, 2, 16]
    var ax = AXFacts(title: "Document", subrole: "AXStandardWindow", resizable: true,
                     close: CGRect(x: 7, y: 37, width: 14, height: 14), minimize: CGRect(x: 27, y: 37, width: 14, height: 14),
                     zoom: CGRect(x: 47, y: 37, width: 14, height: 14))
    ax.sheets = [CGRect(x: 200, y: 58, width: 600, height: 300)]
    r.ax = [1: ax, 2: AXFacts(title: nil, subrole: "AXDialog", minimized: true, modal: true, toolbar: true)]
    r.apps = [10: AppFacts(bundleID: "com.apple.TextEdit", name: "TextEdit"), 20: AppFacts(bundleID: "com.apple.Notes", name: "Notes")]
    r.keyWindow = 1
    r.secureInput = true
    var limits = SizeLimits()
    limits.learn(asked: CGSize(width: 200, height: 200), took: CGSize(width: 400, height: 200))
    let res = CoherenceEntries.make(read: r, windows: [1, 2, 3, 16], joined: [2], limits: [1: limits], pictures: [1: .withApp(a)])
    let e1 = res.entries.first { $0.id == 1 }, e2 = res.entries.first { $0.id == 2 }
    check(e1 == WindowStateEntry(id: 1, app: "com.apple.TextEdit", appName: "TextEdit", title: "Document", role: "window", size: [1000, 700],
                                 min: [400, 1], max: nil, resizable: true, focused: true, minimized: false, fullScreen: false, modal: false,
                                 titleBar: 28, joined: nil, controls: WindowControls(close: [7, 7, 14, 14], minimize: [27, 7, 14, 14], zoom: [47, 7, 14, 14])),
          "a window's entry, every field: \(String(describing: e1))")
    check(e2?.title == nil && e2?.role == "dialog" && e2?.minimized == true && e2?.modal == true && e2?.titleBar == 0 && e2?.joined == true
          && e2?.focused == false && e2?.controls == nil && e2?.app == "com.apple.Notes", "a minimized modal dialog with a toolbar that joined: \(String(describing: e2))")
    check(!res.entries.contains { $0.id == 3 }, "a window neither list has is gone")
    let kids = res.entries.filter { $0.owner != nil }
    check(kids.map(\.id) == [12, 11, 13, 15], "the children, front to back: the submenu, the menu, the tooltip, the sheet: \(kids.map(\.id))")
    check(kids.allSatisfy { $0.owner == 1 }, "each owned by window 1 (the submenu through its menu, though the app shows two windows)")
    check(kids.first { $0.id == 11 }?.role == "menu" && kids.first { $0.id == 13 }?.role == "tooltip" && kids.first { $0.id == 15 }?.role == "sheet",
          "roles: menu, tooltip, sheet by Accessibility")
    check(kids.first { $0.id == 11 }?.rect == [900, 70, 200, 300] && kids.first { $0.id == 11 }?.inCrop == false
          && kids.first { $0.id == 12 }?.inCrop == false && kids.first { $0.id == 13 }?.inCrop == true,
          "the menu's rect from its owner; it and its submenu spill out of its picture, the tooltip is inside it")
    check(!res.entries.contains { [14, 17, 18, 19].contains($0.id) } && res.entries.first { $0.id == 16 }?.owner == nil,
          "never a child: the menu bar, another normal window (16, shown as its own), a status item over no window, another app's menu, an invisible one")
    check(res.order == [12, 11, 13, 15, 1, 2, 16] && res.focus == 1 && res.owners == [11: 1, 12: 1, 13: 1, 15: 1], "order of the listed windows; the focus: \(res.order)")
    var later = r
    later.ax[1]?.frame = CGRect(x: 0, y: 30, width: 1100, height: 760)
    later.ax[1]?.close = CGRect(x: 7, y: 37, width: 14, height: 14)
    let sized = CoherenceEntries.make(read: later, windows: [1], joined: [], limits: [:], pictures: [:]).entries.first
    check(sized?.size == [1100, 760] && sized?.titleBar == 28 && sized?.controls?.close == [7, 7, 14, 14],
          "Accessibility's frame first (a size just written, not yet listed by the window server): \(String(describing: sized?.size))")
    var off = r
    off.windows[2]?.onScreen = false
    off.ax[2] = nil
    check(CoherenceEntries.make(read: off, windows: [2], joined: [], limits: [:], pictures: [:]).entries.first?.minimized == false,
          "off screen without Accessibility's word (another Space): not called minimized")
    let lone = CoherenceEntries.make(read: r, windows: [2], joined: [], limits: [:], pictures: [:])
    check(lone.focus == nil && lone.entries.map(\.id) == [2], "the key window not shown: no focus")
    var single = r
    single.windows[20] = cg(20, pid: 20, layer: 101, CGRect(x: 0, y: 900, width: 100, height: 100))
    single.windows[21] = cg(21, pid: 20, layer: 25, CGRect(x: 0, y: 1100, width: 100, height: 100))
    single.order = [20, 21] + single.order
    let s = CoherenceEntries.make(read: single, windows: [2], joined: [], limits: [:], pictures: [2: .alone])
    check(s.entries.first { $0.id == 20 }?.owner == 2 && s.entries.first { $0.id == 20 }?.inCrop == false && !s.entries.contains { $0.id == 21 },
          "a menu over no window of an app with one shown window is its; a status-level window is not")
    var hidden = r
    hidden.windows[1] = nil
    let h = CoherenceEntries.make(read: hidden, windows: [1], joined: [], limits: [:], pictures: [:])
    check(h.entries.first?.size == nil || h.entries.isEmpty, "a window only Accessibility knows, with no frame from it: not listed")
    hidden.ax[1]?.frame = a
    check(CoherenceEntries.make(read: hidden, windows: [1], joined: [], limits: [:], pictures: [:]).entries.isEmpty,
          "a window off every list (no pid to name its app by): not listed")
}

// MARK: - WindowStateReporter, against the device's reducer

/// The Windows client's WindowSet as far as kind 45 goes: gen order, full replaces, partial merges each
/// entry's fields over the one kept, removed takes children; the message's focus sets every `focused`.
struct Device {
    var windows: [UInt32: WindowStateEntry] = [:]
    var lastGen: Int?
    var secure = false
    mutating func apply(_ m: WindowStateMessage) -> Bool {
        if let g = lastGen, m.gen <= g { return false }
        lastGen = m.gen
        if let s = m.secureInput { secure = s }
        var listed = Set<UInt32>()
        for e in m.windows ?? [] {
            listed.insert(e.id)
            if var w = windows[e.id] { w = merge(w, e); windows[e.id] = w } else { windows[e.id] = e }
        }
        var gone = Set(m.removed ?? [])
        if m.full == true { for id in windows.keys where !listed.contains(id) { gone.insert(id) } }
        var more = true
        while more {
            more = false
            for w in windows.values where w.owner.map(gone.contains) == true && gone.insert(w.id).inserted { more = true }
        }
        for id in gone { windows[id] = nil }
        if let f = m.focus { for id in windows.keys { windows[id]?.focused = id == f } }
        return true
    }
    func merge(_ a: WindowStateEntry, _ e: WindowStateEntry) -> WindowStateEntry {
        var w = a
        if let v = e.app { w.app = v }; if let v = e.appName { w.appName = v }; if let v = e.title { w.title = v }
        if let v = e.role { w.role = v }; if let v = e.size { w.size = v }; if let v = e.min { w.min = v }; if let v = e.max { w.max = v }
        if let v = e.resizable { w.resizable = v }; if let v = e.focused { w.focused = v }; if let v = e.minimized { w.minimized = v }
        if let v = e.fullScreen { w.fullScreen = v }; if let v = e.modal { w.modal = v }; if let v = e.titleBar { w.titleBar = v }
        if let v = e.joined { w.joined = v }; if let v = e.controls { w.controls = v }; if let v = e.owner { w.owner = v }
        if let v = e.rect { w.rect = v }; if let v = e.inCrop { w.inCrop = v }
        return w
    }
}
func result(_ entries: [WindowStateEntry], focus: UInt32? = nil) -> CoherenceEntries.Result {
    CoherenceEntries.Result(entries: entries, order: entries.map(\.id), focus: focus, owners: [:])
}
do {
    var rep = WindowStateReporter()
    let w1 = WindowStateEntry(id: 1, title: "A", size: [800, 600], focused: true)
    let w2 = WindowStateEntry(id: 2, title: "B", size: [400, 300], focused: false)
    let m1 = rep.message(result([w1, w2], focus: 1), secureInput: false, at: 0)
    check(m1?.gen == 1 && m1?.full == true && m1?.windows == [w1, w2] && m1?.order == [1, 2] && m1?.focus == 1 && m1?.secureInput == false
          && m1?.removed == nil && m1?.answering == nil, "the first is full: \(String(describing: m1))")
    check(rep.message(result([w1, w2], focus: 1), secureInput: false, at: 0.1) == nil, "nothing changed: nothing")
    var w2b = w2; w2b.title = "B2"
    let m2 = rep.message(result([w1, w2b], focus: 1), secureInput: false, at: 0.2)
    check(m2?.gen == 2 && m2?.full == nil && m2?.windows == [w2b] && m2?.order == nil && m2?.focus == nil && m2?.secureInput == nil,
          "a title changed: that window's whole entry: \(String(describing: m2))")
    let m3 = rep.message(result([w2b], focus: nil), secureInput: true, at: 0.3)
    check(m3?.removed == [1] && m3?.windows == nil && m3?.order == [2] && m3?.secureInput == true && m3?.focus == nil,
          "a window gone: removed, the order, Secure Input on: \(String(describing: m3))")
    let m4 = rep.message(result([w2b], focus: nil), secureInput: true, answering: (7, 2), at: 0.4)
    check(m4?.answering == 7 && m4?.windows == [w2b] && m4?.gen == 4, "a kind 46's answer goes whatever changed, with the window's entry")
    let m4b = rep.message(result([w2b], focus: nil), secureInput: true, answering: (8, 99), at: 0.45)
    check(m4b?.answering == 8 && m4b?.windows == nil && m4b?.removed == nil, "an answer for a window gone meanwhile still goes")
    rep.gap()
    check(rep.message(result([w2b], focus: nil), secureInput: true, at: 0.5)?.full == true, "after a gap: full")
    check(rep.message(result([w2b], focus: nil), secureInput: true, at: 11) == nil, "no change for a long while: still nothing")
    var w2c = w2b; w2c.size = [500, 300]
    check(rep.message(result([w2c], focus: nil), secureInput: true, at: 11)?.full == true, "a change after 10 s with none: full")
    check(rep.entry(2) == w2c && rep.gen == 7, "it keeps what it sent")
    var w2d = w2c; w2d.focused = true
    let m7 = rep.message(result([w2d], focus: 2), secureInput: true, at: 12)
    check(m7?.focus == 2 && m7?.windows == [w2d], "the focus moved: focus and the entry")
}
// Random walks: after every message the device's windows are the host's, whole.
func randomEntry(_ id: UInt32, owner: UInt32?) -> WindowStateEntry {
    var e = WindowStateEntry(id: id, app: ["a", "b"].randomElement(using: &rng), title: Bool.random(using: &rng) ? "t\(Int.random(in: 0...3, using: &rng))" : "x",
                             role: owner == nil ? "window" : "menu", size: [Double(Int.random(in: 1...3, using: &rng) * 100), 300],
                             focused: false, minimized: Bool.random(using: &rng))
    if let owner { e.owner = owner; e.rect = [0, Double(Int.random(in: 0...2, using: &rng)), 10, 10]; e.inCrop = Bool.random(using: &rng) }
    return e
}
var walks = 0
for _ in 0..<1_500 {
    var rep = WindowStateReporter()
    var dev = Device()
    var t = 0.0
    var ok = true
    for _ in 0..<30 {
        t += [0.05, 0.5, 3, 12].randomElement(using: &rng)!
        let tops = (1...5).filter { _ in Bool.random(using: &rng) }.map(UInt32.init)
        var entries = tops.map { randomEntry($0, owner: nil) }
        for top in tops where Int.random(in: 0..<3, using: &rng) == 0 { entries.append(randomEntry(100 + top, owner: top)) }
        let focus = tops.randomElement(using: &rng)
        for i in entries.indices where entries[i].owner == nil { entries[i].focused = entries[i].id == focus }
        if Int.random(in: 0..<10, using: &rng) == 0 { rep.gap() }
        let secure = Int.random(in: 0..<5, using: &rng) == 0
        let answering: (Int, UInt32)? = tops.isEmpty || !Bool.random(using: &rng) ? nil : (Int.random(in: 1...99, using: &rng), tops[0])
        let before = rep.gen
        if let m = rep.message(result(entries, focus: focus), secureInput: secure, answering: answering, at: t) {
            if m.gen != before + 1 || !dev.apply(m) { ok = false }
            if let a = answering, m.answering != a.0 || !(m.full == true || m.windows?.contains { $0.id == a.1 } == true) { ok = false }
        } else if answering != nil { ok = false }
        let want = Dictionary(uniqueKeysWithValues: entries.map { ($0.id, $0) })
        if dev.windows != want || dev.secure != secure { ok = false }
        if !ok { check(false, "random walk: the device has \(dev.windows) where the host has \(want)"); break }
    }
    if !ok { break }
    walks += 1
}
check(walks == 1_500, "1,500 random walks of 30 changes each: the device's windows are the host's after every message")

// MARK: - SendPacer

do {
    var p = SendPacer()
    check(p.wait(now: 5) == 0, "the first goes at once")
    p.sent(at: 5)
    check(abs(p.wait(now: 5.01) - 0.04) < 1e-9 && p.wait(now: 5.05) == 0 && p.wait(now: 9) == 0, "the next 50 ms after the last at the soonest")
    check(SendPacer.perSecond == 20, "20 a second")
    // A burst of changes every 5 ms for a second: at most 20 messages, the last change in the last message.
    var q = SendPacer()
    var sentTimes: [Double] = []
    var due: Double?
    var t = 0.0
    while t < 1 {
        if due == nil { due = t + q.wait(now: t) }
        if let d = due, t >= d { q.sent(at: t); sentTimes.append(t); due = nil }
        t += 0.005
    }
    check(sentTimes.count <= 21 && sentTimes.count >= 18 && zip(sentTimes, sentTimes.dropFirst()).allSatisfy { $1 - $0 >= 0.05 - 1e-9 },
          "a burst: about 20 a second, never two within 50 ms: \(sentTimes.count)")
}

// MARK: - SizeLimits

do {
    var l = SizeLimits()
    check(l.min == nil && l.max == nil, "nothing known")
    l.learn(asked: CGSize(width: 300, height: 200), took: CGSize(width: 500, height: 201))
    check(l.min == [500, 1] && l.max == nil, "refused to be narrower: its least width, the height within the slack")
    l.learn(asked: CGSize(width: 4000, height: 3000), took: CGSize(width: 2000, height: 3000))
    check(l.min == [500, 1] && l.max == [2000, SizeLimits.unknown], "refused to be wider: its most width")
    l.learn(asked: CGSize(width: 400, height: 3000), took: CGSize(width: 400, height: 3000))
    check(l.min == nil && l.max == [2000, SizeLimits.unknown], "took a width under the least learned: forgotten")
    l.learn(asked: CGSize(width: 2500, height: 500), took: CGSize(width: 2500, height: 500))
    check(l.max == nil, "took a width over the most learned: forgotten")
    l.learn(asked: CGSize(width: 100, height: 100), took: CGSize(width: 300, height: 250))
    l.learn(asked: CGSize(width: 200, height: 100), took: CGSize(width: 150, height: 250))
    check(l.min == [1, 250] && l.max == [150, SizeLimits.unknown], "a most under the least learned drops the least")
    var k = SizeLimits()
    k.learn(asked: CGSize(width: 200, height: 500), took: CGSize(width: 150, height: 500))
    k.learn(asked: CGSize(width: 100, height: 500), took: CGSize(width: 300, height: 500))
    check(k.min == [300, 1] && k.max == nil, "a least over the most learned drops the most")
    var n = SizeLimits()
    n.learn(asked: CGSize(width: CGFloat.nan, height: 0), took: CGSize(width: 10, height: 10))
    check(n == SizeLimits(), "nonsense learns nothing")
}
for _ in 0..<2_000 {
    var l = SizeLimits()
    let lo = Double.random(in: 200...800, using: &rng).rounded(), hi = Double.random(in: 1000...3000, using: &rng).rounded()
    for _ in 0..<10 {
        let a = Double.random(in: 50...4000, using: &rng).rounded()
        l.learn(asked: CGSize(width: a, height: 500), took: CGSize(width: min(max(a, lo), hi), height: 500))
    }
    // A window that holds a fixed least and most: whatever was learned lies within them.
    let ok = (l.minW.map { $0 == lo } ?? true) && (l.maxW.map { $0 == hi } ?? true) && l.minH == nil && l.maxH == nil
    if !ok { check(false, "random limits of a window between \(lo) and \(hi): \(l)"); break }
}

// MARK: - PlaceSize, PlaceLimiter

check(PlaceSize.asked([1100.4, 760.6], room: nil) == CGSize(width: 1100, height: 761), "whole points")
check(PlaceSize.asked([5000, 3000], room: CGSize(width: 2560, height: 1400)) == CGSize(width: 2560, height: 1400)
      && PlaceSize.asked([0.2, 10], room: nil) == CGSize(width: 1, height: 10) && PlaceSize.asked([3, 10], room: CGSize(width: 0, height: 0)) == CGSize(width: 3, height: 10),
      "held to the device's room (none without one); at least a point")
check(PlaceSize.asked([100], room: nil) == nil && PlaceSize.asked([.nan, 100], room: nil) == nil && PlaceSize.asked([-5, 100], room: nil) == nil
      && PlaceSize.asked([1e300, 1e300], room: nil) == CGSize(width: 16384, height: 16384), "two positive numbers, 16384 at most")
do {
    var l = PlaceLimiter()
    let p1 = PlaceWindow(id: 1, size: [500, 400], token: 1), p2 = PlaceWindow(id: 1, size: [510, 400], token: 2),
        p3 = PlaceWindow(id: 1, size: [520, 400], token: 3), q = PlaceWindow(id: 2, size: [300, 300], token: 4)
    check(l.offer(p1, now: 10) == .now, "the first at once")
    check(l.offer(p2, now: 10.01) == .held(until: 10.05) && l.offer(q, now: 10.01) == .now, "the next within 50 ms held; another window's at once")
    check(l.offer(p3, now: 10.02) == .held(until: 10.05) && l.held[1] == p3, "the newest replaces the one held")
    check(l.nextDue == 10.05, "the next due at the end of the window's interval")
    check(l.due(now: 10.04).isEmpty && l.due(now: 10.05) == [p3] && l.held.isEmpty && l.nextDue == nil, "due once the interval is over")
    check(l.offer(p1, now: 10.2) == .now, "then at once again")
    l.forget(1)
    check(l.offer(p2, now: 10.21) == .now, "forgotten: at once")
}
for _ in 0..<500 {
    var l = PlaceLimiter()
    var applied: [Double] = []
    var t = 0.0
    var last: PlaceWindow?
    var ok = true
    for i in 0..<200 {
        t += Double.random(in: 0.001...0.03, using: &rng)
        let p = PlaceWindow(id: 1, size: [Double(i), 1], token: i)
        last = p
        if l.offer(p, now: t) == .now { applied.append(t) }
        for _ in l.due(now: t) { applied.append(t) }
    }
    t += 1
    if let p = l.due(now: t).last { applied.append(t); if p != last { ok = false } }
    if !zip(applied, applied.dropFirst()).allSatisfy({ $1 - $0 >= PlaceLimiter.interval - 1e-9 }) { ok = false }
    if !ok { check(false, "random places: two within 50 ms, or the last one lost"); break }
}

// MARK: - Input and activation

check(CoherenceInput.needsFocus(.pointer(.leftDown, x: 0, y: 0)) && CoherenceInput.needsFocus(.pointer(.rightDown, x: 0, y: 0))
      && CoherenceInput.needsFocus(.pointer(.middleDown, x: 0, y: 0)) && CoherenceInput.needsFocus(.key(hidUsage: 4, down: true, modifiers: 0))
      && CoherenceInput.needsFocus(.text("a")), "a button down, a key down, text need the window key")
check(!CoherenceInput.needsFocus(.pointer(.move, x: 0, y: 0)) && !CoherenceInput.needsFocus(.pointer(.leftUp, x: 0, y: 0))
      && !CoherenceInput.needsFocus(.key(hidUsage: 4, down: false, modifiers: 0)) && !CoherenceInput.needsFocus(.scroll(x: 0, y: 0, dx: 0, dy: 1))
      && !CoherenceInput.needsFocus(.scrollGesture(.began, x: 0, y: 0)), "moves, ups and scrolls never")
do {
    var g = ActivationGate()
    check(!g.shouldActivate(window: 1, isKey: true, now: 0), "a key window is not activated")
    check(g.shouldActivate(window: 1, isKey: false, now: 0) && !g.shouldActivate(window: 1, isKey: false, now: 1),
          "a window not key: once, not again for 2 s")
    check(g.shouldActivate(window: 2, isKey: false, now: 1.1) && g.shouldActivate(window: 1, isKey: false, now: 1.2),
          "another window at once, and back to the first at once: per window, not global")
    check(!g.shouldActivate(window: 1, isKey: false, now: 2) && g.shouldActivate(window: 1, isKey: false, now: 3.3), "the same window again after 2 s")
}

// MARK: - IconSizing

check(IconSizing.drawn(asked: nil) == 64 && IconSizing.drawn(asked: 256) == 256 && IconSizing.drawn(asked: 4096) == 512
      && IconSizing.drawn(asked: 0) == 16 && IconSizing.drawn(asked: -5) == 16, "kind 4 at the hello's size, 16 to 512, else 64")

print("\(checks) checks, \(failures) failed")
exit(failures == 0 ? 0 : 1)
