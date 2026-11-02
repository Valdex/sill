import AppKit
import ApplicationServices
import Foundation
import Network
import ScreenCaptureKit
import StreamProtocol

/// Coherence on the host (M1b; docs/coherence-protocol.md, docs/coherence-plan.md): a device that shows
/// the Mac's windows as windows of its own, each streamed beside the others (SideStreams, Sill's desk),
/// none picked with kind 6.
/// - Kind 29 with `coherence` (`request`): the device's set, held to the contract (CoherenceAsk), with the
///   windows that joined by themselves (JoinRules: `apps`, `all`, an app it launched), is what SideStreams
///   streams for it, every capture at its focus rate; each window's gate (CoherenceGates) holds it to its
///   own rate, or hides it, so a change of focus or of the hidden list starts no capture again
///   (SideStreams.applyGates). `scale` sets the capture's knob (CoherenceScale). A kind 29 without
///   `coherence`, or the device leaving, ends it: the desk sends every window home (SideStreams), and a
///   window sized where it was goes back to its own frame.
/// - Kind 45 (`send`): WindowRegistry's look at the Mac, made into each device's windows and their
///   children (CoherenceEntries), full on the first and after a gap, else what changed (WindowStateReporter),
///   20 a second at most per device (SendPacer).
/// - Kind 46 (`place`): an Accessibility size write, read back (an app that applies it late waited for),
///   what it refused learned as `min` and `max` (SizeLimits), answered with a kind 45; 20 a second at most
///   per window (PlaceLimiter), one at a time per window.
/// - Kind 44 `{window}`, kind 4 at the hello's `iconSize`, and what StreamCoordinator's input, kind 15 and
///   take-back read: which windows a device is shown, a child's owner, a window's rect, pid, element.
/// Main actor.
@MainActor
final class CoherenceController {
    let registry = WindowRegistry()
    private let server: StreamServer
    private let catalog: WindowCatalog
    private let desk: DeskStages
    private let sizer: WindowSizer
    /// The coordinator's side streams (made at a device's first kind 29).
    var sideStreams: () -> SideStreams? = { nil }
    /// A test host writes no window's size (`--synthetic`, as InputInjector.dryRun posts nothing).
    var synthetic = false
    /// A device's name, for the log.
    var deviceName: (NWConnection) -> String = { "\($0.endpoint)" }
    /// Windows joined a set (the catalog looks again, so they stream at once), and a window was sized (the
    /// catalog looks again a moment after the last size, so its stream restarts at it).
    var onJoined: (() -> Void)?
    var onResized: (() -> Void)?
    /// Coherence started or ended (the cursor's shape, the link's ticks and the sound follow it).
    var onActiveChanged: (() -> Void)?
    /// The Mac's key window changed (the menus follow it in Coherence).
    var onKeyWindowChanged: (() -> Void)?
    private var lastKeyWindow: UInt32?

    private struct Device {
        let connection: NWConnection
        var ask: CoherenceAsk
        /// Its kind 29 as it came, for the request SideStreams gets (`effectiveRequest`).
        var raw: SideStreamRequest
        var joins = JoinRules()
        var reporter = WindowStateReporter()
        var pacer = SendPacer()
        var scheduled = false
        /// Kind 46 answers owed: token and window.
        var owed: [(token: Int, window: UInt32)] = []
        /// Each child's owner, from its last kind 45.
        var owners: [UInt32: UInt32] = [:]
        /// The device's largest screen (kind 29's `screen`): Sill's desk's room, the most a size is asked.
        var screen: CGSize?
        /// Windows the person at the Mac took back, and when: left out of what it asks for a moment (a kind
        /// 29 it wrote before it read kind 41).
        var refused: [UInt32: Double] = [:]
    }
    private var devices: [ObjectIdentifier: Device] = [:]
    /// The devices in the order they started.
    private var order: [ObjectIdentifier] = []
    private var gates: [UInt32: CoherenceGate] = [:]
    private var limits: [UInt32: SizeLimits] = [:]
    private var placer = PlaceLimiter()
    private var placeFor: [UInt32: ObjectIdentifier] = [:]
    private var placing: Set<UInt32> = []
    private var placeQueued: [UInt32: (PlaceWindow, ObjectIdentifier)] = [:]
    private var placeFlush: Task<Void, Never>?
    private var resizeRefresh: Task<Void, Never>?
    /// Windows sized where they are (not on the desk), with their frame before: back to it when no device
    /// shows them any more.
    private var resizedInPlace: [UInt32: WindowSizer.Placement] = [:]
    private var activations = ActivationGate()
    /// Bumped by each activation's hold: only the newest releases the input held (StreamCoordinator).
    private(set) var holdToken = 0
    private var childWindows: [UInt32: SCWindow] = [:]
    private var resolving = false
    private var resolvedAt: (ids: Set<UInt32>, at: CFAbsoluteTime)?
    private var keyframeAskedAt: [ObjectIdentifier: [UInt32: Double]] = [:]
    private var iconSizes: [ObjectIdentifier: (connection: NWConnection, size: Int)] = [:]
    private var iconsSent: [ObjectIdentifier: Set<String>] = [:]
    /// Apps launched (kind 7) by a device in Coherence, and when: their first window joins its set, never
    /// the main stream (StreamCoordinator's `pendingLaunch`).
    private var launchedApps: [String: Double] = [:]
    private var rectCache: [UInt32: (rect: CGRect, at: CFAbsoluteTime)] = [:]

    /// How long a window taken back is left out of a device's kind 29s.
    static let refusedFor: Double = 3
    /// How long after a window's last size its catalog entry is looked at again.
    static let resizeSettle: Double = 0.3

    init(server: StreamServer, catalog: WindowCatalog, desk: DeskStages, sizer: WindowSizer) {
        self.server = server
        self.catalog = catalog
        self.desk = desk
        self.sizer = sizer
        registry.interest = { [unowned self] in self.interest() }
        registry.onChange = { [unowned self] in self.registryChanged() }
        // Each device's kind 45 only when its windows changed (WindowStateReporter): cheap at every look.
        registry.onLooked = { [unowned self] in for key in Array(self.devices.keys) { self.schedule(key) } }
    }

    var isActive: Bool { !devices.isEmpty }
    /// Whether this device is in Coherence.
    func serves(_ c: NWConnection) -> Bool { devices[ObjectIdentifier(c)] != nil }

    /// The windows a device is shown: its own list and those that joined, less those taken back.
    private func effective(_ d: Device) -> [UInt32] { d.joins.effective(d.ask).filter { d.refused[$0] == nil } }

    private func interest() -> Set<UInt32> { Set(devices.values.flatMap { effective($0) }) }

    // MARK: Kind 29

    /// A device's kind 29: what SideStreams should stream for it. With `coherence`: its set and the windows
    /// that joined, at its focus rate; without, as it came (and Coherence ends for it).
    func request(_ r: SideStreamRequest, from c: NWConnection) -> SideStreamRequest {
        let key = ObjectIdentifier(c)
        guard var ask = CoherenceAsk(r) else {
            if devices[key] != nil {
                print("Coherence: \(deviceName(c)) left Coherence; its windows go home.")
                leave(key)
            }
            return r
        }
        let now = CFAbsoluteTimeGetCurrent()
        let wasActive = isActive
        var d = devices[key] ?? Device(connection: c, ask: ask, raw: r)
        if devices[key] == nil {
            order.append(key)
            print("Coherence: \(deviceName(c)) shows \(ask.windows.count) window\(ask.windows.count == 1 ? "" : "s") as its own (kind 29)"
                  + (ask.all ? ", every app's new windows too" : ask.apps.isEmpty ? "" : ", and new windows of \(ask.apps.sorted().joined(separator: ", "))"))
        }
        d.refused = d.refused.filter { now - $0.value < Self.refusedFor }
        let refused = d.refused
        ask.windows.removeAll { refused[$0] != nil }
        d.joins.asked(ask, now: now)
        d.ask = ask
        d.raw = r
        d.screen = r.screen.flatMap { $0.count == 2 && $0[0] > 0 && $0[1] > 0 && $0[0].isFinite && $0[1].isFinite
            ? CGSize(width: $0[0], height: $0[1]) : nil }
        devices[key] = d
        if !wasActive {
            registry.start()
            onActiveChanged?()
        } else {
            registry.lookSoon()
        }
        refreshGates()
        schedule(key)
        resolveChildren()
        return effectiveRequest(d)
    }

    private func effectiveRequest(_ d: Device) -> SideStreamRequest {
        var r = d.raw
        r.windows = effective(d)
        r.fps = d.ask.captureFPS
        return r
    }

    /// The device left, or ended Coherence.
    func clientLeft(_ c: NWConnection) {
        let key = ObjectIdentifier(c)
        iconSizes[key] = nil
        iconsSent[key] = nil
        keyframeAskedAt[key] = nil
        leave(key)
    }

    private func leave(_ key: ObjectIdentifier) {
        guard devices.removeValue(forKey: key) != nil else { return }
        order.removeAll { $0 == key }
        refreshGates()
        restoreResized()
        guard devices.isEmpty else { return }
        registry.stop()
        childWindows = [:]
        rectCache = [:]
        placeQueued = [:]
        launchedApps = [:]
        lastKeyWindow = nil
        onActiveChanged?()
    }

    /// The person at the Mac took these windows back (kind 41 went to the devices): out of every set, never
    /// joining again, left out of kind 29s written before the devices read it.
    func takenBack(_ ids: Set<UInt32>) {
        let now = CFAbsoluteTimeGetCurrent()
        for key in Array(devices.keys) {
            devices[key]?.joins.forget(ids)
            devices[key]?.ask.windows.removeAll { ids.contains($0) }
            for id in ids { devices[key]?.refused[id] = now }
            schedule(key)
        }
        refreshGates()
        restoreResized()
    }

    /// Quit: every window a device sized where it was back to its frame (the desk's go home with their stages).
    func restoreForExit() {
        guard !resizedInPlace.isEmpty else { return }
        registry.stop()
        devices = [:]
        order = []
        restoreResized()
    }

    /// Windows sized where they are that no device shows any more: back to their frame (a window on the desk
    /// goes home with its stage).
    private func restoreResized() {
        let shown = interest()
        for (id, p) in resizedInPlace where !shown.contains(id) {
            resizedInPlace[id] = nil
            limits[id] = nil
            guard !synthetic, desk.element(for: id) == nil else { continue }
            if sizer.restore(p) { print("Coherence: \(p.appName) — \(p.title) back to its size.") }
        }
    }

    // MARK: Gates and the capture's scale

    private func refreshGates() {
        gates = CoherenceGates.make(order.compactMap { devices[$0] }.map {
            CoherenceGates.Shown(ask: $0.ask, windows: effective($0), owners: $0.owners)
        })
        sideStreams()?.applyGates()
    }

    /// A window's gate, for SideStreams; nil for one no device in Coherence shows.
    func gate(_ id: UInt32) -> CoherenceGate? { gates[id] }

    /// A device's windows in front and hidden, for which of them SideStreams streams when it shows more than
    /// it streams (SidePlan); nil for a device not in Coherence.
    func picks(_ c: NWConnection) -> (front: Set<UInt32>, hidden: Set<UInt32>)? {
        devices[ObjectIdentifier(c)].map { CoherenceGates.picks(CoherenceGates.Shown(ask: $0.ask, windows: effective($0), owners: $0.owners)) }
    }

    /// The knob side streams capture at while a device is in Coherence (its scale), else `knob`.
    func captureKnob(_ knob: CGFloat) -> CGFloat {
        guard isActive else { return knob }
        return CoherenceScale.knob(knob, deviceScales: devices.values.compactMap(\.ask.scale))
    }

    // MARK: The registry's looks, joins and kind 45

    private func registryChanged() {
        let now = CFAbsoluteTimeGetCurrent()
        let read = registry.last
        rectCache = [:]
        // Regular windows that may join, front to back, and each app's front one.
        var candidates: [JoinCandidate] = []
        var fronts: [String: UInt32] = [:]
        let own = Int32(ProcessInfo.processInfo.processIdentifier)
        for id in read.order {
            guard let w = read.windows[id], let bundle = read.apps[w.pid]?.bundleID,
                  CoherenceEntries.joinable(w, bundle: bundle, ownPID: own) else { continue }
            if fronts[bundle] == nil { fronts[bundle] = id }
            candidates.append(JoinCandidate(id: id, bundle: bundle, firstSeen: read.firstSeen[id] ?? -.infinity))
        }
        var anyJoined = false
        for key in order {
            guard let d = devices[key] else { continue }
            let listed = Set(effective(d))
            guard let joined = devices[key]?.joins.consider(candidates, fronts: fronts, listed: listed, now: now), !joined.isEmpty,
                  let updated = devices[key] else { continue }
            anyJoined = true
            print("Coherence: window\(joined.count == 1 ? "" : "s") \(joined.map(String.init).joined(separator: ", ")) joined \(deviceName(updated.connection))'s set by \(joined.count == 1 ? "itself" : "themselves").")
            sideStreams()?.request(effectiveRequest(updated), from: updated.connection)
        }
        if anyJoined { onJoined?() }
        if read.keyWindow != lastKeyWindow {
            lastKeyWindow = read.keyWindow
            onKeyWindowChanged?()
        }
        refreshGates()
        resolveChildren()
    }

    /// A kind 45 to this device as soon as its pace allows, with every change meanwhile.
    private func schedule(_ key: ObjectIdentifier) {
        guard let d = devices[key], !d.scheduled else { return }
        let wait = d.pacer.wait(now: CFAbsoluteTimeGetCurrent())
        guard wait > 0 else { send(key); return }
        devices[key]?.scheduled = true
        Task { @MainActor [weak self] in
            try? await Task.sleep(for: .milliseconds(Int(wait * 1000) + 1))
            guard let self, self.devices[key] != nil else { return }
            self.devices[key]?.scheduled = false
            self.send(key)
        }
    }

    private func send(_ key: ObjectIdentifier) {
        guard var d = devices[key] else { return }
        let now = CFAbsoluteTimeGetCurrent()
        var pictures: [UInt32: SidePicture] = [:]
        if let side = sideStreams() { for id in side.streaming { pictures[id] = side.pipeline(id)?.picture ?? .unknown } }
        let result = CoherenceEntries.make(read: registry.last, windows: effective(d), joined: d.joins.ever, limits: limits, pictures: pictures)
        let ownersChanged = result.owners != d.owners
        d.owners = result.owners
        let answering = d.owed.first
        let message = d.reporter.message(result, secureInput: registry.last.secureInput, answering: answering, at: now)
        if message != nil {
            if !d.owed.isEmpty { d.owed.removeFirst() }
            d.pacer.sent(at: now)
        }
        devices[key] = d
        if ownersChanged { refreshGates() }
        guard let message else { return }
        server.send(StreamMessage(kind: .windowState, timestamp: Date().timeIntervalSince1970, isKeyframe: false,
                                  payload: Wire.encode(message)), to: d.connection)
        Stats.shared.bump("coh.state")
        if !d.owed.isEmpty { schedule(key) }
    }

    // MARK: Kind 46

    /// A device sizes one of its windows: applied at once, or held (the newest) until the window's interval
    /// is over, and one at a time per window.
    func place(_ p: PlaceWindow, from c: NWConnection) {
        let key = ObjectIdentifier(c)
        guard let d = devices[key], effective(d).contains(p.id) else {
            Stats.shared.bump("coh.placeRefused")
            return
        }
        placeFor[p.id] = key
        switch placer.offer(p, now: CFAbsoluteTimeGetCurrent()) {
        case .now: start(p, for: key)
        case .held: schedulePlaceFlush()
        }
    }

    /// The held sizes applied as their windows' intervals end (the newest of each).
    private func schedulePlaceFlush() {
        guard placeFlush == nil, let due = placer.nextDue else { return }
        placeFlush = Task { @MainActor [weak self] in
            try? await Task.sleep(for: .milliseconds(Int(max(0, due - CFAbsoluteTimeGetCurrent()) * 1000) + 1))
            guard let self else { return }
            self.placeFlush = nil
            for p in self.placer.due(now: CFAbsoluteTimeGetCurrent()) {
                if let key = self.placeFor[p.id], self.devices[key] != nil { self.start(p, for: key) }
            }
            self.schedulePlaceFlush()
        }
    }

    private func start(_ p: PlaceWindow, for key: ObjectIdentifier) {
        guard !placing.contains(p.id) else { placeQueued[p.id] = (p, key); return }
        placing.insert(p.id)
        Task { @MainActor [weak self] in
            await self?.apply(p, for: key)
            guard let self else { return }
            self.placing.remove(p.id)
            if let (next, owner) = self.placeQueued.removeValue(forKey: p.id) { self.start(next, for: owner) }
        }
    }

    /// One size: written through Accessibility (never AXFullScreen), read back, what was refused learned,
    /// answered with a kind 45 carrying the size the window took.
    private func apply(_ p: PlaceWindow, for key: ObjectIdentifier) async {
        guard let d = devices[key], let size = PlaceSize.asked(p.size, room: d.screen) else { return }
        let id = p.id
        if synthetic {
            print("Coherence: window \(id) would be sized to \(Int(size.width))×\(Int(size.height)) pt (not on a test host).")
        } else if let element = element(of: id) {
            let onDesk = desk.element(for: id) != nil
            if !onDesk, resizedInPlace[id] == nil, let frame = WindowSizer.frame(of: element), let pid = pid(of: id) {
                let w = catalog.window(id: id)
                resizedInPlace[id] = WindowSizer.Placement(windowID: id, pid: pid, element: element, originalFrame: frame,
                                                           appName: w?.owningApplication?.applicationName ?? "?", title: w?.title ?? "")
            }
            let before = WindowSizer.frame(of: element)?.size
            let err = WindowSizer.setSize(element, size)
            if err == .success || err == .cannotComplete {
                let took = await settledSize(element, asked: size, before: before)
                if err == .success, let took { limits[id, default: SizeLimits()].learn(asked: size, took: took) }
            } else {
                print("Coherence: window \(id) refused the size \(Int(size.width))×\(Int(size.height)) pt (\(WindowSizer.axErrorName(err))).")
            }
            Stats.shared.bump("coh.placed")
        } else {
            print("Coherence: window \(id) not found through Accessibility; not sized.")
        }
        registry.markDirty(id)
        registry.look()
        guard devices[key] != nil else { return }
        if let token = p.token { devices[key]?.owed.append((token, id)) }
        schedule(key)
        resizeRefresh?.cancel()
        resizeRefresh = Task { @MainActor [weak self] in
            try? await Task.sleep(for: .milliseconds(Int(Self.resizeSettle * 1000)))
            guard !Task.isCancelled, let self else { return }
            self.onResized?()
        }
    }

    /// The size a window took: Accessibility's at once when it is the size asked; an app that applies a size
    /// late (it reads back its old one) or clamps it is waited for until its size holds still for two reads
    /// at something else, 0.3 s at most (VirtualStage's settle, shorter: a device resizing sends 20 a second).
    private func settledSize(_ e: AXUIElement, asked: CGSize, before: CGSize?) async -> CGSize? {
        func near(_ a: CGSize?, _ b: CGSize?) -> Bool {
            guard let a, let b else { return false }
            return abs(a.width - b.width) <= 2 && abs(a.height - b.height) <= 2
        }
        var last = WindowSizer.frame(of: e)?.size
        if near(last, asked) { return last }
        let deadline = CFAbsoluteTimeGetCurrent() + 0.3
        var same = 0
        while CFAbsoluteTimeGetCurrent() < deadline {
            try? await Task.sleep(for: .milliseconds(30))
            let now = WindowSizer.frame(of: e)?.size
            if near(now, asked) { return now }
            same = near(now, last) ? same + 1 : 0
            last = now
            if same >= 2, !near(now, before) { break }
        }
        return last
    }

    // MARK: What the coordinator reads

    /// Whether a device in Coherence is shown this window: one of its set, or a child of one (input, kind 15).
    func shows(_ window: UInt32, to c: NWConnection) -> Bool {
        guard let d = devices[ObjectIdentifier(c)] else { return false }
        return d.owners[window] != nil || effective(d).contains(window)
    }

    /// Whether some device has this window as a child (the menu's list of shared windows leaves it out).
    func isChild(_ id: UInt32) -> Bool { devices.values.contains { $0.owners[id] != nil } }

    /// The window an event aimed at `window` activates: its owner for a child.
    func activationTarget(_ window: UInt32, for c: NWConnection) -> UInt32 {
        devices[ObjectIdentifier(c)]?.owners[window] ?? window
    }

    /// Whether to activate `window` for an event that needs it key (ActivationGate: per window).
    func shouldActivate(_ window: UInt32, isKey: Bool, now: Double) -> Bool {
        activations.shouldActivate(window: window, isKey: isKey, now: now)
    }

    /// A new activation's hold: its token.
    func nextHold() -> Int {
        holdToken += 1
        return holdToken
    }

    /// The Mac's key window as the registry last saw it.
    var keyWindow: UInt32? { registry.last.keyWindow }

    func pid(of id: UInt32) -> pid_t? {
        registry.last.windows[id]?.pid ?? desk.element(for: id)?.pid ?? catalog.window(id: id)?.owningApplication?.processID
    }

    /// A window's Accessibility element: its desk stage's, else the registry's, else matched by the sizer.
    func element(of id: UInt32) -> AXUIElement? {
        if let e = desk.element(for: id)?.element { return e }
        if let e = registry.element(for: id) { return e }
        return catalog.window(id: id).flatMap { sizer.element(for: $0) }
    }

    /// Where a window's picture is on screen (global points), which a device's fractions are of: its crop on
    /// the desk, else its live bounds (cached a tenth of a second: a pen's moves come at 120 Hz).
    func rect(of id: UInt32) -> CGRect? {
        if let onDesk = desk.captureRect(for: id) { return onDesk }
        let now = CFAbsoluteTimeGetCurrent()
        if let c = rectCache[id], now - c.at < 0.1 { return c.rect }
        guard let r = WindowSizer.liveBounds(of: id) ?? registry.last.windows[id]?.frame ?? catalog.window(id: id)?.frame else { return nil }
        rectCache[id] = (r, now)
        return r
    }

    /// A child window to capture alone (SideStreams' `childWindow`).
    func childWindow(_ id: UInt32) -> SCWindow? { childWindows[id] }

    /// The children the devices ask to stream (their kind 29s list them: they spill out of their owner's
    /// picture) found in ScreenCaptureKit's list, which the catalog never holds; once a second at most for
    /// the same ones.
    private func resolveChildren() {
        var wanted = Set<UInt32>()
        for d in devices.values {
            for id in effective(d) where d.owners[id] != nil || (catalog.window(id: id) == nil && (registry.last.windows[id]?.layer ?? 0) != 0) {
                wanted.insert(id)
            }
        }
        childWindows = childWindows.filter { wanted.contains($0.key) }
        let missing = wanted.filter { childWindows[$0] == nil }
        guard !missing.isEmpty, !resolving else { return }
        let now = CFAbsoluteTimeGetCurrent()
        if let last = resolvedAt, last.ids == missing, now - last.at < 1 { return }
        resolving = true
        resolvedAt = (missing, now)
        Task { @MainActor [weak self] in
            let content = await WindowCatalog.shareableContent(excludingDesktopWindows: false, onScreenWindowsOnly: true, timeout: 1)
            guard let self else { return }
            self.resolving = false
            var found = false
            for w in content?.windows ?? [] where missing.contains(w.windowID) && !WindowCatalog.isOwn(w) {
                self.childWindows[w.windowID] = w
                found = true
            }
            if found { self.sideStreams()?.changed() }
        }
    }

    // MARK: Kind 44, kind 7, an app a device's action opened

    /// Kind 44 `{window}`: a keyframe from that window's encoder only, once a second at most per device and
    /// window. Nothing for a window this host does not stream.
    func keyframe(_ window: UInt32, from c: NWConnection) {
        guard sideStreams()?.streaming.contains(window) == true else { return }
        let key = ObjectIdentifier(c)
        let now = CFAbsoluteTimeGetCurrent()
        guard SideSend.mayAskKeyframe(lastAsked: keyframeAskedAt[key]?[window], now: now) else {
            Stats.shared.bump("coh.keyframeDropped")
            return
        }
        keyframeAskedAt[key, default: [:]][window] = now
        _ = sideStreams()?.requestKeyframe(window)
        Stats.shared.bump("coh.keyframe")
    }

    /// An app a device in Coherence launched (kind 7): its next window joins every set (else its front one).
    func launched(_ bundleID: String) {
        guard isActive, !bundleID.isEmpty else { return }
        let now = CFAbsoluteTimeGetCurrent()
        launchedApps[bundleID] = now
        for key in Array(devices.keys) { devices[key]?.joins.launched(bundleID, now: now) }
        registry.lookSoon()
    }

    /// Whether a launch the coordinator waits for is Coherence's (its window joins a set, not the main stream).
    func ownsLaunch(_ bundleID: String) -> Bool {
        launchedApps[bundleID].map { CFAbsoluteTimeGetCurrent() - $0 < JoinRules.launchSpan } ?? false
    }

    /// An app a device's action brought forward that no set shows (a link opened from a chat): its front
    /// window joins, as an app launched from the device (StreamCoordinator's `followIfOpenedByDevice`).
    func follow(pid: pid_t) {
        guard isActive, pid != ProcessInfo.processInfo.processIdentifier,
              let bundle = NSRunningApplication(processIdentifier: pid)?.bundleIdentifier else { return }
        let shownPids = Set(interest().compactMap { registry.last.windows[$0]?.pid })
        guard !shownPids.contains(pid) else { return }
        launched(bundle)
    }

    // MARK: Kind 4 at the hello's size

    /// A device's hello: with `iconSize`, the icons of the apps with windows go to it at that size (16…512),
    /// and each app's as its first window shows; the installed apps' list keeps 64 px.
    func hello(_ c: NWConnection, _ hello: Hello) {
        guard let asked = hello.iconSize else { return }
        iconSizes[ObjectIdentifier(c)] = (c, IconSizing.drawn(asked: asked))
        sendIcons()
    }

    /// The catalog's window list changed: icons of apps new to it, at each device's size.
    func windowsListed() { if !iconSizes.isEmpty { sendIcons() } }

    private func sendIcons() {
        let bundles = Set(catalog.infos.map(\.bundleID)).filter { !$0.isEmpty }
        let server = self.server
        for (key, want) in iconSizes {
            let missing = bundles.subtracting(iconsSent[key] ?? []).sorted()
            guard !missing.isEmpty else { continue }
            iconsSent[key, default: []].formUnion(missing)
            let connection = want.connection, size = want.size
            // Drawn off the main actor (a 512 px icon is tens of milliseconds); the server's send is thread-safe.
            Task.detached(priority: .utility) {
                for bundle in missing {
                    guard let png = WindowCatalog.iconPNG(bundleID: bundle, size: size) else { continue }
                    server.send(StreamMessage(kind: .appIcon, timestamp: Date().timeIntervalSince1970, isKeyframe: false,
                                              payload: ImageBlob.encodeIcon(bundleID: bundle, png: png)), to: connection)
                }
            }
        }
    }
}
