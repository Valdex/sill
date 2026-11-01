#if canImport(CoreGraphics)
import CoreGraphics   // on Linux Foundation has CGFloat, CGSize and CGRect: the check runs there too
#endif
import Foundation
import StreamProtocol

// Coherence's rules on the host (M1b; docs/coherence-protocol.md, docs/coherence-plan.md), apart from what
// they do on the Mac: what a device's kind 29 asks for once held to the contract's limits (CoherenceAsk),
// which frames of each window reach its encoder (CoherenceGates: the focus rate, the others' rate, hidden
// windows), the capture's pixel scale (CoherenceScale), which windows join the set by themselves
// (JoinRules), what kind 45 says of each window and its children (CoherenceEntries), what each kind 45
// carries after the last one and when one may go (WindowStateReporter, SendPacer), the sizes a window
// refused (SizeLimits), a kind 46's size and pace (PlaceSize, PlaceLimiter), which input needs its window
// key first and when to try again (CoherenceInput, ActivationGate), and kind 4's icon size (IconSizing).
// WindowRegistry reads the Mac, CoherenceController and StreamCoordinator act on these.
//
// Pure: Foundation (and SideStreamPolicy.swift's SidePlan, StreamProtocol's types), so it is checked on
// its own with swiftc, on Linux too (Tests/checks/coherence-policy).

// MARK: - Kind 29

/// A device's kind 29 with `coherence` true, held to the contract: each window once in the order asked;
/// `fps` 1…30 (15 when absent: #66's rule, SidePlan), `focusFPS` 1…120 (60 when absent); a focus only when
/// it is one of the windows; the hidden list without the focus; app IDs trimmed, none empty or absurdly
/// long; a scale only when it is a positive number. Nil for a kind 29 without `coherence` (#66's, or the
/// one that ends Coherence: no window).
struct CoherenceAsk: Equatable {
    var windows: [UInt32]
    var fps: Int
    var focus: UInt32?
    var focusFPS: Int
    var hidden: Set<UInt32>
    var apps: Set<String>
    var all: Bool
    /// The device's pixels per point, as it said (CoherenceScale holds it).
    var scale: Double?

    init?(_ r: SideStreamRequest) {
        guard r.coherence == true else { return nil }
        windows = SidePlan.unique(r.windows)
        fps = SidePlan.fps(asked: r.fps)
        focusFPS = Self.focusRate(asked: r.focusFPS)
        let listed = windows
        focus = r.focus.flatMap { listed.contains($0) ? $0 : nil }
        let front = focus
        hidden = Set((r.hidden ?? []).filter { $0 != front })
        apps = Set((r.apps ?? []).map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }.filter { !$0.isEmpty && $0.utf8.count <= 255 })
        all = r.all == true
        scale = r.scale.flatMap { $0.isFinite && $0 > 0 ? $0 : nil }
    }

    /// `focusFPS` held: 60 unless asked, 1 to 120.
    static func focusRate(asked: Int?) -> Int {
        min(max(asked ?? SideStreamRequest.defaultFocusFPS, 1), SideStreamRequest.maxFocusFPS)
    }

    /// The rate every capture of this device's windows runs at: the focus rate (or the others', if
    /// higher), so a change of focus only changes a gate and never starts a capture again
    /// (`SCStream.updateConfiguration` wedged on macOS 27, WindowCapture.swift).
    var captureFPS: Int { max(fps, focusFPS) }
}

// MARK: - Gates (focus, rates, hidden)

/// Which frames of a window reach its encoder: at most `cap` a second, and one every few seconds while
/// `hidden` (SideFrameGate's two knobs).
struct CoherenceGate: Equatable {
    var cap: Int
    var hidden: Bool
}

enum CoherenceGates {
    /// One device's view: its ask and the windows it is shown (its own list and the windows that joined),
    /// with each child's top-level owner from its last kind 45.
    struct Shown {
        var ask: CoherenceAsk
        var windows: [UInt32]
        var owners: [UInt32: UInt32] = [:]
    }

    /// Each window's gate over every device that shows it: the focus rate where a device has it, or the
    /// window that owns it, in front, else that device's `fps`, the most of any device; hidden only where
    /// every device that shows it hides it (a child with its owner), never a device's focus.
    static func make(_ shown: [Shown]) -> [UInt32: CoherenceGate] {
        var gates: [UInt32: CoherenceGate] = [:]
        for s in shown {
            for id in SidePlan.unique(s.windows) {
                let top = s.owners[id] ?? id
                let front = s.ask.focus == top
                let cap = front ? s.ask.focusFPS : s.ask.fps
                let hidden = !front && (s.ask.hidden.contains(id) || (top != id && s.ask.hidden.contains(top)))
                if let g = gates[id] {
                    gates[id] = CoherenceGate(cap: max(g.cap, cap), hidden: g.hidden && hidden)
                } else {
                    gates[id] = CoherenceGate(cap: cap, hidden: hidden)
                }
            }
        }
        return gates
    }

    /// The encoder's bitrate for a cap: the stream's bitrate per 60 fps in proportion, 2 Mbit/s at least
    /// (SideStreams' rule for a side stream's start).
    static func bitrate(per60: Int, cap: Int) -> Int { max(2_000_000, per60 * max(cap, 1) / 60) }
}

// MARK: - Capture scale

enum CoherenceScale {
    /// The least and most a device's scale counts for: Windows' 100 % to 400 %.
    static let least = 1.0, most = 4.0

    /// The knob a coherence device's windows capture at: the stream's knob (the Resolution of the pair
    /// that runs), no more than the largest pixels per point of the devices that said theirs, each held
    /// to 1…4. CaptureScale then holds it to each window's display. No device said: the knob.
    static func knob(_ knob: CGFloat, deviceScales: [Double]) -> CGFloat {
        let valid = deviceScales.filter { $0.isFinite && $0 > 0 }.map { min(max($0, least), most) }
        guard let largest = valid.max() else { return knob }
        return min(knob, CGFloat(largest))
    }
}

// MARK: - Windows that join by themselves

/// A window that may join a device's set by itself (kind 29's `apps` and `all`): a regular window
/// (CoherenceEntries.joinable) of `bundle`, first seen at `firstSeen` (seconds; minus infinity for one
/// already open when the host first looked).
struct JoinCandidate: Equatable {
    var id: UInt32
    var bundle: String
    var firstSeen: Double
}

/// Which windows join a device's set by themselves, and for how long a joined window the device has not
/// listed yet is kept (its kind 29 may have been written before it read the kind 45 that brought it).
/// - A window of an app in `apps` (or any app's, under `all`) that opens after the app was asked for, or at
///   most `lead` before (an app launched from the device opens its window before the kind 29 that adds it
///   arrives), joins once. A window open before that never joins.
/// - An app launched from the device (kind 7), or brought forward by its action: its next window joins;
///   none within `launchWait` (it was running with windows already), its front window does.
/// - A joined window stays in the set until the device lists it, or until a kind 29 that leaves it out
///   comes `grace` after it joined (the device closed it, or would not show it); it never joins again.
struct JoinRules: Equatable {
    static let lead: Double = 2
    static let grace: Double = 2
    static let launchWait: Double = 1.5
    static let launchSpan: Double = 10

    private var appSince: [String: Double] = [:]
    private var allSince: Double?
    private var launches: [String: Double] = [:]
    /// Joined, not yet listed by the device: when each joined.
    private(set) var pending: [UInt32: Double] = [:]
    /// Every window that joined (kind 45's `joined`), never again.
    private(set) var ever: Set<UInt32> = []

    /// A kind 29 from the device: apps and `all` timed from their first ask; joined windows it lists, or
    /// leaves out after their grace, are no longer pending.
    mutating func asked(_ ask: CoherenceAsk, now: Double) {
        for a in ask.apps where appSince[a] == nil { appSince[a] = now }
        for a in Array(appSince.keys) where !ask.apps.contains(a) { appSince[a] = nil }
        if ask.all { if allSince == nil { allSince = now } } else { allSince = nil }
        for (id, at) in pending where ask.windows.contains(id) || now - at >= Self.grace { pending[id] = nil }
    }

    /// An app the device launched or brought forward (the first time counts while it waits).
    mutating func launched(_ bundle: String, now: Double) {
        if let at = launches[bundle], now - at < Self.launchSpan { return }
        launches[bundle] = now
    }

    /// The windows that join now, from the regular windows on the Mac, the front window of each app
    /// (`fronts`) and those already in the set (`listed`).
    mutating func consider(_ candidates: [JoinCandidate], fronts: [String: UInt32], listed: Set<UInt32>, now: Double) -> [UInt32] {
        var joined: [UInt32] = []
        func join(_ id: UInt32) {
            guard !listed.contains(id), !ever.contains(id), !joined.contains(id) else { return }
            joined.append(id)
            pending[id] = now
            ever.insert(id)
        }
        for c in candidates where c.firstSeen.isFinite {
            if let since = allSince, c.firstSeen >= since - Self.lead { join(c.id); continue }
            if let since = appSince[c.bundle], c.firstSeen >= since - Self.lead { join(c.id); continue }
            if let at = launches[c.bundle], c.firstSeen >= at - Self.lead {
                join(c.id)
                launches[c.bundle] = nil
            }
        }
        for (bundle, at) in launches {
            if now - at >= Self.launchSpan { launches[bundle] = nil; continue }
            guard now - at >= Self.launchWait else { continue }
            if let front = fronts[bundle] { join(front) }
            launches[bundle] = nil
        }
        return joined
    }

    /// The windows the device is shown: its own list, then the joined windows it has not listed yet in
    /// the order they joined.
    func effective(_ ask: CoherenceAsk) -> [UInt32] {
        ask.windows + pending.filter { !ask.windows.contains($0.key) }.sorted { ($0.value, $0.key) < ($1.value, $1.key) }.map(\.key)
    }

    /// The person at the Mac took these back: no longer pending (and never joining again).
    mutating func forget(_ ids: Set<UInt32>) {
        for id in ids { pending[id] = nil; ever.insert(id) }
    }
}

// MARK: - What the Mac says of its windows

/// What the window server says of one window (CGWindowList): global points, top-left origin.
struct CGFacts: Equatable {
    var id: UInt32
    var pid: Int32
    var layer: Int
    var frame: CGRect
    var onScreen: Bool
    var alpha: Double = 1
    var name: String?
}

/// What Accessibility says of a top-level window, as last read. Rects are global points.
struct AXFacts: Equatable {
    var title: String?
    var subrole: String?
    var minimized = false
    var fullScreen = false
    var modal = false
    /// Its size is settable (kAXSize).
    var resizable: Bool?
    var close: CGRect?
    var minimize: CGRect?
    var zoom: CGRect?
    /// A toolbar among its children (a unified toolbar: its title bar is not plain).
    var toolbar = false
    /// Its sheets' and popovers' frames among its children.
    var sheets: [CGRect] = []
    var popovers: [CGRect] = []
    var frame: CGRect?
}

/// An app, by its process.
struct AppFacts: Equatable {
    var bundleID: String?
    var name: String?
}

/// One look at the Mac (WindowRegistry.read).
struct RegistryRead: Equatable {
    /// Every on-screen window, and each window of interest found off screen (minimized, another Space).
    var windows: [UInt32: CGFacts] = [:]
    /// The on-screen windows, front to back.
    var order: [UInt32] = []
    /// The windows of interest (the devices' sets), as Accessibility last read them.
    var ax: [UInt32: AXFacts] = [:]
    var apps: [Int32: AppFacts] = [:]
    /// The key window: the frontmost app's focused window.
    var keyWindow: UInt32?
    var secureInput = false
    /// When each on-screen regular window was first seen; minus infinity for those open at the first look.
    var firstSeen: [UInt32: Double] = [:]
}

/// What a window's side stream shows besides the window: its app's other windows inside `rect` (a display
/// crop: AppCrop, or Sill's desk with its app), the window alone, or not known (not streaming yet).
enum SidePicture: Equatable {
    case withApp(CGRect)
    case alone
    case unknown
}

/// Kind 45's windows, from one look at the Mac: each window a device is shown, then its children.
/// - A window: its app, title (Accessibility's, else the window server's), role (its AXSubrole), size in
///   points (the window server's frame, else Accessibility's), what it refused (SizeLimits), whether its
///   size is settable, focused (the key window), minimized, full screen, modal, the height of a plain title
///   bar (`titleBar`), its traffic lights from its top-left, `joined`. A window neither lists is gone.
/// - A child: another window of the same app on screen that is a sheet or popover by Accessibility
///   (matched by frame), or at a menu's, tooltip's, panel's or modal panel's level (`childRole`); its owner
///   is the shown window it overlaps most (its middle inside one first), else the owner of a child it
///   overlaps (a submenu), else, for a menu or a modal panel, the app's one shown window. `rect` is its
///   frame from the owner's top-left; `inCrop` whether the owner's picture shows it.
/// - `order`: the shown windows and children on screen, front to back; `focus`: the key window when shown.
enum CoherenceEntries {
    struct Result: Equatable {
        var entries: [WindowStateEntry] = []
        var order: [UInt32] = []
        var focus: UInt32?
        /// Each child's owner.
        var owners: [UInt32: UInt32] = [:]
    }

    /// Window levels (CGWindowLevel.h's values).
    static let floatingLevel = 3, modalPanelLevel = 8, utilityLevel = 19, statusLevel = 25, popUpMenuLevel = 101,
               helpLevel = 200, draggingLevel = 500
    /// How near a child may be to its owner and still overlap it (points): a popover's arrow, a menu's edge.
    static let reach: CGFloat = 4
    /// The least side of a child, in points.
    static let leastChild: CGFloat = 2

    static func make(read: RegistryRead, windows: [UInt32], joined: Set<UInt32>, limits: [UInt32: SizeLimits],
                     pictures: [UInt32: SidePicture]) -> Result {
        var r = Result()
        var tops: [(id: UInt32, pid: Int32, frame: CGRect)] = []
        for id in SidePlan.unique(windows) {
            let cg = read.windows[id], ax = read.ax[id]
            guard let frame = cg?.frame ?? ax?.frame, let pid = cg?.pid ?? pidOf(id, read) else { continue }
            tops.append((id, pid, frame))
            let app = read.apps[pid]
            var e = WindowStateEntry(id: id, app: app?.bundleID, appName: app?.name)
            e.title = ax?.title ?? cg?.name
            e.role = role(subrole: ax?.subrole)
            e.size = [rounded(frame.width), rounded(frame.height)]
            e.min = limits[id]?.min
            e.max = limits[id]?.max
            e.resizable = ax?.resizable
            e.focused = read.keyWindow == id
            e.minimized = ax.map(\.minimized) ?? (cg.map { !$0.onScreen } ?? false)
            e.fullScreen = ax?.fullScreen ?? false
            e.modal = ax?.modal ?? false
            e.titleBar = titleBar(close: ax?.close, frame: frame, toolbar: ax?.toolbar ?? false, fullScreen: ax?.fullScreen ?? false)
            if joined.contains(id) { e.joined = true }
            if let ax, ax.close != nil || ax.minimize != nil || ax.zoom != nil {
                e.controls = WindowControls(close: ax.close.map { relative($0, to: frame.origin) },
                                            minimize: ax.minimize.map { relative($0, to: frame.origin) },
                                            zoom: ax.zoom.map { relative($0, to: frame.origin) })
            }
            r.entries.append(e)
        }
        let shownTops = Set(tops.map(\.id))
        for child in children(read: read, tops: tops) {
            guard let cg = read.windows[child.id], let owner = tops.first(where: { $0.id == child.owner }) else { continue }
            var e = WindowStateEntry(id: child.id, app: read.apps[cg.pid]?.bundleID, appName: read.apps[cg.pid]?.name)
            e.role = child.role
            e.owner = child.owner
            e.rect = relative(cg.frame, to: owner.frame.origin)
            e.inCrop = inCrop(cg.frame, pictures[child.owner] ?? .unknown)
            r.entries.append(e)
            r.owners[child.id] = child.owner
        }
        let listed = Set(r.entries.map(\.id))
        r.order = read.order.filter { listed.contains($0) }
        r.focus = read.keyWindow.flatMap { shownTops.contains($0) ? $0 : nil }
        return r
    }

    /// The pid of a window known only to Accessibility (off every list): none.
    private static func pidOf(_ id: UInt32, _ read: RegistryRead) -> Int32? { nil }

    /// A top-level window's role from its AXSubrole: a dialog, a panel, else a window.
    static func role(subrole: String?) -> String {
        switch subrole {
        case "AXDialog", "AXSystemDialog": return WindowStateEntry.dialog
        case "AXFloatingWindow", "AXSystemFloatingWindow": return WindowStateEntry.panel
        default: return WindowStateEntry.window
        }
    }

    /// A window's role as a child: Accessibility's sheet or popover when its frame matched one (`matched`),
    /// else by its level: a menu, a tooltip (help and status levels), a dialog (a modal panel), a panel
    /// (floating and utility levels). Nil: not a child (a normal window, the menu bar, the Dock, a drag).
    static func childRole(layer: Int, matched: String?) -> String? {
        if let matched { return matched }
        switch layer {
        case popUpMenuLevel: return WindowStateEntry.menu
        case helpLevel..<draggingLevel, statusLevel: return WindowStateEntry.tooltip
        case modalPanelLevel: return WindowStateEntry.dialog
        case floatingLevel, utilityLevel: return WindowStateEntry.panel
        default: return nil
        }
    }

    /// The children of the shown windows `tops`, in the window server's order (front to back).
    static func children(read: RegistryRead, tops: [(id: UInt32, pid: Int32, frame: CGRect)]) -> [(id: UInt32, owner: UInt32, role: String)] {
        let topIDs = Set(tops.map(\.id))
        let pids = Set(tops.map(\.pid))
        var found: [(id: UInt32, owner: UInt32, role: String, frame: CGRect, pid: Int32)] = []
        // Back to front, so a submenu finds the menu it came from already owned.
        for id in read.order.reversed() {
            guard !topIDs.contains(id), let w = read.windows[id], pids.contains(w.pid), w.onScreen, w.alpha > 0,
                  w.frame.width >= leastChild, w.frame.height >= leastChild else { continue }
            let mine = tops.filter { $0.pid == w.pid }
            var matched: String?
            for t in mine {
                guard let ax = read.ax[t.id] else { continue }
                if ax.sheets.contains(where: { same($0, w.frame) }) { matched = WindowStateEntry.sheet }
                else if ax.popovers.contains(where: { same($0, w.frame) }) { matched = WindowStateEntry.popover }
                if matched != nil { break }
            }
            guard let role = childRole(layer: w.layer, matched: matched) else { continue }
            var owner: UInt32?
            if let inside = mine.first(where: { $0.frame.contains(CGPoint(x: w.frame.midX, y: w.frame.midY)) }) {
                owner = inside.id
            } else {
                let near = mine.map { ($0.id, area($0.frame.insetBy(dx: -reach, dy: -reach).intersection(w.frame))) }.filter { $0.1 > 0 }
                owner = near.max { $0.1 < $1.1 }?.0
            }
            if owner == nil {
                owner = found.first { $0.pid == w.pid && $0.frame.insetBy(dx: -reach, dy: -reach).intersects(w.frame) }?.owner
            }
            if owner == nil, mine.count == 1, role == WindowStateEntry.menu || role == WindowStateEntry.dialog { owner = mine[0].id }
            guard let owner else { continue }
            found.append((id, owner, role, w.frame, w.pid))
        }
        let rank = Dictionary(read.order.enumerated().map { ($1, $0) }, uniquingKeysWith: { a, _ in a })
        return found.sorted { (rank[$0.id] ?? 0) < (rank[$1.id] ?? 0) }.map { ($0.id, $0.owner, $0.role) }
    }

    /// The height of a plain title bar the device may crop: twice the close button's middle below the
    /// window's top (the traffic lights sit in the middle of the bar), 16 to 60 points; 0 for a window with
    /// a toolbar (unified: keep it), in full screen, or without a close button.
    static func titleBar(close: CGRect?, frame: CGRect, toolbar: Bool, fullScreen: Bool) -> Double {
        guard !toolbar, !fullScreen, let close else { return 0 }
        let h = (2 * (close.midY - frame.minY)).rounded()
        return h >= 16 && h <= 60 ? Double(h) : 0
    }

    /// A rect from `origin`, [x, y, width, height] in points, to a hundredth.
    static func relative(_ r: CGRect, to origin: CGPoint) -> [Double] {
        [rounded(r.minX - origin.x), rounded(r.minY - origin.y), rounded(r.width), rounded(r.height)]
    }

    /// Whether a child at `frame` shows in its owner's picture: inside the crop (a point of slack); never in
    /// a picture of the window alone; not known (nil, which a device takes as inside) before it streams.
    static func inCrop(_ frame: CGRect, _ picture: SidePicture) -> Bool? {
        switch picture {
        case .withApp(let rect): return rect.insetBy(dx: -1, dy: -1).contains(frame)
        case .alone: return false
        case .unknown: return nil
        }
    }

    /// A regular window that may join by itself: at the normal level, larger than 100 points each way (the
    /// catalog's rule), drawn, not Sill's own (its pid, Sill Viewer) nor a system helper.
    static func joinable(_ w: CGFacts, bundle: String?, ownPID: Int32) -> Bool {
        w.layer == 0 && w.frame.width > 100 && w.frame.height > 100 && w.alpha > 0 && w.pid != ownPID
            && !(w.name ?? "").lowercased().contains("layerprobe")
            && !(bundle ?? "").hasPrefix("me.saffer.sill.client")
    }

    static func rounded(_ v: CGFloat) -> Double { (Double(v) * 100).rounded() / 100 }
    private static func area(_ r: CGRect) -> CGFloat { r.isNull || r.isEmpty ? 0 : r.width * r.height }
    private static func same(_ a: CGRect, _ b: CGRect) -> Bool {
        abs(a.minX - b.minX) <= 2 && abs(a.minY - b.minY) <= 2 && abs(a.width - b.width) <= 2 && abs(a.height - b.height) <= 2
    }
}

// MARK: - Kind 45: what each message carries

/// One device's kind 45s: `full` on the first, after `gap()` (the host's look started again), and for a change
/// after `gapAfter` with no message; else the whole entry of each window that changed (the device
/// merges entry fields over the one it keeps, so a whole entry is always right), `removed` for windows no
/// longer listed, `order`, `focus` and `secureInput` when they changed. A kind 46's answer always goes,
/// with the window's entry. `gen` rises with every message.
struct WindowStateReporter: Equatable {
    static let gapAfter: Double = 10

    private(set) var gen = 0
    private var sent: [UInt32: WindowStateEntry] = [:]
    private var sentOrder: [UInt32] = []
    private var sentFocus: UInt32?
    private var sentSecure: Bool?
    private var lastAt: Double?
    private var owesFull = true

    /// The next message is a full one.
    mutating func gap() { owesFull = true }

    /// What was last sent of a window.
    func entry(_ id: UInt32) -> WindowStateEntry? { sent[id] }

    /// The message for the windows as they are now (nil: nothing changed and nothing is answered).
    mutating func message(_ now: CoherenceEntries.Result, secureInput: Bool, answering: (token: Int, window: UInt32)? = nil,
                          at time: Double) -> WindowStateMessage? {
        var m = WindowStateMessage(gen: gen + 1)
        let changed = now.entries.filter { sent[$0.id] != $0 || $0.id == answering?.window }
        let ids = Set(now.entries.map(\.id))
        let removed = sent.keys.filter { !ids.contains($0) }.sorted()
        if !changed.isEmpty { m.windows = changed }
        if !removed.isEmpty { m.removed = removed }
        if now.order != sentOrder { m.order = now.order }
        if now.focus != sentFocus, let f = now.focus { m.focus = f }
        if secureInput != sentSecure { m.secureInput = secureInput }
        let nothing = m.windows == nil && m.removed == nil && m.order == nil && m.focus == nil && m.secureInput == nil
        if nothing && answering == nil && !owesFull { return nil }
        // Something to say after a long silence (or owed): the whole set rather than what changed.
        if owesFull || lastAt.map({ time - $0 >= Self.gapAfter }) ?? true {
            m = WindowStateMessage(gen: gen + 1, full: true, windows: now.entries, order: now.order, focus: now.focus, secureInput: secureInput)
        }
        m.answering = answering?.token
        gen += 1
        sent = Dictionary(now.entries.map { ($0.id, $0) }, uniquingKeysWith: { a, _ in a })
        sentOrder = now.order
        sentFocus = now.focus
        sentSecure = secureInput
        lastAt = time
        owesFull = false
        return m
    }
}

/// Kind 45s to one device at most `perSecond` a second: a change within `interval` of the last message
/// waits for the end of it, and every change meanwhile goes in that one message.
struct SendPacer: Equatable {
    static let perSecond = 20
    static var interval: Double { 1 / Double(perSecond) }
    private var lastAt: Double?

    /// Seconds until a message may go (0: now).
    func wait(now: Double) -> Double {
        guard let lastAt else { return 0 }
        return max(0, lastAt + Self.interval - now)
    }

    mutating func sent(at now: Double) { lastAt = now }
}

// MARK: - Sizes a window refused

/// What a window took against what it was asked, each side on its own: a side larger than asked is its
/// least, smaller its most; a side it took as asked beyond what was learned forgets that (the app's limit
/// changed). Two points of slack (Accessibility and the window server round differently). Sent as kind 45's
/// `min` and `max` once either side is known, the other then 1 (`min`) or `unknown` (`max`).
struct SizeLimits: Equatable {
    static let tolerance = 2.0
    static let unknown = 65536.0
    var minW: Double?, minH: Double?, maxW: Double?, maxH: Double?

    mutating func learn(asked: CGSize, took: CGSize) {
        Self.axis(asked: Double(asked.width), took: Double(took.width), min: &minW, max: &maxW)
        Self.axis(asked: Double(asked.height), took: Double(took.height), min: &minH, max: &maxH)
    }

    private static func axis(asked a: Double, took t: Double, min lo: inout Double?, max hi: inout Double?) {
        guard a.isFinite, t.isFinite, a > 0, t > 0 else { return }
        if t > a + tolerance {
            lo = t
            if let h = hi, h < t { hi = nil }
        } else if t < a - tolerance {
            hi = t
            if let l = lo, l > t { lo = nil }
        } else {
            if let l = lo, a < l - tolerance { lo = nil }
            if let h = hi, a > h + tolerance { hi = nil }
        }
    }

    var min: [Double]? { minW == nil && minH == nil ? nil : [minW ?? 1, minH ?? 1] }
    var max: [Double]? { maxW == nil && maxH == nil ? nil : [maxW ?? Self.unknown, maxH ?? Self.unknown] }
}

// MARK: - Kind 46

enum PlaceSize {
    /// The most a side may be asked, in points, without a room to hold it to.
    static let most: CGFloat = 16384

    /// A kind 46's size in whole points, each side 1…the room Sill's desk keeps for the device's windows
    /// (its largest screen, kind 29's `screen`), else `most`; nil for one that is not two positive numbers.
    static func asked(_ size: [Double], room: CGSize?) -> CGSize? {
        guard size.count == 2, size.allSatisfy({ $0.isFinite && $0 > 0 }) else { return nil }
        let w = room.map { $0.width > 0 ? $0.width : most } ?? most, h = room.map { $0.height > 0 ? $0.height : most } ?? most
        return CGSize(width: min(max(CGFloat(size[0]).rounded(), 1), min(w, most)),
                      height: min(max(CGFloat(size[1]).rounded(), 1), min(h, most)))
    }
}

/// Kind 46 applied at most `PlaceWindow.maxPerSecond` times a second per window: one within the interval
/// of the last applied is held, the newest replacing the one held, until the interval is over.
struct PlaceLimiter: Equatable {
    static var interval: Double { 1 / Double(PlaceWindow.maxPerSecond) }
    enum Decision: Equatable { case now, held(until: Double) }

    private var lastAt: [UInt32: Double] = [:]
    private(set) var held: [UInt32: PlaceWindow] = [:]

    mutating func offer(_ p: PlaceWindow, now: Double) -> Decision {
        if let last = lastAt[p.id], now - last < Self.interval, now >= last {
            held[p.id] = p
            return .held(until: last + Self.interval)
        }
        lastAt[p.id] = now
        held[p.id] = nil
        return .now
    }

    /// The held requests whose interval is over, now applied.
    mutating func due(now: Double) -> [PlaceWindow] {
        var out: [PlaceWindow] = []
        for (id, p) in held.sorted(by: { $0.key < $1.key }) where now >= (lastAt[id] ?? 0) + Self.interval {
            out.append(p)
            held[id] = nil
            lastAt[id] = now
        }
        return out
    }

    mutating func forget(_ id: UInt32) { lastAt[id] = nil; held[id] = nil }

    /// When the soonest held request is due; nil with none held.
    var nextDue: Double? { held.keys.map { (lastAt[$0] ?? 0) + Self.interval }.min() }
}

// MARK: - Input aimed at a window

enum CoherenceInput {
    /// Whether the event needs its window key first: a button down, a key down, text. Moves, scrolls and
    /// ups never do (#66's rule: a Mac lets a window that is not in front take them).
    static func needsFocus(_ e: InputEvent) -> Bool {
        switch e {
        case .pointer(let action, _, _): return action.isButtonDown
        case .key(_, let down, _): return down
        case .text: return true
        case .scroll, .scrollGesture: return false
        }
    }
}

/// When a window that is not key is activated for an event aimed at it: at once, unless the last try was
/// for this same window less than `retry` ago (an app that does not come forward must not be asked at
/// every key). Per window: a click in another window always activates that one.
struct ActivationGate: Equatable {
    static let retry: Double = 2
    private var last: (window: UInt32, at: Double)?

    mutating func shouldActivate(window: UInt32, isKey: Bool, now: Double) -> Bool {
        guard !isKey else { return false }
        if let last, last.window == window, now - last.at < Self.retry, now >= last.at { return false }
        last = (window, now)
        return true
    }

    static func == (a: ActivationGate, b: ActivationGate) -> Bool { a.last?.window == b.last?.window && a.last?.at == b.last?.at }
}

// MARK: - Icons

enum IconSizing {
    /// Kind 4's size for a device that asks none (every device before), and the bounds of one asked.
    static let standard = 64, least = 16, largest = 512

    /// The size kind 4 is drawn at for a hello's `iconSize`: 16…512, else 64.
    static func drawn(asked: Int?) -> Int {
        guard let asked else { return standard }
        return min(max(asked, least), largest)
    }
}
