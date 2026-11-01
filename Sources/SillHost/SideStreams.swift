import CoreMedia
import Foundation
import Network
import ScreenCaptureKit
import StreamProtocol

/// The windows streamed besides the one picked (kinds 29–31; pull request #66's Mac client, ported for
/// this fork's coherence, docs/coherence-plan.md): each captured and encoded on its own at a lower rate
/// (SideStreamRequest: 15 fps unless asked otherwise, 30 at most), at the stream's capture scale held to
/// the window's display (CaptureScale), sharing the Mac's encoder engine with the main stream; the main
/// stream never waits for them, because the server drops a side frame rather than queue it behind
/// anything (StreamServer.sendSide, SideSend). A window the main stream shows is never also a side
/// stream, a window that changes size is started again at its new size, and one whose capture or encoder
/// fails is left alone for 10 s. At most `maxWindows` at once, the first asked first (SidePlan). Nothing is
/// captured for a device that asks for none: an iPhone or iPad never does. Main actor.
@MainActor
final class SideStreams {
    static let maxWindows = 6

    private let server: StreamServer
    private let catalog: WindowCatalog
    /// The stream's capture knob (the Resolution, held to each window's display by CaptureScale), the
    /// encoder to use and the bitrate per 60 fps, read at each start.
    var settings: () -> (scale: CGFloat, software: Bool, bitratePer60: Int, prioritizeSpeed: Bool) = { (2, false, 15_000_000, false) }
    /// The window the main stream shows, if it is one.
    var mainWindow: () -> UInt32? = { nil }
    /// Sill's desk, where a whole-app device's windows live (nil: none, a test host).
    var desk: () -> DeskStages? = { nil }
    /// WindowServer delivers no display stream (StreamCoordinator.displayStreamsDead): every window
    /// is captured alone, a full-screen one and one cropped with its app too.
    var displayStreamsDead: () -> Bool = { false }

    private struct Wanted {
        let connection: NWConnection
        var windows: [UInt32]
        var fps: Int
        var wholeApp: Bool
        var screen: CGSize?
    }
    private var wanted: [ObjectIdentifier: Wanted] = [:]
    /// The devices in the order they first asked: the first asked first (SidePlan).
    private var askOrder: [ObjectIdentifier] = []

    /// Some device asks for side streams (or for whole-app capture): the window list then names windows
    /// in other Spaces too, and the main stream announces its source before its first frame.
    var asked: Bool { !wanted.isEmpty }
    /// Whether this device asks (a drag out of a window is watched for it alone: it reads kind 36).
    func asks(_ c: NWConnection) -> Bool { wanted[ObjectIdentifier(c)] != nil }
    /// The largest screen of the devices that asked (Sill's desk makes each window's room that big).
    var largestScreen: CGSize? {
        wanted.values.compactMap(\.screen).max { $0.width * $0.height < $1.width * $1.height }
    }
    /// Some device asks for windows captured with what belongs to them (StreamCoordinator's main
    /// stream follows it too).
    var wholeApp: Bool { wanted.values.contains { $0.wholeApp } }
    /// The devices that asked for whole-app capture (a Mac client, a coherence client).
    var wholeAppConnections: [NWConnection] { askOrder.compactMap { wanted[$0] }.filter(\.wholeApp).map(\.connection) }
    /// The devices' wish for whole-app capture changed: the main stream starts again with it.
    var onWholeAppChanged: (() -> Void)?
    /// Whether some device asks at all changed (`asked`).
    var onAskedChanged: (() -> Void)?
    /// The person at this Mac stopped the sharing (StreamCoordinator.sharingStopped).
    var onStoppedByPerson: (() -> Void)?
    private var streams: [UInt32: SideWindowPipeline] = [:]
    private var failedAt: [UInt32: Double] = [:]
    private var reconciling = false
    private var again = false
    /// The encoders by window, for the server's keyframe requests on its network queue.
    private let encoders = EncoderBox()

    init(server: StreamServer, catalog: WindowCatalog) {
        self.server = server
        self.catalog = catalog
        let box = encoders
        server.onSideKeyframeNeeded = { id in box.get(id)?.requestKeyframe() }
    }

    func request(_ r: SideStreamRequest, from c: NWConnection) {
        let id = ObjectIdentifier(c)
        let windows = SidePlan.unique(r.windows)
        let before = (wholeApp, asked)
        // Kept with no windows when it asks for whole-app capture: the main stream follows that.
        if windows.isEmpty && r.wholeApp != true {
            wanted[id] = nil
            askOrder.removeAll { $0 == id }
        } else {
            let screen = r.screen.flatMap { $0.count == 2 && $0[0] > 0 && $0[1] > 0 && $0[0].isFinite && $0[1].isFinite
                ? CGSize(width: $0[0], height: $0[1]) : nil }
            if wanted[id] == nil { askOrder.append(id) }
            wanted[id] = Wanted(connection: c, windows: windows, fps: SidePlan.fps(asked: r.fps), wholeApp: r.wholeApp == true, screen: screen)
        }
        if wholeApp != before.0 { onWholeAppChanged?() }
        if asked != before.1 { onAskedChanged?() }
        Task { await reconcile() }
    }

    func clientLeft(_ c: NWConnection) {
        let before = (wholeApp, asked)
        guard wanted.removeValue(forKey: ObjectIdentifier(c)) != nil else { return }
        askOrder.removeAll { $0 == ObjectIdentifier(c) }
        if wholeApp != before.0 { onWholeAppChanged?() }
        if asked != before.1 { onAskedChanged?() }
        Task { await reconcile() }
    }

    /// The catalog changed (sizes, windows gone) or the main stream did.
    func changed() { Task { await reconcile() } }

    /// The windows streaming beside the main one now.
    var streaming: [UInt32] { Array(streams.keys) }

    /// A window's pipeline while it streams beside the main one: coherence's focus rate and hidden flag
    /// go through its gate (`SideWindowPipeline.setCap`, `.setHidden`).
    func pipeline(_ id: UInt32) -> SideWindowPipeline? { streams[id] }

    /// `ids` no longer asked for by any device until it asks again (the person at the Mac took them back).
    func forget(_ ids: Set<UInt32>) {
        for (key, entry) in wanted {
            var e = entry
            e.windows.removeAll { ids.contains($0) }
            wanted[key] = e
        }
        Task { await reconcile() }
    }

    /// Every running stream stopped, what the devices ask for kept: the desk's captures start again
    /// (`StreamCoordinator.refreshDesk`), and `changed()` starts them again.
    func stopStreams() async {
        for id in Array(streams.keys) { await stop(id) }
    }

    /// Every capture stopped, waited for: the exit path, before the desk's displays go.
    func stopCapturesNow(timeout: TimeInterval) {
        for stream in streams.values {
            stream.capture.stopNow(timeout: timeout)
            stream.pill?.stopNow(timeout: timeout)
        }
    }

    /// Brings the running streams to what is asked: one at a time, a request made meanwhile runs after.
    func reconcile() async {
        if reconciling { again = true; return }
        reconciling = true
        defer { reconciling = false }
        repeat {
            again = false
            await reconcileOnce()
        } while again
    }

    private func reconcileOnce() async {
        let main = mainWindow()
        let now = Date().timeIntervalSince1970
        let asks = askOrder.compactMap { key in wanted[key].map { SidePlan.Ask(device: key, windows: $0.windows, fps: $0.fps) } }
        let realDisplays = catalog.realDisplays
        // A Desktop window (DesktopWindow): one of this Mac's own displays, whole; else a listed window big
        // enough to capture.
        let plan = SidePlan.make(asks: asks, main: main, failedAt: failedAt, now: now, limit: Self.maxWindows) { id in
            if let d = DesktopWindow.display(of: id) { return realDisplays.contains { $0.displayID == d } }
            guard let w = self.catalog.window(id: id) else { return false }
            return w.frame.width >= 32 && w.frame.height >= 32
        }
        var target: [UInt32: SCWindow] = [:]
        var displays: [UInt32: SCDisplay] = [:]
        for id in plan.order {
            if let d = DesktopWindow.display(of: id) {
                if let display = realDisplays.first(where: { $0.displayID == d }) { displays[id] = display }
            } else if let w = catalog.window(id: id) {
                target[id] = w
            }
        }
        let connections = Dictionary(uniqueKeysWithValues: wanted.map { ($0.key, $0.value.connection) })
        server.setSideSubscribers(plan.subscribers.mapValues { $0.compactMap { connections[$0] } })
        // The desk keeps what a device shows (side streams and the main window) and sends the rest home.
        if let desk = desk() {
            // Every window a device has open stays there, streaming or not: one whose side stream
            // failed to start, is over the limit, or is briefly off the window list while it moves
            // is still shown on the device, and sending it home would move it under the person at
            // this Mac and make its display again when it streams.
            var keep = Set(wanted.values.flatMap(\.windows))
            if let main { keep.insert(main) }
            if !wholeApp { keep = [] }   // no device asks for it any more: every window goes home
            // A capture still running on a display as it is removed left WindowServer's display streams
            // delivering nothing, for every app, until it restarted (macOS 27.0.1): stopped first.
            for id in Array(streams.keys) where DesktopWindow.display(of: id) == nil && !keep.contains(id) { await stop(id) }
            desk.keep(keep)
        }
        for (id, stream) in streams where DesktopWindow.display(of: id) != nil {
            let size = displays[id].map { CGDisplayBounds($0.displayID).size }
            if size != stream.size || plan.fps[id] != stream.fps { await stop(id) }
        }
        for (id, d) in displays where streams[id] == nil {
            await start(display: d, id: id, fps: plan.fps[id] ?? SideStreamRequest.defaultFPS)
        }
        for (id, stream) in streams where DesktopWindow.display(of: id) == nil {
            let w = target[id]
            let moved = stream.cropFrame.map { $0.origin != w?.frame.origin } ?? false
            // Captured alone because its window was off screen when it started: tried again once the
            // window shows, at most every 30 s (alone for another reason, the desk's refusal, it stays).
            let retry = stream.offScreen && !stream.onDesk && stream.cropFrame == nil && w?.isOnScreen == true
                && Date().timeIntervalSince(stream.startedAt) > 30
            if w == nil || w!.frame.size != stream.size || plan.fps[id] != stream.fps || moved
                || stream.wholeApp != wholeApp || retry { await stop(id) }
        }
        for (id, w) in target where streams[id] == nil {
            await start(w, fps: plan.fps[id] ?? SideStreamRequest.defaultFPS)
        }
    }

    /// The encoder of a new side stream, wired to the server and to this type's failure handling.
    private func makeEncoder(id: UInt32, stream: SideWindowPipeline, width: Int, height: Int, fps: Int) throws -> HEVCEncoder {
        let s = settings()
        let bitrate = max(2_000_000, s.bitratePer60 * fps / 60)
        let enc = try HEVCEncoder(width: width, height: height, fps: fps, bitrate: bitrate, prioritizeSpeed: s.prioritizeSpeed,
                                  software: s.software, quiet: true)
        let server = self.server
        enc.onEncoded = { data, isKey, ps in server.sendSide(window: id, parameterSets: ps?.encoded(), frame: data, isKeyframe: isKey) }
        enc.onHung = { [weak self] in
            Task { @MainActor in
                guard let self, self.streams[id] === stream else { return }
                print("Side stream of window \(id): the encoder stopped answering; leaving it for 10 s.")
                self.failedAt[id] = Date().timeIntervalSince1970
                await self.stop(id)
            }
        }
        stream.capture.statsPrefix = "side.cap"
        // Each frame through the window's gate (its cap; coherence's hidden windows), then the encoder.
        let gate = stream.gate
        stream.capture.onFrame = { pb, pts in
            if gate.admits(at: CMTimeGetSeconds(pts)) { enc.encode(pb, pts: pts) } else { Stats.shared.bump("side.gated") }
        }
        stream.capture.onStopped = { [weak self] error in
            Task { @MainActor in
                guard let self, self.streams[id] === stream else { return }
                if StreamCoordinator.stoppedByPerson(error) { self.onStoppedByPerson?(); return }
                self.failedAt[id] = Date().timeIntervalSince1970
                await self.stop(id)
            }
        }
        stream.encoder = enc
        return enc
    }

    /// A Desktop window's side stream: the display whole, at the knob held to the display's own scale.
    private func start(display d: SCDisplay, id: UInt32, fps: Int) async {
        let s = settings()
        let size = CGDisplayBounds(d.displayID).size
        let filter = SCContentFilter(display: d, excludingWindows: [])
        let scale = CaptureScale.effective(knob: s.scale, displayScale: CaptureScale.displayScale(pointPixelScale: filter.pointPixelScale))
        let width = evenPixels(size.width * scale), height = evenPixels(size.height * scale)
        let stream = SideWindowPipeline(id: id, size: size, fps: fps)
        do {
            _ = try makeEncoder(id: id, stream: stream, width: width, height: height, fps: fps)
            streams[id] = stream
            encoders.set(id, stream.encoder)
            try await stream.capture.start(filter: filter, width: width, height: height, fps: fps, showsCursor: false)
            print("Side stream: display \(d.displayID) (a Desktop window) at \(width)×\(height), \(fps) fps")
        } catch {
            print("Side stream of display \(d.displayID) did not start: \(error)")
            failedAt[id] = Date().timeIntervalSince1970
            if streams[id] === stream { streams[id] = nil; encoders.set(id, nil) }
        }
    }

    private func start(_ w: SCWindow, fps: Int) async {
        let id = w.windowID
        let s = settings()
        let stream = SideWindowPipeline(id: id, size: w.frame.size, fps: fps)
        stream.wholeApp = wholeApp
        stream.offScreen = wholeApp && !w.isOnScreen
        do {
            var staged: (filter: SCContentFilter, sourceRect: CGRect, size: CGSize)?
            if wholeApp, let desk = desk() {
                do { staged = try await desk.prepare(w, size: w.frame.size, captureScale: s.scale, fps: nil, room: largestScreen) }
                catch { print("Sill's desk: window \(id) could not move there (\(error)); capturing it where it is.") }
            }
            // What to capture, and its size: on the desk, the window alone (its menus are for when it is
            // the main stream; window capture shows the system's share pill by itself), or its display in
            // full screen (the window an app reports then can be far wider than its Space: a browser's,
            // 3768 pt on a 1712 pt display, fitted into the frame a quarter of it, 2026-10-12); cropped
            // from its own display with its app (on screen only: cropped in another Space it showed what
            // lay there instead); else the window alone, at its display's scale (CaptureScale).
            let filter: SCContentFilter
            var sourceRect: CGRect?
            let width: Int, height: Int
            if let staged {
                stream.onDesk = true
                let scale = min(s.scale, desk()?.scale(for: id) ?? s.scale)
                width = evenPixels(staged.size.width * scale); height = evenPixels(staged.size.height * scale)
                if desk()?.isFullScreen(id) == true, !displayStreamsDead() {
                    filter = staged.filter
                    sourceRect = staged.sourceRect
                } else {
                    filter = SCContentFilter(desktopIndependentWindow: w)
                }
            } else if wholeApp, !displayStreamsDead(), w.isOnScreen, let crop = await AppCrop.crop(for: w) {
                stream.cropFrame = crop.frame
                let pill = SharePill()
                await pill.start(w)
                stream.pill = pill
                filter = crop.filter
                sourceRect = crop.sourceRect
                let scale = CaptureScale.effective(knob: s.scale, displayScale: CaptureScale.displayScale(pointPixelScale: filter.pointPixelScale))
                width = evenPixels(crop.sourceRect.width * scale); height = evenPixels(crop.sourceRect.height * scale)
            } else {
                filter = SCContentFilter(desktopIndependentWindow: w)
                (width, height) = CaptureScale.windowSize(w.frame.size, knob: s.scale,
                                                          displayScale: CaptureScale.displayScale(pointPixelScale: filter.pointPixelScale))
            }
            _ = try makeEncoder(id: id, stream: stream, width: width, height: height, fps: fps)
            streams[id] = stream
            encoders.set(id, stream.encoder)
            try await stream.capture.start(filter: filter, width: width, height: height, fps: fps, showsCursor: false, sourceRect: sourceRect)
            let how = stream.onDesk ? ", on Sill's desk" : stream.cropFrame != nil ? ", with its app" : ""
            print("Side stream: \(w.owningApplication?.applicationName ?? "?") — \(w.title ?? "") (window \(id)) at \(width)×\(height), \(fps) fps\(how)")
        } catch {
            print("Side stream of window \(id) did not start: \(error)")
            failedAt[id] = Date().timeIntervalSince1970
            await stream.pill?.stop()
            if streams[id] === stream { streams[id] = nil; encoders.set(id, nil) }
        }
    }

    private func stop(_ id: UInt32) async {
        guard let stream = streams.removeValue(forKey: id) else { return }
        encoders.set(id, nil)
        await stream.capture.stop()
        await stream.pill?.stop()
        stream.capture.onFrame = nil
        stream.encoder = nil   // its deinit drains and invalidates the session
    }

    /// Lock-protected, for the network queue.
    private final class EncoderBox: @unchecked Sendable {
        private let lock = NSLock()
        private var map: [UInt32: HEVCEncoder] = [:]
        func get(_ id: UInt32) -> HEVCEncoder? { lock.lock(); defer { lock.unlock() }; return map[id] }
        func set(_ id: UInt32, _ e: HEVCEncoder?) { lock.lock(); map[id] = e; lock.unlock() }
    }
}

/// One window streamed beside the main one: its capture, its encoder, how it is captured, and its frame
/// gate (SideFrameGate: the rate cap and the hidden flag), the seam for coherence's focus rate and hidden
/// windows, which change what reaches the encoder without restarting the capture. Main actor, but for
/// the gate, which the capture's queue reads.
@MainActor
final class SideWindowPipeline {
    let id: UInt32
    /// The window's size in points when it started: a new size starts it again.
    let size: CGSize
    /// The rate it was started at (its capture's).
    let fps: Int
    /// The window's frame when it was cropped from its display (wholeApp), to tell when it moved.
    var cropFrame: CGRect?
    /// The share pill macOS draws while the window itself is captured (SharePill).
    var pill: SharePill?
    /// On Sill's desk (DeskStages).
    var onDesk = false
    /// Whether whole-app capture was asked for when it started (a window neither on the desk nor
    /// cropped, off screen, is captured alone though it was).
    var wholeApp = false
    /// Its window was off screen when it started (captured alone for that): tried again once it shows.
    var offScreen = false
    let startedAt = Date()
    let capture = WindowCapture()
    var encoder: HEVCEncoder?
    /// Which captured frames reach the encoder.
    nonisolated let gate: SideFrameGateBox

    init(id: UInt32, size: CGSize, fps: Int) {
        self.id = id; self.size = size; self.fps = fps
        gate = SideFrameGateBox(SideFrameGate(cap: fps))
    }

    /// At most `fps` frames a second reach the encoder from now on (at most the capture's own rate).
    func setCap(_ fps: Int) { gate.update { $0.cap = fps; $0.reset() } }
    /// A window hidden on the device: one frame every few seconds, its capture kept.
    func setHidden(_ hidden: Bool) { gate.update { if $0.hidden != hidden { $0.hidden = hidden; $0.reset() } } }
}

/// A SideFrameGate behind a lock: read on the capture's queue, changed on the main actor.
final class SideFrameGateBox: @unchecked Sendable {
    private let lock = NSLock()
    private var gate: SideFrameGate
    init(_ gate: SideFrameGate) { self.gate = gate }
    func admits(at t: Double) -> Bool { lock.lock(); defer { lock.unlock() }; return gate.admits(at: t) }
    func update(_ change: (inout SideFrameGate) -> Void) { lock.lock(); change(&gate); lock.unlock() }
}
