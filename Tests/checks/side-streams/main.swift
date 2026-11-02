import Foundation

// The side streams' and Sill's desk's rules (Sources/SillHost/SideStreamPolicy.swift; pull request #66's,
// ported for this fork's coherence): which windows stream beside the main one, at what rate and to whom;
// which captured frames reach a window's encoder (the cap, a hidden window); which side frames go to a
// client and when a keyframe is asked for; the desk's displays (their envelope, scale, memory and
// identities); a dropped file's name on the Mac. Then random runs against each rule's properties.
var failures = 0
var checks = 0
func check(_ ok: Bool, _ what: @autoclosure () -> String, line: Int = #line) {
    checks += 1
    if !ok { failures += 1; print("FAIL (line \(line)): \(what())") }
}
var rng = SystemRandomNumberGenerator()

// MARK: - SidePlan

check(SidePlan.fps(asked: nil) == 15 && SidePlan.fps(asked: 0) == 1 && SidePlan.fps(asked: -5) == 1 && SidePlan.fps(asked: 24) == 24
      && SidePlan.fps(asked: 120) == 30, "a request's rate: 15 by default, 1 to 30")
check(SidePlan.unique([3, 1, 3, 2, 1]) == [3, 1, 2], "each window once, first listed first")
typealias Ask = SidePlan.Ask<String>
do {
    let asks = [Ask(device: "a", windows: [10, 11, 12], fps: 15), Ask(device: "b", windows: [12, 13, 10], fps: 30)]
    let p = SidePlan.make(asks: asks, main: nil, failedAt: [:], now: 100, limit: 6) { _ in true }
    check(p.order == [10, 11, 12, 13], "first asked first, over every device: \(p.order)")
    check(p.fps == [10: 30, 11: 15, 12: 30, 13: 30], "each window at the most any device asked: \(p.fps)")
    check(p.subscribers[10] == ["a", "b"] && p.subscribers[11] == ["a"] && p.subscribers[13] == ["b"], "who receives each")
    let m = SidePlan.make(asks: asks, main: 12, failedAt: [:], now: 100, limit: 6) { _ in true }
    check(m.order == [10, 11, 13] && m.fps[12] == nil && m.subscribers[12] == nil, "the main stream's window never beside it")
    let f = SidePlan.make(asks: asks, main: nil, failedAt: [11: 95, 13: 89.9], now: 100, limit: 6) { _ in true }
    check(f.order == [10, 12, 13], "a window that failed 5 s ago left alone, one 10.1 s ago tried again: \(f.order)")
    let l = SidePlan.make(asks: asks, main: nil, failedAt: [10: 99], now: 100, limit: 2) { $0 != 11 }
    check(l.order == [12, 13], "at most the limit, counting only windows that stream: \(l.order)")
    let d = SidePlan.make(asks: [Ask(device: "a", windows: [5, 5, 6, 5], fps: 15)], main: nil, failedAt: [:], now: 0, limit: 6) { _ in true }
    check(d.order == [5, 6] && d.subscribers[5] == ["a"], "a window listed twice by one device: once, one subscriber")
    check(SidePlan.make(asks: [Ask](), main: 1, failedAt: [:], now: 0, limit: 6) { _ in true } == SidePlan.Plan<String>(), "nobody asks: nothing")
}
// More windows than the limit (a Coherence device lists every window it shows): in front first (its focus, a
// child of the window in front), then the windows shown, then those hidden, each group first asked first.
do {
    let seven: [UInt32] = [1, 2, 3, 4, 5, 6, 7]
    func plan(_ asks: [Ask], limit: Int = 6, failed: [UInt32: Double] = [:]) -> [UInt32] {
        SidePlan.make(asks: asks, main: nil, failedAt: failed, now: 100, limit: limit) { _ in true }.order
    }
    check(plan([Ask(device: "a", windows: seven, fps: 60)]) == [1, 2, 3, 4, 5, 6], "seven asked, none in front or hidden: the first six asked")
    let last = plan([Ask(device: "a", windows: seven, fps: 60, front: [7])])
    check(last == [7, 1, 2, 3, 4, 5], "the focus listed seventh streams first, the sixth listed waits: \(last)")
    let moved = plan([Ask(device: "a", windows: seven, fps: 60, front: [6])])
    check(moved == [6, 1, 2, 3, 4, 5] && !moved.contains(7) && moved.contains(6),
          "the focus moves from 7 to 6: 6 starts, 7 (now the last shown) stops: \(moved)")
    let child = plan([Ask(device: "a", windows: seven + [9], fps: 60, front: [7, 9])])
    check(child == [7, 9, 1, 2, 3, 4], "the focus and its child that spills out (in front with it) first: \(child)")
    let hid = plan([Ask(device: "a", windows: seven, fps: 60, front: [5], hidden: [1, 2])])
    check(hid == [5, 3, 4, 6, 7, 1], "hidden windows after the shown ones, first asked first: \(hid)")
    check(plan([Ask(device: "a", windows: seven, fps: 60, front: [7], hidden: [7])]).first == 7, "in front beats hidden (a device never hides its focus)")
    let two = [Ask(device: "a", windows: [1, 2, 3, 4], fps: 60, hidden: [4]), Ask(device: "b", windows: [4, 5, 6, 7, 8], fps: 60, front: [8], hidden: [5])]
    check(plan(two) == [8, 1, 2, 3, 4, 6], "two devices: one's focus first; hidden only where every device that lists it hides it: \(plan(two))")
    let other = plan([Ask(device: "a", windows: seven, fps: 60, front: [7]), Ask(device: "b", windows: [7, 1], fps: 60, hidden: [7])])
    check(other.first == 7, "in front for one device, though another that shows it later hides it: still first: \(other)")
    check(plan(two, failed: [8: 99]) == [1, 2, 3, 4, 6, 7], "a focus whose stream failed lately takes no place: \(plan(two, failed: [8: 99]))")
    check(Set(plan(two, limit: 9)) == Set([1, 2, 3, 4, 5, 6, 7, 8]), "within the limit every window streams, whatever its place")
    check(plan([Ask(device: "a", windows: [3, 1, 2], fps: 15, front: [9], hidden: [8])]) == [3, 1, 2], "a device's sets name only what it asked")
}
// Random, with places: the plan is the eligible windows ordered in front, shown, hidden (the best place any
// device gives each), first asked first within each, up to the limit; the focus streams while it is eligible
// and fewer than the limit are in front.
for _ in 0..<2_000 {
    let devices = ["a", "b", "c"].prefix(Int.random(in: 1...3, using: &rng))
    let asks = devices.map { d -> Ask in
        let ws = (0..<Int.random(in: 0...9, using: &rng)).map { _ in UInt32.random(in: 1...12, using: &rng) }
        return Ask(device: d, windows: ws, fps: 30, front: Set(ws.filter { _ in Int.random(in: 0..<6, using: &rng) == 0 }),
                   hidden: Set(ws.filter { _ in Int.random(in: 0..<3, using: &rng) == 0 }))
    }
    let failed = Dictionary(uniqueKeysWithValues: (1...12).filter { _ in Int.random(in: 0..<8, using: &rng) == 0 }.map { (UInt32($0), 95.0) })
    let limit = Int.random(in: 0...7, using: &rng)
    let p = SidePlan.make(asks: asks, main: nil, failedAt: failed, now: 100, limit: limit) { _ in true }
    func place(_ id: UInt32) -> Int {
        asks.filter { $0.windows.contains(id) }.map { $0.front.contains(id) ? 0 : $0.hidden.contains(id) ? 2 : 1 }.min()!
    }
    let eligible = SidePlan.unique(asks.flatMap(\.windows)).filter { failed[$0] == nil }
    let want = Array((0...2).flatMap { g in eligible.filter { place($0) == g } }.prefix(limit))
    let fronts = eligible.filter { place($0) == 0 }
    if p.order != want || (fronts.count <= limit && !fronts.allSatisfy(p.order.contains)) {
        check(false, "random plan with places: \(asks) limit \(limit) → \(p.order), want \(want)"); break
    }
}
// Random: the plan never holds the main window, a failing one, an ineligible one, more than the limit, a
// window nobody asked for or a subscriber who did not ask for it, and keeps the first-asked order.
for _ in 0..<2_000 {
    let devices = ["a", "b", "c"].prefix(Int.random(in: 0...3, using: &rng))
    let asks = devices.map { Ask(device: $0, windows: (0..<Int.random(in: 0...6, using: &rng)).map { _ in UInt32.random(in: 1...9, using: &rng) },
                                 fps: Int.random(in: 1...30, using: &rng)) }
    let main: UInt32? = Bool.random(using: &rng) ? UInt32.random(in: 1...9, using: &rng) : nil
    let failed = Dictionary(uniqueKeysWithValues: (1...9).filter { _ in Int.random(in: 0..<4, using: &rng) == 0 }.map { (UInt32($0), Double.random(in: 80...100, using: &rng)) })
    let gone = Set((1...9).filter { _ in Int.random(in: 0..<5, using: &rng) == 0 }.map(UInt32.init))
    let limit = Int.random(in: 0...6, using: &rng)
    let p = SidePlan.make(asks: asks, main: main, failedAt: failed, now: 100, limit: limit) { !gone.contains($0) }
    let firstAsked = SidePlan.unique(asks.flatMap(\.windows))
    let ok = p.order.count <= limit && !p.order.contains { $0 == main || gone.contains($0) || (failed[$0].map { 100 - $0 < 10 } ?? false) }
        && Set(p.order).count == p.order.count
        && p.order == firstAsked.filter { p.order.contains($0) }
        && p.order.allSatisfy { id in p.subscribers[id] == asks.filter { $0.windows.contains(id) }.map(\.device)
                                     && p.fps[id] == asks.filter { $0.windows.contains(id) }.map(\.fps).max() }
        && Set(p.fps.keys) == Set(p.order) && Set(p.subscribers.keys) == Set(p.order)
    let full = firstAsked.filter { $0 != main && !gone.contains($0) && !(failed[$0].map { 100 - $0 < 10 } ?? false) }
    if !ok || p.order != Array(full.prefix(limit)) { check(false, "random plan: \(asks) main \(String(describing: main)) → \(p)"); break }
}

// MARK: - SideFrameGate

func admitted(_ gate: inout SideFrameGate, fps: Double, seconds: Double, jitter: Double = 0, from start: Double = 0) -> Int {
    var n = 0
    var t = start
    while t < start + seconds {
        if gate.admits(at: t + Double.random(in: -jitter...jitter, using: &rng)) { n += 1 }
        t += 1 / fps
    }
    return n
}
do {
    var g = SideFrameGate(cap: 30)
    check(admitted(&g, fps: 60, seconds: 10) == 300, "60 fps captured, a cap of 30: every other frame")
    var e = SideFrameGate(cap: 60)
    check(admitted(&e, fps: 60, seconds: 10, jitter: 0.002) == 600, "a cap of the capture's rate drops nothing, with 2 ms of jitter")
    var q = SideFrameGate(cap: 15)
    let quarter = admitted(&q, fps: 60, seconds: 10)
    check(quarter == 150 || quarter == 151, "a cap of 15: one in four (the second a quarter interval early at most): \(quarter)")
    var o = SideFrameGate(cap: 30)
    let odd = admitted(&o, fps: 40, seconds: 10)
    check(odd >= 295 && odd <= 301, "40 fps captured at a cap of 30: about 30 a second, never more: \(odd)")
    var h = SideFrameGate(cap: 30, hidden: true)
    let rare = admitted(&h, fps: 60, seconds: 30)
    check((10...11).contains(rare) && h.interval == 3, "a hidden window: one frame every 3 s: \(rare) in 30 s")
    var p = SideFrameGate(cap: 30)
    check(p.admits(at: 0) && !p.admits(at: 0.01) && p.admits(at: 5) && !p.admits(at: 5.01) && p.admits(at: 5.04),
          "after a pause the next frame goes at once, then the cap again")
    var c = SideFrameGate(cap: 15)
    _ = c.admits(at: 0)
    c.cap = 60; c.reset()
    check(c.admits(at: 0.017) && c.admits(at: 0.034), "a raised cap (focus) takes effect at once after reset")
    var z = SideFrameGate(cap: 0)
    check(z.interval == 1 && z.admits(at: 0) && !z.admits(at: 0.5) && z.admits(at: 1), "a cap of 0 counts as 1")
}
// Random: never more than the cap's frames over any run (plus the first), and none dropped when the
// capture is under the cap.
for _ in 0..<500 {
    let cap = Int.random(in: 1...120, using: &rng), fps = Double(Int.random(in: 5...120, using: &rng))
    var g = SideFrameGate(cap: cap)
    let n = admitted(&g, fps: fps, seconds: 5, jitter: 0.1 / fps)
    let captured = Int((5 * fps).rounded(.up))
    let most = Int((5 * Double(cap)).rounded(.up)) + 1
    if n > most || (fps * 1.3 <= Double(cap) && n < captured - 1) {
        check(false, "random gate: cap \(cap), \(fps) fps captured, \(n) of \(captured) let through"); break
    }
}

// MARK: - SideSend

let budget = SideSend.budgetHome
check(SideSend.decide(waiting: true, isKeyframe: false, haveSets: true, newSets: false, pending: 0, budget: budget) == (.skip, true),
      "a waiting client skips a delta")
check(SideSend.decide(waiting: true, isKeyframe: true, haveSets: false, newSets: false, pending: 0, budget: budget) == (.skip, true),
      "and a keyframe with no sets known")
check(SideSend.decide(waiting: true, isKeyframe: true, haveSets: true, newSets: false, pending: budget + 1, budget: budget) == (.skip, true),
      "and a keyframe while its connection is over the budget")
check(SideSend.decide(waiting: true, isKeyframe: true, haveSets: true, newSets: false, pending: budget, budget: budget) == (.send(parameterSets: true), false),
      "a waiting client gets a keyframe with the sets in front, and waits no more")
check(SideSend.decide(waiting: false, isKeyframe: false, haveSets: true, newSets: false, pending: budget + 1, budget: budget) == (.skip, true),
      "a delta over the budget is dropped, and the client waits")
check(SideSend.decide(waiting: false, isKeyframe: false, haveSets: true, newSets: false, pending: budget, budget: budget) == (.send(parameterSets: false), false),
      "a delta within the budget goes alone")
check(SideSend.decide(waiting: false, isKeyframe: true, haveSets: true, newSets: true, pending: 10 * budget, budget: budget) == (.send(parameterSets: true), false),
      "a keyframe to a client that is not waiting goes whatever waits, its new sets first")
check(SideSend.budgetHome == 196_608 && SideSend.budgetRemote == 65_536, "the budgets: 192 KB at home, 64 KB away")
check(SideSend.mayAskKeyframe(lastAsked: nil, now: 5) && !SideSend.mayAskKeyframe(lastAsked: 4.5, now: 5) && SideSend.mayAskKeyframe(lastAsked: 4, now: 5),
      "a keyframe asked once a second at most")
// Random: a client's stream as it receives it is always decodable: no delta before the keyframe it
// follows, the sets in front of every keyframe it is sent after waiting, and a client that skipped a
// delta waits for the next keyframe.
for _ in 0..<500 {
    var waiting = true, decodable = false, setsKnown = false, ok = true
    for i in 0..<200 {
        let key = i % 30 == 0 || Int.random(in: 0..<40, using: &rng) == 0
        let newSets = i == 0 || (key && Int.random(in: 0..<10, using: &rng) == 0)
        if newSets { setsKnown = true }
        let pending = Int.random(in: 0...(2 * budget), using: &rng)
        let (action, after) = SideSend.decide(waiting: waiting, isKeyframe: key, haveSets: setsKnown, newSets: newSets, pending: pending, budget: budget)
        switch action {
        case .send(let sets):
            if !key && !decodable { ok = false }
            if waiting && !sets { ok = false }
            if key { decodable = true }
        case .skip:
            if !key { decodable = false }
        }
        if !after && action == .skip { ok = false }
        waiting = after
    }
    if !ok { check(false, "random side stream: a client was sent a frame it could not decode"); break }
}

// MARK: - DeskGeometry

check(DeskGeometry.envelope(for: CGSize(width: 1280, height: 800)) == CGSize(width: 1296, height: 848),
      "a window's display: 16 points wider, 48 taller (the menu bar), rounded up to 16")
check(DeskGeometry.envelope(for: CGSize(width: 1281, height: 801)) == CGSize(width: 1312, height: 864), "rounded up")
check(DeskGeometry.envelope(for: CGSize(width: 400, height: 300)) == CGSize(width: 800, height: 800), "no side under 800 points (2× kept)")
check(DeskGeometry.envelope(for: CGSize(width: 5120, height: 2880)) == CGSize(width: 3792, height: 2160), "at most the largest 2× display")
check(DeskGeometry.scale(window: CGSize(width: 2560, height: 1440), current: nil) == 2
      && DeskGeometry.scale(window: CGSize(width: 5120, height: 1440), current: nil) == 1
      && DeskGeometry.scale(window: CGSize(width: 3700, height: 1440), current: 1) == 1
      && DeskGeometry.scale(window: CGSize(width: 3600, height: 1440), current: 1) == 2
      && DeskGeometry.scale(window: CGSize(width: 3792, height: 2160), current: 2) == 2,
      "Retina unless wider than a 2× display can be; back to 2× only 5 % within it")
check(DeskGeometry.framebuffer(widthPt: 3792, heightPt: 2160, scale: 2) == 3792.0 * 2 * 2160 * 2 * 4, "a framebuffer's bytes")
let gb = 1024.0 * 1024 * 1024
check(DeskGeometry.roomForAnother(framebuffers: [], memory: 16 * gb) && !DeskGeometry.roomForAnother(framebuffers: [], memory: 0.4 * gb),
      "the first display needs room for a largest one (131 MB) in a quarter of the memory")
let big = DeskGeometry.framebuffer(widthPt: 3792, heightPt: 2160, scale: 2)
check(DeskGeometry.roomForAnother(framebuffers: Array(repeating: big, count: 31), memory: 16 * gb)
      && !DeskGeometry.roomForAnother(framebuffers: Array(repeating: big, count: 32), memory: 16 * gb),
      "in 16 GB's quarter (4 GB) another largest display fits beside 31 of them, not beside 32")
check(DeskGeometry.roomForAnother(framebuffers: [1_000_000_000], memory: 8 * gb) && !DeskGeometry.roomForAnother(framebuffers: [1_100_000_000], memory: 8 * gb),
      "one more as large as the largest made: 1 GB beside 1 GB fits 8 GB's quarter, 1.1 GB does not")
check(!DeskGeometry.roomForAnother(framebuffers: [1_000_000_000, 200_000_000], memory: 8 * gb),
      "one more counted as the largest made, not the smallest")
check(DeskGeometry.freeSlot(used: []) == 0 && DeskGeometry.freeSlot(used: [0, 1, 3]) == 2 && DeskGeometry.freeSlot(used: [1, 2]) == 0,
      "the lowest slot free")
check(DeskGeometry.productID(slot: 0) == 0x100 && DeskGeometry.productID(slot: 5) == 0x105, "a slot's product ID, beside the iPad's 0x0001")
for _ in 0..<2_000 {
    let w = CGFloat.random(in: 1...8000, using: &rng), h = CGFloat.random(in: 1...5000, using: &rng)
    let e = DeskGeometry.envelope(for: CGSize(width: w, height: h))
    let ok = e.width >= 800 && e.height >= 800 && e.width <= 3792 && e.height <= 2160
        && e.width.truncatingRemainder(dividingBy: 16) == 0 && e.height.truncatingRemainder(dividingBy: 16) == 0
        && (w + 16 > 3792 || e.width >= w + 16) && (h + 48 > 2160 || e.height >= h + 48)
    if !ok { check(false, "random envelope of \(w)×\(h): \(e)"); break }
    let used = Set((0..<10).filter { _ in Bool.random(using: &rng) })
    let s = DeskGeometry.freeSlot(used: used)
    if used.contains(s) || (0..<s).contains(where: { !used.contains($0) }) { check(false, "random slot \(s) of \(used)"); break }
}

// MARK: - DropFileName

check(DropFileName.safe("Résumé ✓.pdf") == "Résumé ✓.pdf", "a name as it is")
check(DropFileName.safe("../../etc/passwd") == "file..-..-etc-passwd" && DropFileName.safe("a/b:c") == "a-b-c", "no path, no colon")
check(DropFileName.safe(".hidden") == "file.hidden" && DropFileName.safe("..") == "file.." && DropFileName.safe("") == "file"
      && DropFileName.safe("   ") == "file", "never hidden, never empty")
check(DropFileName.safe("a\u{0}b\nc\u{7F}.txt") == "abc.txt" && DropFileName.safe("\n.x") == "file.x", "control characters dropped")
let long = DropFileName.safe(String(repeating: "é", count: 300))
check(long.utf8.count == 200 && long.allSatisfy { $0 == "é" }, "at most 200 bytes of UTF-8, cut on a character")
let emoji = DropFileName.safe("." + String(repeating: "👩‍👩‍👧", count: 40))
check(emoji.utf8.count <= 200 && emoji.hasPrefix("file.") && emoji.dropFirst(5).allSatisfy { $0 == "👩‍👩‍👧" }, "a hidden name's length held too")
for _ in 0..<2_000 {
    let scalars = (0..<Int.random(in: 0...300, using: &rng)).compactMap { _ in
        Unicode.Scalar([0x2F, 0x3A, 0x2E, 0x00, 0x0A, 0x41, 0xE9, 0x1F469, 0x20].randomElement(using: &rng)!) }
    let name = String(String.UnicodeScalarView(scalars))
    let s = DropFileName.safe(name)
    if s.isEmpty || s.hasPrefix(".") || s.contains("/") || s.contains(":") || s.utf8.count > 200 || s.unicodeScalars.contains(where: { $0.value < 0x20 }) {
        check(false, "random name \(name.debugDescription) → \(s.debugDescription)"); break
    }
}

print(failures == 0 ? "\(checks) checks, 0 failed" : "\(failures) FAILED of \(checks)")
exit(failures == 0 ? 0 : 1)
