import Foundation
import StreamProtocol

// The rules of the side streams and Sill's desk (pull request #66's, ported; docs/coherence-plan.md),
// apart from what they do on the Mac: which windows stream beside the main one, at what rate and to
// whom (SidePlan); which of a window's captured frames reach its encoder (SideFrameGate, the seam for
// coherence's focus rate and hidden windows); which side frames go to a client and when a keyframe is
// asked for (SideSend); the desk's displays' sizes, scale, memory and identities (DeskGeometry); and a
// dropped file's name on the Mac (DropFileName). SideStreams, StreamServer, DeskStages, VirtualStage
// and FileDrops act on them.
//
// Pure: Foundation (and CaptureScale.swift's VirtualDisplaySize), so it is checked on its own with
// swiftc, on Linux too (Tests/checks/side-streams).

/// Which windows stream beside the main one, at what rate, and to whom: the devices' requests (kind 29)
/// in the order they first asked, each window once, the main stream's window never, a window whose
/// capture or encoder failed lately skipped for `coolDown`, only a window still there (`eligible`), at
/// most `limit`. When more are asked than that (a Coherence device lists every window it shows), a
/// window in front for some device goes first (its focus, and a child of the window it has in front),
/// then one some device shows, then one every device that lists it hides (kind 29's `hidden`), each
/// group first asked first; as the focus moves, the picks follow it (docs/coherence-protocol.md, kind
/// 29). A window's rate is the most any device asked for it.
enum SidePlan {
    /// A window's place in the queue for the limit.
    enum Pick: Int, Comparable {
        case front, shown, hidden
        static func < (a: Pick, b: Pick) -> Bool { a.rawValue < b.rawValue }
    }

    struct Ask<Device: Hashable>: Equatable {
        var device: Device
        var windows: [UInt32]
        var fps: Int
        /// The windows this device has in front and those it hides (a Coherence device's, from
        /// CoherenceGates.picks); none for #66's requests, whose windows are all `shown`.
        var front: Set<UInt32> = []
        var hidden: Set<UInt32> = []

        func pick(_ id: UInt32) -> Pick { front.contains(id) ? .front : hidden.contains(id) ? .hidden : .shown }
    }

    struct Plan<Device: Hashable>: Equatable {
        /// The windows that stream, in the order picked (in front, shown, hidden; each first asked first).
        var order: [UInt32] = []
        /// Each one's rate.
        var fps: [UInt32: Int] = [:]
        /// The devices each goes to, in the order they asked.
        var subscribers: [UInt32: [Device]] = [:]
    }

    /// Seconds a failed window is left alone.
    static let coolDown: Double = 10

    /// A request's rate, held: `SideStreamRequest.defaultFPS` when none, 1 to `SideStreamRequest.maxFPS`.
    static func fps(asked: Int?) -> Int {
        min(max(asked ?? SideStreamRequest.defaultFPS, 1), SideStreamRequest.maxFPS)
    }

    /// Each window once, in the order first listed.
    static func unique(_ ids: [UInt32]) -> [UInt32] {
        var seen = Set<UInt32>()
        return ids.filter { seen.insert($0).inserted }
    }

    static func make<Device: Hashable>(asks: [Ask<Device>], main: UInt32?, failedAt: [UInt32: Double], now: Double,
                                       limit: Int, eligible: (UInt32) -> Bool) -> Plan<Device> {
        var asked: [UInt32] = []
        var fps: [UInt32: Int] = [:]
        var subscribers: [UInt32: [Device]] = [:]
        var pick: [UInt32: Pick] = [:]
        for ask in asks {
            for id in unique(ask.windows) where id != main {
                if fps[id] == nil { asked.append(id) }
                fps[id] = max(fps[id] ?? 0, ask.fps)
                // The best place any device gives it: in front for one device is in front.
                pick[id] = min(pick[id] ?? .hidden, ask.pick(id))
                if subscribers[id]?.contains(ask.device) != true { subscribers[id, default: []].append(ask.device) }
            }
        }
        // In front, then shown, then hidden; within each, first asked first.
        let queue = asked.enumerated().sorted { (pick[$0.element]!, $0.offset) < (pick[$1.element]!, $1.offset) }.map(\.element)
        var plan = Plan<Device>()
        for id in queue where plan.order.count < limit {
            if let failed = failedAt[id], now - failed < coolDown { continue }
            guard eligible(id) else { continue }
            plan.order.append(id)
            plan.fps[id] = fps[id]
            plan.subscribers[id] = subscribers[id]
        }
        return plan
    }
}

/// Which of a side window's captured frames reach its encoder: at most `cap` a second, and while the
/// window is `hidden` (minimized or covered on the device: coherence's kind 29 `hidden`) one every
/// `hiddenInterval`. The capture runs at its own rate meanwhile, so a change of either never restarts
/// the stream: the gate only stops dropping frames (docs/coherence-protocol.md, kind 29's `focus`). A frame
/// may come a quarter of an interval early (the capture's jitter); the frames let through never run
/// ahead of the cap over time, and after a pause the next frame goes at once.
struct SideFrameGate: Equatable {
    var cap: Int
    var hidden = false
    /// Seconds between a hidden window's frames.
    static let hiddenInterval: Double = 3
    /// When the next frame is due; nil before the first.
    private var next: Double?

    init(cap: Int, hidden: Bool = false) { self.cap = cap; self.hidden = hidden }

    /// Seconds between frames now.
    var interval: Double { hidden ? Self.hiddenInterval : 1 / Double(max(cap, 1)) }

    /// Whether the frame captured at `t` (seconds) goes to the encoder.
    mutating func admits(at t: Double) -> Bool {
        let every = interval
        guard let due = next else { next = t + every; return true }
        guard t >= due - every / 4 else { return false }
        // On schedule from the last due time, so early frames never add up; restarted after a pause.
        next = t - due > every ? t + every : due + every
        return true
    }

    /// The cap or `hidden` changed: the next frame goes at once if the new interval allows it.
    mutating func reset() { next = nil }
}

/// One side stream's frame to one client (StreamServer.sendSide, kinds 30 and 31). A client new to the
/// window, or one that lost a delta, waits for the window's next keyframe, which goes with the window's
/// parameter sets in front; a delta goes only while what waits in the client's connection is within the
/// side budget (the main stream's frames come first: a side frame never queues behind them), else it
/// is dropped and the client waits; a keyframe for a client that is not waiting goes whatever waits.
enum SideSend {
    enum Action: Equatable {
        /// Send the frame, the window's parameter sets first when `parameterSets`.
        case send(parameterSets: Bool)
        /// Not this frame; the window's encoder should make a keyframe.
        case skip
    }

    /// What may wait in a client's connection before a side delta is dropped: at home, away.
    static let budgetHome = 192 * 1024
    static let budgetRemote = 64 * 1024
    /// Seconds between a window's keyframe requests on behalf of lossy clients.
    static let keyframeEvery: Double = 1

    /// The action for one client, and whether it waits for a keyframe after it. `haveSets`: the window's
    /// parameter sets are known (they come with its first keyframe); `newSets`: this frame brings sets.
    static func decide(waiting: Bool, isKeyframe: Bool, haveSets: Bool, newSets: Bool, pending: Int, budget: Int)
        -> (action: Action, waiting: Bool) {
        if waiting {
            guard isKeyframe, pending <= budget, haveSets else { return (.skip, true) }
            return (.send(parameterSets: true), false)
        }
        if !isKeyframe, pending > budget { return (.skip, true) }
        return (.send(parameterSets: newSets), false)
    }

    /// Whether a keyframe may be asked for the window now (once every `keyframeEvery` at most).
    static func mayAskKeyframe(lastAsked: Double?, now: Double) -> Bool {
        lastAsked.map { now - $0 >= keyframeEvery } ?? true
    }
}

/// Sill's desk's displays (DeskStages, VirtualStage): each window's display just larger than the window
/// (and the device's largest screen), Retina unless the window is wider or taller than a 2× display can
/// be, a share of the Mac's memory for all of them, and the identities they take.
enum DeskGeometry {
    /// The largest 2× display macOS 27.0.1 made with a mode ScreenCaptureKit listed (3840 and 4096 points
    /// wide came up with no mode at all; 3792×2160, 3808×1512 and 2560×2400 did).
    static let largest = CGSize(width: 3792, height: 2160)
    /// What the desk's displays may take of this Mac's memory in all (each holds a framebuffer).
    static let memoryShare = 0.25

    /// A display just larger than the window: room for its menu bar, rounded up to 16 points, at most
    /// `largest`, every side within VirtualDisplaySize's (no side under 800 points, which macOS keeps at 2×).
    static func envelope(for size: CGSize) -> CGSize {
        func up(_ v: CGFloat) -> CGFloat { (v / 16).rounded(.up) * 16 }
        let want = CGSize(width: up(size.width + 16), height: up(size.height + 48))
        return VirtualDisplaySize.clamp(CGSize(width: min(want.width, largest.width), height: min(want.height, largest.height)))
    }

    /// The display's scale for a window `window` big: 1 for one wider or taller than `largest` (an
    /// ultrawide's, whose 1× pixels are its points), 2 otherwise; a display at 1× goes back to 2× only
    /// once the window is 5 % within it, so a window resized about the line does not flap.
    static func scale(window: CGSize, current: Int?) -> Int {
        if window.width > largest.width || window.height > largest.height { return 1 }
        let fits2x = window.width <= largest.width * 0.95 && window.height <= largest.height * 0.95
        return current == 1 && !fits2x ? 1 : 2
    }

    /// A display's framebuffer in bytes: its points at its scale, 4 bytes a pixel.
    static func framebuffer(widthPt: Int, heightPt: Int, scale: Int) -> Double {
        Double(widthPt * scale) * Double(heightPt * scale) * 4
    }

    /// Whether another display fits: the framebuffers already made, and one more as large as the largest
    /// of them (at least a `largest` 2× display), within `memoryShare` of `memory`.
    static func roomForAnother(framebuffers: [Double], memory: Double) -> Bool {
        let total = framebuffers.reduce(0, +)
        let another = max(framebuffers.max() ?? 0, Double(largest.width * largest.height) * 4 * 4)
        return total + another <= memory * memoryShare
    }

    /// The lowest identity slot not in use (each slot one product ID and serial, so macOS records as many
    /// identities as the most windows the desk held at once, never one per display made).
    static func freeSlot(used: Set<Int>) -> Int {
        var s = 0
        while used.contains(s) { s += 1 }
        return s
    }

    /// A slot's product ID: 0x100 and up, beside the iPad's stage's 0x0001.
    static func productID(slot: Int) -> UInt32 { 0x100 + UInt32(clamping: slot) }
}

/// A file's name as a device sent it (kind 34), as it lands on the Mac (`~/Downloads/From ‹device›/`):
/// no path, `/` and `:` made `-`, control characters dropped, a name that is empty or starts with a dot
/// (hidden, `.` or `..`) made visible ("file…"), and at most `maxBytes` of UTF-8, cut on a character.
enum DropFileName {
    static let maxBytes = 200

    static func safe(_ name: String) -> String {
        var cleaned = String(String.UnicodeScalarView(name.unicodeScalars.filter { $0.value >= 0x20 && $0.value != 0x7F }))
        cleaned = cleaned.replacingOccurrences(of: "/", with: "-").replacingOccurrences(of: ":", with: "-")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        if cleaned.isEmpty || cleaned.hasPrefix(".") { cleaned = "file" + cleaned }
        var out = ""
        for c in cleaned {
            guard out.utf8.count + String(c).utf8.count <= maxBytes else { break }
            out.append(c)
        }
        return out
    }
}
