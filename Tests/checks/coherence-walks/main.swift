import Foundation

// Coherence's semantics across the two languages (docs/coherence-protocol.md), where coherence-fixtures
// checks only that each side reads and writes the other's JSON. Two directions, each a checked-in fixture:
//
// 1. Mac → Windows (kind 45). Seeded random walks of a simulated Mac (windows appearing, closing, retitled,
//    resized, minimized, in full screen, apps hidden; menus, submenus, tooltips, panels, dialogs, sheets and
//    popovers opening inside their owner's picture or spilling out of it, moving to another window; the key
//    window, Secure Input, a kind 46's answer, sizes a window refused and forgot; windows joining by
//    themselves, asked for, stopped by the device and now and then shown again at once, taken back at the
//    Mac; gaps, silences and a new session) go through the host's real CoherenceEntries.make and
//    WindowStateReporter. Each kind 45 the host would send is written with the set a receiver holds after it
//    by the contract (each window's fields laid over what it had, a field left out kept; a window the host no
//    longer has gone, with its children; a window the device stopped kept out of sight until a kind 45
//    removes it), and the windows that need a stream of their own. The device asks for what that set says
//    (top-level windows and the children that spill out), as the Windows client does. Stale and repeated
//    messages are delivered again. Checked here: every message carries the host's windows (nothing the host
//    knows is lost by leaving it out), gen rises by one, a gap or 10 s of silence gives a full message, an
//    answer goes with its window. The Windows client's checks (Sill.Checks, group "swift-walks") feed the same
//    messages to its real WindowSet and compare after every one.
//    Written to $WALKS_OUT; run.sh compares it with WindowsClient/Sill.Checks/Fixtures/swift-walks.jsonl.
//
// 2. Windows → Mac (kind 29). The Windows client's real CoherenceController and SideStreamPlan, driven
//    through seeded random states by Sill.Checks (`--write-kind29`), wrote each kind 29 it sent with what it
//    means by it ($WALKS_KIND29, Tests/checks/coherence-walks/kind29-walks.jsonl). Each is read here as the
//    host reads it (SideStreamRequest, CoherenceAsk, CoherenceGates.make with the owners the host named in
//    its kind 45s, SidePlan.make, CoherenceScale): the host streams those windows, the focus at the focus
//    rate, the others at `fps`, a child at its owner's rate, the hidden ones paused, at the device's scale,
//    with its apps; and of more than six, the six the contract picks (the window in front and a child of it,
//    then the windows shown, then the hidden ones, each in the order listed).
//
// Foundation only (module.txt), so it runs on Linux too. The numbers come from a SplitMix64 generator and no
// Set or Dictionary order reaches the output, so the fixture is the same on every system.
var failures = 0
var checks = 0
func check(_ ok: Bool, _ what: @autoclosure () -> String, line: Int = #line) {
    checks += 1
    if !ok { failures += 1; print("FAIL (line \(line)): \(what())") }
}
let environment = ProcessInfo.processInfo.environment

/// SplitMix64: the same numbers on every system and toolchain.
struct Seeded {
    var state: UInt64
    mutating func next() -> UInt64 {
        state &+= 0x9E37_79B9_7F4A_7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
        z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
        return z ^ (z >> 31)
    }
    mutating func int(_ n: Int) -> Int { n <= 1 ? 0 : Int(next() % UInt64(n)) }
    mutating func oneIn(_ n: Int) -> Bool { int(n) == 0 }
    mutating func pick<T>(_ a: [T]) -> T? { a.isEmpty ? nil : a[int(a.count)] }
}

func encode<T: Encodable>(_ value: T) -> String {
    let e = JSONEncoder()
    e.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
    return String(data: try! e.encode(value), encoding: .utf8)!
}

// MARK: - What a receiver keeps

/// What a receiver keeps of a window by the contract, as the fixture writes it: every field but `controls`
/// (for a device that draws the Mac's frame itself, which the Windows client does not), a Bool left out read
/// as false and `titleBar` left out as 0 (so a child, which carries neither, reads as not focused). Fields
/// that read as nothing are left out.
struct Kept: Codable, Equatable {
    var id: UInt32
    var app: String?, appName: String?, title: String?, role: String?
    var size: [Double]?, min: [Double]?, max: [Double]?
    var resizable: Bool?
    var focused: Bool?, minimized: Bool?, fullScreen: Bool?, modal: Bool?, joined: Bool?
    var titleBar: Double?
    var owner: UInt32?, rect: [Double]?, inCrop: Bool?

    init(_ e: WindowStateEntry) {
        id = e.id; app = e.app; appName = e.appName; title = e.title; role = e.role
        size = e.size; min = e.min; max = e.max; resizable = e.resizable
        focused = e.focused == true ? true : nil
        minimized = e.minimized == true ? true : nil
        fullScreen = e.fullScreen == true ? true : nil
        modal = e.modal == true ? true : nil
        joined = e.joined == true ? true : nil
        titleBar = (e.titleBar ?? 0) > 0 ? e.titleBar : nil
        owner = e.owner; rect = e.rect; inCrop = e.inCrop
    }
}

/// A device by the contract: the windows in the order first seen, each the fields laid over what it had. A
/// window it stops showing keeps its entry, out of sight, until a kind 45 removes it (`stopped`: the windows it
/// stopped, until it shows them again; a window under one of them is out of sight too).
struct ContractDevice {
    var order: [UInt32] = []
    var windows: [UInt32: WindowStateEntry] = [:]
    var stopped: Set<UInt32> = []
    var secure = false
    var lastGen: Int?

    /// Out of sight: stopped here, or under a window that is.
    func outOfSight(_ id: UInt32) -> Bool {
        var cur = id
        var seen: Set<UInt32> = []
        while true {
            if stopped.contains(cur) { return true }
            guard let o = windows[cur]?.owner, seen.insert(cur).inserted else { return false }
            if stopped.contains(o) { return true }
            guard windows[o] != nil else { return false }
            cur = o
        }
    }

    /// The windows the device shows.
    var shown: [UInt32] { order.filter { !outOfSight($0) } }

    /// `e`'s fields over `a`'s: a field `e` leaves out keeps `a`'s.
    static func over(_ a: WindowStateEntry, _ e: WindowStateEntry) -> WindowStateEntry {
        var w = a
        if let v = e.app { w.app = v }; if let v = e.appName { w.appName = v }; if let v = e.title { w.title = v }
        if let v = e.role { w.role = v }; if let v = e.size { w.size = v }; if let v = e.min { w.min = v }; if let v = e.max { w.max = v }
        if let v = e.resizable { w.resizable = v }; if let v = e.focused { w.focused = v }; if let v = e.minimized { w.minimized = v }
        if let v = e.fullScreen { w.fullScreen = v }; if let v = e.modal { w.modal = v }; if let v = e.titleBar { w.titleBar = v }
        if let v = e.joined { w.joined = v }; if let v = e.controls { w.controls = v }; if let v = e.owner { w.owner = v }
        if let v = e.rect { w.rect = v }; if let v = e.inCrop { w.inCrop = v }
        return w
    }

    mutating func put(_ e: WindowStateEntry) {
        if let w = windows[e.id] { windows[e.id] = Self.over(w, e) } else { windows[e.id] = e; order.append(e.id) }
    }

    /// These windows go, and every window owned by one that goes.
    mutating func remove(_ ids: Set<UInt32>) {
        var gone = ids
        var more = true
        while more {
            more = false
            for id in order where !gone.contains(id) {
                if let o = windows[id]?.owner, gone.contains(o) { gone.insert(id); more = true }
            }
        }
        order.removeAll { gone.contains($0) }
        for id in gone { windows[id] = nil }
    }

    /// What a device holds once a message has carried the host's windows as they are: each window's fields
    /// over what it had, and a window the host no longer has gone (children with it).
    mutating func take(_ t: CoherenceEntries.Result, secure s: Bool) {
        for e in t.entries { put(e) }
        let ids = Set(t.entries.map(\.id))
        remove(Set(order.filter { !ids.contains($0) }))
        secure = s
    }

    /// One kind 45 by the contract (this check's own reading, to see that each message carries the host's
    /// windows): an older generation dropped; entries laid over what is kept; `removed`, and for `full` every
    /// window it leaves out, gone with their children; `focus` sets every `focused`.
    mutating func apply(_ m: WindowStateMessage) -> Bool {
        if let g = lastGen, m.gen <= g { return false }
        lastGen = m.gen
        if let s = m.secureInput { secure = s }
        var listed = Set<UInt32>()
        for var e in m.windows ?? [] {
            if e.owner == e.id { e.owner = nil }
            listed.insert(e.id)
            put(e)
        }
        var gone = Set(m.removed ?? [])
        if m.full == true { for id in order where !listed.contains(id) { gone.insert(id) } }
        remove(gone)
        if let f = m.focus { for id in order { windows[id]?.focused = id == f } }
        return true
    }

    /// What it keeps of the windows it shows, and of those it stopped (kept out of sight).
    var kept: [Kept] { shown.map { Kept(windows[$0]!) } }
    var keptStopped: [Kept] { order.filter { outOfSight($0) }.map { Kept(windows[$0]!) } }

    /// The windows that need a stream of their own, by the contract: a top-level window it shows (the host never
    /// names a menu, popover or tooltip as one), and a child whose owner the device has that spills out of its
    /// owner's picture (`inCrop` false; absent counts as inside).
    var streamed: [UInt32] {
        shown.filter { id in
            let w = windows[id]!
            if let o = w.owner { return windows[o] != nil && w.inCrop == false }
            return ![WindowStateEntry.menu, WindowStateEntry.popover, WindowStateEntry.tooltip].contains(WindowStateEntry.knownRole(w.role))
        }
    }
}

// MARK: - The simulated Mac

enum ChildKind: CaseIterable {
    case menu, tooltip, panel, dialog, sheet, popover
    /// The window level it sits at (CGWindowLevel.h): a sheet and a popover at the normal level, known as
    /// children by Accessibility.
    var layer: Int {
        switch self {
        case .menu: return CoherenceEntries.popUpMenuLevel
        case .tooltip: return CoherenceEntries.helpLevel
        case .panel: return CoherenceEntries.floatingLevel
        case .dialog: return CoherenceEntries.modalPanelLevel
        case .sheet, .popover: return 0
        }
    }
}

struct MacTop {
    var id: UInt32
    var pid: Int32
    var slot: Int
    var w = 800, h = 600
    var title: String
    var subrole: String? = "AXStandardWindow"
    var minimized = false, fullScreen = false, modal = false, toolbar = false
    var resizable = true
    /// Far apart on Sill's desk, so a child's owner is never in doubt.
    var frame: CGRect { CGRect(x: slot * 3000, y: 100, width: w, height: h) }
}

struct MacChild {
    var id: UInt32
    var pid: Int32
    /// The top-level window it hangs from; a submenu's `parent` is its menu.
    var owner: UInt32
    var parent: UInt32?
    var kind: ChildKind
    /// From the owner's top-left (a submenu's: from its menu's).
    var x: Int, y: Int, w: Int, h: Int
}

struct Mac {
    var apps: [Int32: AppFacts] = [10: AppFacts(bundleID: "com.apple.TextEdit", name: "TextEdit"),
                                   20: AppFacts(bundleID: "com.apple.Safari", name: "Safari"),
                                   30: AppFacts(bundleID: "com.apple.Notes", name: "Notes"),
                                   40: AppFacts(bundleID: "com.example.Joins", name: "Joins")]
    var hidden: Set<Int32> = []
    var tops: [MacTop] = []
    var children: [MacChild] = []
    var key: UInt32?
    var secure = false
    var limits: [UInt32: SizeLimits] = [:]
    var nextID: UInt32 = 1
    var nextSlot = 0

    func top(_ id: UInt32) -> MacTop? { tops.first { $0.id == id } }
    func shown(_ t: MacTop) -> Bool { !t.minimized && !hidden.contains(t.pid) }
    var shownTops: [MacTop] { tops.filter(shown) }

    func origin(_ c: MacChild) -> CGPoint {
        if let p = c.parent, let menu = children.first(where: { $0.id == p }) {
            let o = origin(menu)
            return CGPoint(x: o.x + CGFloat(c.x), y: o.y + CGFloat(c.y))
        }
        let f = top(c.owner)!.frame
        return CGPoint(x: f.minX + CGFloat(c.x), y: f.minY + CGFloat(c.y))
    }

    func frame(_ c: MacChild) -> CGRect {
        let o = origin(c)
        return CGRect(x: o.x, y: o.y, width: CGFloat(c.w), height: CGFloat(c.h))
    }

    /// A window's children close (and their submenus).
    mutating func dropChildren(where gone: (MacChild) -> Bool) {
        var drop = Set(children.filter(gone).map(\.id))
        var more = true
        while more {
            more = false
            for c in children where !drop.contains(c.id) {
                if let p = c.parent, drop.contains(p) { drop.insert(c.id); more = true }
            }
        }
        children.removeAll { drop.contains($0.id) }
    }

    mutating func newID() -> UInt32 { defer { nextID += 1 }; return nextID }

    /// One look, as WindowRegistry reads it: every window (minimized and hidden ones off screen), the
    /// on-screen ones front to back (submenus, then the other children, then the windows), Accessibility for
    /// each window, the apps, the key window and Secure Input.
    func read() -> RegistryRead {
        var r = RegistryRead()
        for t in tops {
            r.windows[t.id] = CGFacts(id: t.id, pid: t.pid, layer: 0, frame: t.frame, onScreen: shown(t), name: t.title)
            let o = t.frame.origin
            var ax = AXFacts(title: t.title, subrole: t.subrole, minimized: t.minimized, fullScreen: t.fullScreen, modal: t.modal,
                             resizable: t.resizable,
                             close: CGRect(x: o.x + 7, y: o.y + 7, width: 14, height: 14),
                             minimize: CGRect(x: o.x + 27, y: o.y + 7, width: 14, height: 14),
                             zoom: CGRect(x: o.x + 47, y: o.y + 7, width: 14, height: 14), toolbar: t.toolbar)
            ax.frame = t.frame
            ax.sheets = children.filter { $0.owner == t.id && $0.parent == nil && $0.kind == .sheet }.map(frame)
            ax.popovers = children.filter { $0.owner == t.id && $0.parent == nil && $0.kind == .popover }.map(frame)
            r.ax[t.id] = ax
        }
        let visible = children.filter { c in top(c.owner).map(shown) ?? false }
        for c in children {
            r.windows[c.id] = CGFacts(id: c.id, pid: c.pid, layer: c.kind.layer, frame: frame(c), onScreen: top(c.owner).map(shown) ?? false)
        }
        r.order = visible.filter { $0.parent != nil }.map(\.id) + visible.filter { $0.parent == nil }.map(\.id)
            + tops.filter(shown).sorted { $0.slot > $1.slot }.map(\.id)
        for (pid, facts) in apps { r.apps[pid] = facts }
        r.keyWindow = key
        r.secureInput = secure
        return r
    }
}

// MARK: - Direction 1: the walks

/// One walk of the Mac, the host and a device that follows the contract.
struct Walk {
    let number: Int
    var rng: Seeded
    var mac = Mac()
    var reporter = WindowStateReporter()
    var device = ContractDevice()
    /// Windows the device asked for that the host has not listed yet.
    var asks: [UInt32] = []
    /// Windows that joined by themselves, not listed by the device yet; every one that ever joined.
    var pendingJoins: [UInt32] = []
    var joined: Set<UInt32> = []
    /// Taken back at the Mac: out of the set for good.
    var refused: Set<UInt32> = []
    /// The messages of this session (for stale deliveries).
    var sent: [(gen: Int, json: String)] = []
    var time = 0.0
    var lastSentAt: Double?
    var owesFull = true
    var token = 0
    var lines: [String] = []
    var stats = Coverage()

    init(number: Int) {
        self.number = number
        rng = Seeded(state: 0x5111_C0DE_0000 &+ UInt64(number) &* 7919)
    }

    var step = 0

    mutating func emit(_ event: String, _ fields: String = "") {
        lines.append("{\"walk\":\(number),\"step\":\(step),\"event\":\"\(event)\"\(fields.isEmpty ? "" : "," + fields)}")
    }

    /// What the device holds after an event: the windows it shows, those stopped and kept out of sight (when
    /// any), Secure Input, and the windows that need a stream.
    var expect: String {
        let stopped = device.keptStopped.isEmpty ? "" : ",\"stopped\":\(encode(device.keptStopped))"
        return "\"expect\":{\"windows\":\(encode(device.kept))\(stopped),\"secureInput\":\(device.secure),\"streamed\":\(encode(device.streamed))}"
    }

    /// The windows the device asks for (its kind 29): what it shows by the contract, then what it asked for.
    var deviceList: [UInt32] { device.streamed + asks.filter { !device.streamed.contains($0) } }

    mutating func run(steps: Int) {
        emit("start")
        // A few windows open before Coherence starts; the device asks for some of them.
        for _ in 0..<(2 + rng.int(3)) { newTop() }
        for t in mac.tops where rng.int(3) > 0 { asks.append(t.id) }
        if asks.isEmpty, let t = mac.tops.first { asks.append(t.id) }
        for s in 0..<steps {
            step = s
            deviceEvents()
            macEvents()
            if rng.oneIn(18), let id = rng.pick(device.order.filter { device.windows[$0]?.owner == nil && !pendingJoins.contains($0) }) {
                // Taken back at the Mac (kind 41): the device closes it (and what it owns); the host never
                // shows it to this device again.
                refused.insert(id)
                device.remove([id])
                device.stopped.remove(id)
                asks.removeAll { $0 == id }
                pendingJoins.removeAll { $0 == id }
                stats.takenBack += 1
                emit("41", "\"json\":\(encode(TakenBack(windows: [id]))),\(expect)")
            }
            if rng.oneIn(25) {
                // A new session (a reconnect): generations start again, and the first message is full.
                reporter = WindowStateReporter()
                device.lastGen = nil
                sent = []
                owesFull = true
                stats.sessions += 1
                emit("session")
            }
            look()
            redeliver()
        }
    }

    // MARK: The device

    mutating func deviceEvents() {
        let list = deviceList
        pendingJoins.removeAll { list.contains($0) }
        // The person picks another Mac window, or one the device stopped showing, at once, whether or not a
        // message has gone since (asked for again before the host's next kind 45, it is still the device's to
        // the host, which sends nothing of it: the device shows it from the entry it kept).
        if rng.oneIn(5), let t = rng.pick(mac.tops.filter { !device.shown.contains($0.id) && !asks.contains($0.id) && !refused.contains($0.id) }) {
            show(t.id)
        }
        // The device stops showing a window it listed (a window it closed here), and keeps its entry out of sight
        // until a kind 45 removes it; now and then it shows it again at once.
        if rng.oneIn(14), let id = rng.pick(device.order.filter { device.windows[$0]?.owner == nil && list.contains($0) }) {
            device.stopped.insert(id)
            asks.removeAll { $0 == id }
            stats.forgotten += 1
            emit("forget", "\"window\":\(id),\(expect)")
            if rng.oneIn(2) { show(id) }
        }
        asks.removeAll { id in device.shown.contains(id) || mac.top(id) == nil }
    }

    /// Another window to show: one stopped comes back from the entry kept if no kind 45 has removed it (the
    /// Windows client's WindowSet.Show), any other is asked for until the host lists it.
    mutating func show(_ id: UInt32) {
        if device.stopped.contains(id) {
            device.stopped.remove(id)
            if device.windows[id] != nil { stats.shownFromKept += 1 } else { stats.askedAgain += 1 }
            emit("show", "\"window\":\(id),\(expect)")
        }
        if !device.shown.contains(id) { asks.append(id) }
    }

    // MARK: The Mac

    mutating func newTop(pid: Int32? = nil) {
        let p = pid ?? rng.pick([10, 20, 20, 30])!
        let id = mac.newID()
        let t = MacTop(id: id, pid: p, slot: mac.nextSlot, w: 400 + 50 * rng.int(17), h: 300 + 50 * rng.int(13), title: "w\(id)")
        mac.nextSlot += 1
        mac.tops.append(t)
    }

    mutating func openChild() {
        let list = deviceList
        let shownListed = mac.shownTops.filter { list.contains($0.id) }
        guard let t = rng.pick(!shownListed.isEmpty && rng.int(4) > 0 ? shownListed : mac.shownTops) else { return }
        let kind = rng.pick(ChildKind.allCases)!
        let spill = rng.oneIn(2)
        var w = 80 + 20 * rng.int(16), h = 40 + 20 * rng.int(16)
        var x: Int, y: Int
        switch kind {
        case .sheet:
            w = Swift.min(w + 200, t.w - 20)
            if spill { h = t.h }   // taller than the window under its title bar: it spills out at the bottom
            x = (t.w - w) / 2; y = 28
        case .tooltip:
            w = Swift.min(w, 200); h = 20
            x = rng.int(Swift.max(t.w - w, 1)); y = spill ? t.h - 10 : rng.int(Swift.max(t.h - h, 1))
        default:
            // Its middle inside the window, or hanging over its right or bottom edge (the middle still inside
            // or the window the one it overlaps most).
            if spill {
                if rng.oneIn(2) { x = t.w - w / 3; y = rng.int(Swift.max(t.h - h, 1)) }
                else { x = rng.int(Swift.max(t.w - w, 1)); y = t.h - h / 3 }
            } else {
                w = Swift.min(w, t.w - 2); h = Swift.min(h, t.h - 2)
                x = 1 + rng.int(Swift.max(t.w - w - 1, 1)); y = 1 + rng.int(Swift.max(t.h - h - 1, 1))
            }
        }
        mac.children.append(MacChild(id: mac.newID(), pid: t.pid, owner: t.id, parent: nil, kind: kind, x: x, y: y, w: w, h: h))
    }

    /// A menu, panel or tooltip moves to another window of its app (its submenus close).
    mutating func moveChild() {
        let movable = mac.children.filter { c in
            c.parent == nil && [ChildKind.menu, .panel, .tooltip].contains(c.kind) && mac.shownTops.contains { $0.pid == c.pid && $0.id != c.owner }
        }
        let list = deviceList
        guard let c = rng.pick(movable) else { return }
        let others = mac.shownTops.filter { $0.pid == c.pid && $0.id != c.owner }
        guard let other = rng.pick(others.contains { list.contains($0.id) } ? others.filter { list.contains($0.id) } : others),
              let j = mac.children.firstIndex(where: { $0.id == c.id }) else { return }
        mac.dropChildren { $0.parent == c.id }
        let k = mac.children.firstIndex(where: { $0.id == c.id }) ?? j
        mac.children[k].owner = other.id
        mac.children[k].x = 1; mac.children[k].y = 1
        mac.children[k].w = Swift.min(c.w, other.w - 2); mac.children[k].h = Swift.min(c.h, other.h - 2)
    }

    /// Sizes a window refused (a kind 46 the window would not take), and later took beyond them.
    mutating func limits() {
        let list = deviceList
        let listed = mac.tops.filter { list.contains($0.id) }
        if let t = rng.pick(!listed.isEmpty && rng.int(4) > 0 ? listed : mac.tops) {
            var l = mac.limits[t.id] ?? SizeLimits()
            let size = CGSize(width: t.w, height: t.h)
            switch l.minW != nil || l.maxH != nil ? rng.int(4) : rng.int(2) {
            case 0: l.learn(asked: CGSize(width: t.w - 150, height: t.h), took: size)
            case 1: l.learn(asked: CGSize(width: t.w, height: t.h + 400), took: size)
            default:
                // It took a size under its least, or over its most: the limit is forgotten.
                if let m = l.minW { l.learn(asked: CGSize(width: m - 100, height: Double(t.h)), took: CGSize(width: m - 100, height: Double(t.h))) }
                if let m = l.maxH { l.learn(asked: CGSize(width: Double(t.w), height: m + 100), took: CGSize(width: Double(t.w), height: m + 100)) }
            }
            mac.limits[t.id] = l
        }
    }

    /// Another key window: a shown one, none, or another app's.
    mutating func moveKey() { mac.key = rng.oneIn(5) ? 9999 : (rng.oneIn(6) ? nil : rng.pick(mac.shownTops)?.id) }

    mutating func macEvents() {
        for _ in 0..<(1 + rng.int(3)) {
            switch rng.int(28) {
            case 0, 1: newTop()
            case 2:
                // A window closes (one with children open, more often than not), and its children with it.
                let list = deviceList
                let owning = mac.tops.filter { t in list.contains(t.id) && mac.children.contains { $0.owner == t.id } }
                if let t = rng.pick(!owning.isEmpty && rng.oneIn(2) ? owning : mac.tops) {
                    mac.tops.removeAll { $0.id == t.id }
                    mac.dropChildren { $0.owner == t.id }
                    if mac.key == t.id { mac.key = nil }
                }
            case 3: if let i = rng.pick(Array(mac.tops.indices)) { mac.tops[i].title = "w\(mac.tops[i].id).\(rng.int(9))" }
            case 4: if let i = rng.pick(Array(mac.tops.indices)) { mac.tops[i].w = 400 + 50 * rng.int(17); mac.tops[i].h = 300 + 50 * rng.int(13) }
            case 5:
                if let i = rng.pick(Array(mac.tops.indices)) {
                    mac.tops[i].minimized.toggle()
                    // Its menus and tooltips close; its sheets, popovers, panels and dialogs go off screen with it.
                    let id = mac.tops[i].id
                    if mac.tops[i].minimized { mac.dropChildren { $0.owner == id && [.menu, .tooltip].contains($0.kind) } }
                }
            case 6: if let i = rng.pick(Array(mac.tops.indices)) { mac.tops[i].fullScreen.toggle() }
            case 7: if let i = rng.pick(Array(mac.tops.indices)) { mac.tops[i].toolbar.toggle() }
            case 8: if let i = rng.pick(Array(mac.tops.indices)) { mac.tops[i].subrole = rng.pick(["AXStandardWindow", "AXDialog", "AXFloatingWindow", nil])! }
            case 9: if let i = rng.pick(Array(mac.tops.indices)) { mac.tops[i].modal.toggle() }
            case 10: if let i = rng.pick(Array(mac.tops.indices)) { mac.tops[i].resizable.toggle() }
            case 11:
                let pid = rng.pick([10, 20, 30] as [Int32])!
                if mac.hidden.contains(pid) { mac.hidden.remove(pid) } else {
                    mac.hidden.insert(pid)
                    mac.dropChildren { $0.pid == pid && [.menu, .tooltip].contains($0.kind) }
                }
            case 12, 13, 21: openChild()
            case 14:
                if let menu = rng.pick(mac.children.filter { $0.kind == .menu && $0.parent == nil }) {
                    mac.children.append(MacChild(id: mac.newID(), pid: menu.pid, owner: menu.owner, parent: menu.id, kind: .menu,
                                                 x: menu.w - 4, y: 20, w: 120 + 20 * rng.int(6), h: 60 + 20 * rng.int(6)))
                }
            case 15: if let c = rng.pick(mac.children) { mac.dropChildren { $0.id == c.id } }
            case 16:
                if let i = rng.pick(Array(mac.children.indices)), mac.children[i].parent == nil, let t = mac.top(mac.children[i].owner) {
                    // Moved: inside the window, or over its edge.
                    let c = mac.children[i]
                    mac.children[i].x = rng.oneIn(2) ? t.w - c.w / 3 : Swift.max(1, Swift.min(rng.int(t.w), t.w - c.w - 1))
                }
            case 17, 22: moveChild()
            case 18, 23, 24: moveKey()
            case 19: mac.secure.toggle()
            case 20, 25: limits()
            default:
                // A window of an app the device asked for opens and joins by itself.
                if rng.oneIn(2) {
                    newTop(pid: 40)
                    let id = mac.tops.last!.id
                    pendingJoins.append(id)
                    joined.insert(id)
                }
            }
        }
    }

    // MARK: The host

    mutating func look() {
        time += rng.oneIn(16) ? 12 : [0.06, 0.1, 0.3, 0.8][rng.int(4)]
        if rng.oneIn(16) { reporter.gap(); owesFull = true }
        let list = deviceList.filter { !refused.contains($0) }
        let effective = list + pendingJoins.filter { !list.contains($0) && !refused.contains($0) }
        // A kind 46's answer: the window took a size, and the message says which token it answers.
        var answering: (token: Int, window: UInt32)?
        if rng.oneIn(6), let id = rng.pick(effective.filter { mac.top($0) != nil }), let i = mac.tops.firstIndex(where: { $0.id == id }) {
            mac.tops[i].w = 400 + 50 * rng.int(17)
            token += 1
            answering = (token, id)
        }
        let read = mac.read()
        var pictures: [UInt32: SidePicture] = [:]
        for id in effective {
            if let t = mac.top(id) { pictures[id] = rng.oneIn(7) ? .unknown : .withApp(t.frame) }   // .unknown: its stream restarting
            else { pictures[id] = .alone }
        }
        let truth = CoherenceEntries.make(read: read, windows: effective, joined: joined, limits: mac.limits, pictures: pictures)
        stats.spilled += truth.entries.filter { $0.owner != nil && $0.inCrop == false && effective.contains($0.id) }.count
        let now = Set(truth.entries.map(\.id))
        let unknownInCrop = truth.entries.filter { $0.owner != nil && $0.inCrop == nil && device.windows[$0.id]?.inCrop != nil }.count
        let newOwner = truth.entries.filter { e in e.owner != nil && device.windows[e.id]?.owner.map { $0 != e.owner } == true }.count
        let goneWithOwner = device.order.filter { id in device.windows[id]?.owner.map { !now.contains($0) } == true }.count
        let forgotLimit = truth.entries.filter { e in
            guard let had = device.windows[e.id] else { return false }
            return (had.min != nil && had.min != [1, 1] && e.min == [1, 1])
                || (had.max != nil && had.max != [SizeLimits.unknown, SizeLimits.unknown] && e.max == [SizeLimits.unknown, SizeLimits.unknown])
        }.count
        // The pace (20 a second) may hold a look's changes: the next message carries them with whatever else
        // changes meanwhile.
        if answering == nil && rng.oneIn(6) { return }
        let before = reporter.gen
        let silent = lastSentAt.map { time - $0 >= WindowStateReporter.gapAfter } ?? true
        guard let m = reporter.message(truth, secureInput: read.secureInput, answering: answering, at: time) else {
            var after = device
            after.take(truth, secure: read.secureInput)
            check(after.kept == device.kept && after.keptStopped == device.keptStopped && after.secure == device.secure,
                  "walk \(number) step \(step): no message, though the device would see a change: \(encode(after.kept)) against \(encode(device.kept)), "
                  + "kept \(encode(after.keptStopped)) against \(encode(device.keptStopped))")
            check(answering == nil, "walk \(number) step \(step): an answer that never went")
            return
        }
        // What the contract can carry: a field the host sent is never taken back by leaving it out, but for
        // `inCrop` (absent: not known now; the device keeps what it had).
        for e in truth.entries {
            guard let had = device.windows[e.id] else { continue }
            var lost: [String] = []
            if had.app != nil && e.app == nil { lost.append("app") }
            if had.appName != nil && e.appName == nil { lost.append("appName") }
            if had.title != nil && e.title == nil { lost.append("title") }
            if had.role != nil && e.role == nil { lost.append("role") }
            if had.size != nil && e.size == nil { lost.append("size") }
            if had.min != nil && e.min == nil { lost.append("min") }
            if had.max != nil && e.max == nil { lost.append("max") }
            if had.resizable != nil && e.resizable == nil { lost.append("resizable") }
            if had.focused != nil && e.focused == nil { lost.append("focused") }
            if had.minimized != nil && e.minimized == nil { lost.append("minimized") }
            if had.fullScreen != nil && e.fullScreen == nil { lost.append("fullScreen") }
            if had.modal != nil && e.modal == nil { lost.append("modal") }
            if had.titleBar != nil && e.titleBar == nil { lost.append("titleBar") }
            if had.joined != nil && e.joined == nil { lost.append("joined") }
            if had.controls != nil && e.controls == nil { lost.append("controls") }
            if had.owner != nil && e.owner == nil { lost.append("owner") }
            if had.rect != nil && e.rect == nil { lost.append("rect") }
            check(lost.isEmpty, "walk \(number) step \(step): window \(e.id) loses \(lost) by leaving them out, which a device keeps: had \(encode(had)), now \(encode(e))")
        }
        var expected = device
        expected.take(truth, secure: read.secureInput)
        var reading = device
        let applied = reading.apply(m)
        check(applied && m.gen == before + 1, "walk \(number) step \(step): gen \(m.gen) after \(before)")
        // A window the device stopped and showed again before the host read the stop is not in the message (the
        // host still counts it as the device's): the device shows it from the entry it kept.
        check(reading.kept == expected.kept && reading.keptStopped == expected.keptStopped && reading.secure == expected.secure,
              "walk \(number) step \(step): the message does not carry the host's windows:\n  message \(encode(m))\n  device  \(encode(reading.kept)) kept \(encode(reading.keptStopped))\n  host    \(encode(expected.kept)) kept \(encode(expected.keptStopped))")
        if owesFull || silent { check(m.full == true, "walk \(number) step \(step): a gap or 10 s of silence, and the message is not full: \(encode(m))") }
        if let a = answering {
            stats.answers += 1
            check(m.answering == a.token && (m.full == true || truth.entries.allSatisfy { $0.id != a.window } || m.windows?.contains { $0.id == a.window } == true),
                  "walk \(number) step \(step): the answer to \(a.token) without its window's entry: \(encode(m))")
        }
        device = expected
        device.lastGen = m.gen
        owesFull = false
        lastSentAt = time
        let json = encode(m)
        sent.append((m.gen, json))
        stats.messages += 1
        if m.full == true { stats.full += 1 }
        if m.full != true && m.focus != nil { stats.focusMoves += 1 }
        stats.keptInCrop += unknownInCrop
        stats.newOwner += newOwner
        stats.goneWithOwner += goneWithOwner
        stats.forgotLimit += forgotLimit
        emit("45", "\"gen\":\(m.gen),\"full\":\(m.full == true)" + (m.answering.map { ",\"answering\":\($0)" } ?? "")
             + ",\"applied\":true,\"json\":\(json),\(expect)")
    }

    /// A message delivered again late (an older generation: dropped whole), or twice (the same one: what the
    /// device holds is the same either way).
    mutating func redeliver() {
        guard sent.count >= 2, rng.oneIn(9) else { return }
        if rng.oneIn(3) {
            let last = sent[sent.count - 1]
            emit("45", "\"gen\":\(last.gen),\"repeat\":true,\"json\":\(last.json),\(expect)")
        } else {
            let old = sent[rng.int(sent.count - 1)]
            stats.stale += 1
            emit("45", "\"gen\":\(old.gen),\"applied\":false,\"json\":\(old.json),\(expect)")
        }
    }
}

// MARK: - Direction 2: kind 29 as the host reads it

/// What the host's WindowList says it streams beside the main window (SideStreams.maxWindows, which this
/// check cannot compile: SideStreams.swift is the host's AppKit side).
let advertisedSideStreams = 6

struct Kind29Line: Decodable {
    /// A window the device means to stream; `front`: it is the window in front or a child of it.
    struct Stream: Decodable { let id: UInt32; let fps: Int; let hidden: Bool; let front: Bool? }
    struct Meaning: Decodable {
        let leave: Bool?
        let windows: [Stream]?
        let focus: UInt32?
        let fps: Int?
        let focusFPS: Int?
        let scale: Double?
        let screen: [Double]?
        let apps: [String]?
        let all: Bool?
    }
    let walk: Int
    let step: Int
    let request: SideStreamRequest
    /// Each child's top-level owner as the host named it in its kind 45s ([child, owner] pairs).
    let owners: [[UInt32]]?
    let meaning: Meaning
}

func readKind29(_ path: String) {
    guard let text = try? String(contentsOfFile: path, encoding: .utf8) else {
        check(false, "the Windows client's kind 29 walks are at \(path)")
        return
    }
    var lines = 0, requests = 0, leaves = 0, windows = 0, focused = 0, hidden = 0, children = 0, childFocus = 0, all = 0, apps = 0
    var over = 0, overFocusLate = 0, overHidden = 0
    var badWalks: [Int: Int] = [:]
    var walks: [Int] = []
    for raw in text.split(separator: "\n") where !raw.isEmpty {
        lines += 1
        guard let line = try? JSONDecoder().decode(Kind29Line.self, from: Data(raw.utf8)) else {
            check(false, "a kind 29 line that does not decode: \(raw.prefix(200))")
            continue
        }
        if walks.last != line.walk { walks.append(line.walk) }
        let at = "walk \(line.walk) step \(line.step)"
        let ask = CoherenceAsk(line.request)
        var ok = true
        func fail(_ s: String) { ok = false; print("FAIL kind 29 \(at): \(s)\n     request \(encode(line.request))") }
        if line.meaning.leave == true {
            leaves += 1
            if ask != nil { fail("the device leaves Coherence and the host reads a Coherence ask") }
        } else if let ask, let streams = line.meaning.windows {
            requests += 1
            let ids = streams.map(\.id)
            var owners: [UInt32: UInt32] = [:]
            for p in line.owners ?? [] where p.count == 2 { owners[p[0]] = p[1] }
            windows += ids.count
            if ask.windows.sorted() != ids.sorted() || ask.windows.count != line.request.windows.count {
                fail("the host streams \(ask.windows), the device means \(ids)")
            }
            // The host streams at most six (its WindowList's `sideStreams`); beyond that, by the contract, the
            // window in front and a child of it first, then those shown, then those hidden, each as listed.
            let shown = CoherenceGates.Shown(ask: ask, windows: ask.windows, owners: owners)
            let picks = CoherenceGates.picks(shown)
            let plan = SidePlan.make(asks: [SidePlan.Ask(device: 0, windows: ask.windows, fps: ask.captureFPS, front: picks.front, hidden: picks.hidden)],
                                     main: nil, failedAt: [:], now: 0, limit: advertisedSideStreams) { _ in true }
            let meant = Dictionary(streams.map { ($0.id, $0) }, uniquingKeysWith: { a, _ in a })
            func place(_ id: UInt32) -> Int { meant[id].map { $0.front == true ? 0 : $0.hidden ? 2 : 1 } ?? 1 }
            let listed = SidePlan.unique(line.request.windows)
            let want = Array((0...2).flatMap { g in listed.filter { place($0) == g } }.prefix(advertisedSideStreams))
            if plan.order != want { fail("the side streams' plan streams \(plan.order) of \(ask.windows), the contract picks \(want)") }
            if ids.count > advertisedSideStreams {
                over += 1
                if let f = line.meaning.focus {
                    if !plan.order.contains(f) { fail("\(ids.count) windows: the focus \(f) does not stream") }
                    if !Array(listed.prefix(advertisedSideStreams)).contains(f) { overFocusLate += 1 }
                }
                if streams.contains(where: { $0.hidden }) { overHidden += 1 }
            }
            let gates = CoherenceGates.make([shown])
            if Set(gates.keys) != Set(ids) { fail("gates for \(gates.keys.sorted()), the device means \(ids)") }
            for s in streams {
                let want = CoherenceGate(cap: s.fps, hidden: s.hidden)
                if gates[s.id] != want { fail("window \(s.id): the host's gate \(String(describing: gates[s.id])), the device means \(want) (owner \(String(describing: owners[s.id])), focus \(String(describing: line.meaning.focus)))") }
                if s.hidden { hidden += 1 }
                if owners[s.id] != nil { children += 1 }
                if s.fps == line.meaning.focusFPS && line.meaning.fps != line.meaning.focusFPS { focused += 1 }
                if owners[s.id] != nil && s.id == line.meaning.focus { childFocus += 1 }
            }
            if ask.focus != line.meaning.focus { fail("the host's focus \(String(describing: ask.focus)), the device means \(String(describing: line.meaning.focus))") }
            if ask.fps != line.meaning.fps || ask.focusFPS != line.meaning.focusFPS {
                fail("rates \(ask.fps) and \(ask.focusFPS), the device means \(String(describing: line.meaning.fps)) and \(String(describing: line.meaning.focusFPS))")
            }
            if ask.captureFPS != max(ask.fps, ask.focusFPS) { fail("captures at \(ask.captureFPS)") }
            // On a Retina display (the knob 2): the device's pixels a point, at least 1, at most the display's.
            if let scale = line.meaning.scale {
                let knob = CoherenceScale.knob(2, deviceScales: ask.scale.map { [$0] } ?? [])
                let want = CGFloat(min(2, max(1, scale)))
                if abs(knob - want) > 1e-9 { fail("captures at \(knob) pixels a point, the device's scale is \(scale)") }
            }
            if line.request.screen != line.meaning.screen { fail("screen \(String(describing: line.request.screen)), the device means \(String(describing: line.meaning.screen))") }
            if line.meaning.all == true { all += 1 }
            if !(line.meaning.apps ?? []).isEmpty { apps += 1 }
            if ask.apps != Set(line.meaning.apps ?? []) || ask.all != (line.meaning.all ?? false) {
                fail("apps \(ask.apps.sorted()) all \(ask.all), the device means \(line.meaning.apps ?? []) all \(line.meaning.all ?? false)")
            }
        } else {
            fail("the host reads no Coherence ask (\(String(describing: ask)))")
        }
        if !ok { badWalks[line.walk, default: 0] += 1 }
    }
    for w in walks { check(badWalks[w] == nil, "kind 29 walk \(w): \(badWalks[w] ?? 0) requests the host reads otherwise than the device means them") }
    check(lines >= 100 && requests >= 80 && leaves >= 1 && hidden >= 20 && children >= 10 && focused >= 40 && childFocus >= 1 && all >= 5 && apps >= 20
          && over >= 20 && overFocusLate >= 3 && overHidden >= 5,
          "the kind 29 walks cover enough: \(lines) lines, \(requests) requests, \(leaves) ends, \(windows) windows, \(focused) at the focus rate, \(hidden) hidden, \(children) children (\(childFocus) the focus), \(all) with every app's windows, \(apps) with apps, \(over) over six (\(overFocusLate) with the focus listed after the sixth, \(overHidden) with hidden ones)")
    print("kind 29: \(lines) lines in \(walks.count) walks, \(requests) requests (\(windows) windows: \(focused) at the focus rate, \(hidden) hidden, "
          + "\(children) children, \(childFocus) of them the focus; \(all) with every app's windows, \(apps) with apps; \(over) over six, "
          + "\(overFocusLate) with the focus listed after the sixth, \(overHidden) with hidden ones), \(leaves) ends")
}

// MARK: - Main

var out: [String] = []
/// What the walks went through, so a change to the simulation that stops exercising something fails.
struct Coverage: CustomStringConvertible {
    var messages = 0, full = 0, stale = 0, takenBack = 0, forgotten = 0, sessions = 0, answers = 0, spilled = 0
    var focusMoves = 0, keptInCrop = 0, newOwner = 0, goneWithOwner = 0, forgotLimit = 0, shownFromKept = 0, askedAgain = 0
    static func += (a: inout Coverage, b: Coverage) {
        a.messages += b.messages; a.full += b.full; a.stale += b.stale; a.takenBack += b.takenBack; a.forgotten += b.forgotten
        a.sessions += b.sessions; a.answers += b.answers; a.spilled += b.spilled; a.focusMoves += b.focusMoves
        a.keptInCrop += b.keptInCrop; a.newOwner += b.newOwner; a.goneWithOwner += b.goneWithOwner; a.forgotLimit += b.forgotLimit
        a.shownFromKept += b.shownFromKept; a.askedAgain += b.askedAgain
    }
    var description: String {
        "\(messages) messages (\(full) full, \(answers) answers, \(focusMoves) moving the focus), \(stale) stale, \(takenBack) taken back, "
            + "\(forgotten) stopped by the device (\(shownFromKept) shown again before the host's next kind 45, \(askedAgain) asked for again after it), "
            + "\(sessions) new sessions, \(spilled) spilled children asked for, "
            + "\(keptInCrop) inCrop not known (kept), \(newOwner) children to another owner, \(goneWithOwner) gone with their owner, "
            + "\(forgotLimit) limits forgotten"
    }
}
var totals = Coverage()
let walkCount = 12, stepsPerWalk = 28
for n in 1...walkCount {
    var w = Walk(number: n)
    w.run(steps: stepsPerWalk)
    out += w.lines
    totals += w.stats
}
check(totals.messages >= 200 && totals.full >= 20 && totals.stale >= 10 && totals.takenBack >= 3 && totals.forgotten >= 5
      && totals.sessions >= 3 && totals.answers >= 20 && totals.spilled >= 20 && totals.focusMoves >= 10 && totals.keptInCrop >= 3
      && totals.newOwner >= 2 && totals.goneWithOwner >= 3 && totals.forgotLimit >= 2 && totals.shownFromKept >= 5 && totals.askedAgain >= 2,
      "the kind 45 walks cover enough: \(totals)")
print("kind 45: \(walkCount) walks of \(stepsPerWalk) steps, \(totals)")
if let path = environment["WALKS_OUT"] {
    try! (out.joined(separator: "\n") + "\n").write(toFile: path, atomically: true, encoding: .utf8)
}
if let path = environment["WALKS_KIND29"] { readKind29(path) }

print("\(checks) checks, \(failures) failed")
exit(failures == 0 ? 0 : 1)
