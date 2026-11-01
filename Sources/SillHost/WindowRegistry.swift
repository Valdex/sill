import AppKit
import ApplicationServices
import Foundation
import StreamProtocol

/// Coherence's look at the Mac's windows (kind 45, docs/coherence-protocol.md; M1b): while a device shows
/// windows in Coherence, what the window server says of every window on screen (and of each window of
/// interest off it: minimized, in another Space), what Accessibility says of the windows the devices are
/// shown (`interest`), the key window and Secure Event Input. CoherenceEntries (pure) makes kind 45 of it.
///
/// When it looks: every `pollInterval` while it runs (the window server's list costs a few milliseconds),
/// and soon after an Accessibility notification of any regular app (a window created, moved, resized,
/// miniaturized, deminiaturized, focused, a sheet or a menu opened; a window of interest destroyed or
/// retitled) or an app launched, quit or activated, at most every `leastInterval`. Accessibility is read
/// again only for a window marked by a notification, whose frame changed, whose app's windows changed, or
/// read more than `axRefresh` ago: each read is a call into the app. Every element gets `axTimeout` as
/// its messaging timeout, as the repo's other AX code does (a hung app costs a quarter second, not the host).
///
/// The notifications reach the main run loop, which runs under the AppKit loop (Sill.app, the CLI's
/// `--virtual-display`); without it the poll alone sees every change, a little later. Main actor.
@MainActor
final class WindowRegistry {
    /// The windows the devices are shown (their sets): read through Accessibility, and looked up when off screen.
    var interest: () -> Set<UInt32> = { [] }
    /// A look found something different from the last (`last`).
    var onChange: (() -> Void)?
    /// Every look, after `onChange`: what the devices are told may have changed outside the look (a stream
    /// started, so a child's `inCrop` is known).
    var onLooked: (() -> Void)?
    /// The last look.
    private(set) var last = RegistryRead()
    private(set) var running = false
    /// This process runs NSApplication's event loop, so `NSWorkspace.frontmostApplication` is current
    /// (StreamCoordinator's `activePID`); otherwise the owner of the frontmost normal window is the active app.
    var trustAppKit = false

    static let pollInterval: Double = 0.25
    static let leastInterval: Double = 0.05
    static let axRefresh: Double = 2
    static let axTimeout: Float = 0.25

    private var observers: [pid_t: AXObserver] = [:]
    private var workspaceTokens: [NSObjectProtocol] = []
    private var timer: DispatchSourceTimer?
    private var lookedAt: CFAbsoluteTime = 0
    private var lookOwed = false
    /// Windows whose Accessibility facts are read again at the next look.
    private var dirty: Set<UInt32> = []
    private var axCache: [UInt32: (facts: AXFacts, at: CFAbsoluteTime, frame: CGRect?)] = [:]
    private var elements: [UInt32: AXUIElement] = [:]
    /// Windows whose element has its own notifications (destroyed, title changed).
    private var watchedElements: Set<UInt32> = []
    private var elementsLookedAt: [pid_t: CFAbsoluteTime] = [:]
    private var apps: [pid_t: AppFacts] = [:]
    private var firstSeen: [UInt32: Double] = [:]
    private var lookedOnce = false
    /// Each app's windows on screen at the last look: a change marks its windows of interest (a sheet).
    private var pidWindows: [pid_t: Set<UInt32>] = [:]
    private let relay = Relay()

    init() { relay.registry = self }

    /// Notifications on each app's element. Window-level ones are delivered for its windows too.
    private static let appNotifications = [kAXWindowCreatedNotification, kAXFocusedWindowChangedNotification, kAXMainWindowChangedNotification,
                                           kAXWindowMovedNotification, kAXWindowResizedNotification, kAXWindowMiniaturizedNotification,
                                           kAXWindowDeminiaturizedNotification, kAXSheetCreatedNotification, kAXMenuOpenedNotification,
                                           kAXMenuClosedNotification]
    /// Notifications on a window of interest's own element.
    private static let windowNotifications = [kAXUIElementDestroyedNotification, kAXTitleChangedNotification]

    // MARK: Running

    func start() {
        guard !running else { return }
        running = true
        let center = NSWorkspace.shared.notificationCenter
        workspaceTokens = [
            center.addObserver(forName: NSWorkspace.didLaunchApplicationNotification, object: nil, queue: nil) { [weak self] note in
                let pid = (note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication)?.processIdentifier
                Task { @MainActor in
                    guard let self, let pid else { return }
                    self.observe(pid)
                    self.lookSoon()
                }
            },
            center.addObserver(forName: NSWorkspace.didTerminateApplicationNotification, object: nil, queue: nil) { [weak self] note in
                let pid = (note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication)?.processIdentifier
                Task { @MainActor in
                    guard let self, let pid else { return }
                    self.forgetApp(pid)
                    self.lookSoon()
                }
            },
            center.addObserver(forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: nil) { [weak self] _ in
                Task { @MainActor in self?.lookSoon() }
            },
        ]
        for app in NSWorkspace.shared.runningApplications where app.activationPolicy == .regular { observe(app.processIdentifier) }
        let t = DispatchSource.makeTimerSource(queue: .main)
        t.schedule(deadline: .now() + Self.pollInterval, repeating: Self.pollInterval, leeway: .milliseconds(20))
        t.setEventHandler { [weak self] in Task { @MainActor in self?.look() } }
        t.resume()
        timer = t
        look()
    }

    func stop() {
        guard running else { return }
        running = false
        timer?.cancel()
        timer = nil
        for token in workspaceTokens { NSWorkspace.shared.notificationCenter.removeObserver(token) }
        workspaceTokens = []
        for observer in observers.values {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), AXObserverGetRunLoopSource(observer), CFRunLoopMode.commonModes)
        }
        observers = [:]
        elements = [:]
        watchedElements = []
        elementsLookedAt = [:]
        axCache = [:]
        dirty = []
        apps = [:]
        firstSeen = [:]
        pidWindows = [:]
        lookedOnce = false
        last = RegistryRead()
    }

    /// A look soon: at once when the last was `leastInterval` ago, else at the end of it.
    func lookSoon() {
        guard running, !lookOwed else { return }
        let wait = lookedAt + Self.leastInterval - CFAbsoluteTimeGetCurrent()
        guard wait > 0 else { look(); return }
        lookOwed = true
        Task { @MainActor [weak self] in
            try? await Task.sleep(for: .milliseconds(Int(wait * 1000) + 1))
            guard let self else { return }
            self.lookOwed = false
            self.look()
        }
    }

    /// This window's Accessibility facts are read again at the next look (its size was just written).
    func markDirty(_ id: UInt32) { dirty.insert(id) }

    // MARK: Looking

    /// One look: the window server's list, Accessibility for the windows of interest, the key window and
    /// Secure Event Input; `onChange` when it differs from the last.
    func look() {
        guard running else { return }
        let now = CFAbsoluteTimeGetCurrent()
        lookedAt = now
        let interest = self.interest()
        var read = RegistryRead()
        let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] ?? []
        for info in list {
            guard let f = Self.facts(info) else { continue }
            read.windows[f.id] = f
            read.order.append(f.id)
        }
        // A window of interest off screen (minimized, another Space): its own entry from the window server.
        for id in interest where read.windows[id] == nil {
            if let info = (CGWindowListCopyWindowInfo([.optionIncludingWindow], id) as? [[String: Any]])?.first,
               let f = Self.facts(info), f.id == id { read.windows[id] = f }
        }
        // First seen: the windows open at the first look count as open before (JoinRules), those out of sight
        // then too (minimized, in another Space: shown again later, they did not open).
        if !lookedOnce, let all = CGWindowListCopyWindowInfo(.optionAll, kCGNullWindowID) as? [[String: Any]] {
            for info in all { if let f = Self.facts(info), f.layer == 0 { firstSeen[f.id] = -.infinity } }
        }
        for f in read.windows.values where f.layer == 0 && f.onScreen && firstSeen[f.id] == nil {
            firstSeen[f.id] = lookedOnce ? now : -.infinity
        }
        lookedOnce = true
        if firstSeen.count > 4096 { firstSeen = firstSeen.filter { read.windows[$0.key] != nil } }
        read.firstSeen = firstSeen.filter { read.windows[$0.key] != nil }
        // Each app's windows: a change marks its windows of interest (a sheet came or went).
        var byPid: [pid_t: Set<UInt32>] = [:]
        for f in read.windows.values where f.onScreen { byPid[f.pid, default: []].insert(f.id) }
        for id in interest {
            guard let pid = read.windows[id]?.pid else { continue }
            if pidWindows[pid] != byPid[pid] { dirty.insert(id) }
        }
        pidWindows = byPid
        for pid in Set(read.windows.values.map(\.pid)) { read.apps[pid] = app(pid) }
        // Accessibility, for the windows the devices are shown.
        for id in interest {
            guard let w = read.windows[id] else { continue }
            if let facts = axFacts(id, pid: w.pid, frame: w.frame, now: now) { read.ax[id] = facts }
        }
        // What is gone is forgotten.
        for id in Array(axCache.keys) where read.windows[id] == nil { axCache[id] = nil; elements[id] = nil; watchedElements.remove(id) }
        read.keyWindow = keyWindow(order: read.order, windows: read.windows)
        read.secureInput = SecureInput.on
        if read != last {
            last = read
            onChange?()
        }
        onLooked?()
    }

    /// The window server's facts of one entry of its list.
    private static func facts(_ info: [String: Any]) -> CGFacts? {
        guard let id = info[kCGWindowNumber as String] as? CGWindowID,
              let pid = info[kCGWindowOwnerPID as String] as? pid_t,
              let b = info[kCGWindowBounds as String] as? NSDictionary,
              let rect = CGRect(dictionaryRepresentation: b as CFDictionary) else { return nil }
        return CGFacts(id: id, pid: pid, layer: info[kCGWindowLayer as String] as? Int ?? 0, frame: rect,
                       onScreen: info[kCGWindowIsOnscreen as String] as? Bool ?? false,
                       alpha: info[kCGWindowAlpha as String] as? Double ?? 1, name: info[kCGWindowName as String] as? String)
    }

    private func app(_ pid: pid_t) -> AppFacts {
        if let known = apps[pid] { return known }
        let running = NSRunningApplication(processIdentifier: pid)
        let facts = AppFacts(bundleID: running?.bundleIdentifier, name: running?.localizedName)
        apps[pid] = facts
        return facts
    }

    private func forgetApp(_ pid: pid_t) {
        if let observer = observers.removeValue(forKey: pid) {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), AXObserverGetRunLoopSource(observer), CFRunLoopMode.commonModes)
        }
        apps[pid] = nil
        elementsLookedAt[pid] = nil
    }

    /// A window's Accessibility facts: the last read while it holds (not marked, the same frame, younger than
    /// `axRefresh`), else read now; the last read when the window's element is not found or no longer answers.
    private func axFacts(_ id: UInt32, pid: pid_t, frame: CGRect?, now: CFAbsoluteTime) -> AXFacts? {
        if let c = axCache[id], !dirty.contains(id), c.frame == frame, now - c.at < Self.axRefresh { return c.facts }
        guard let element = element(for: id, pid: pid, frame: frame) else { return axCache[id]?.facts }
        var role: CFTypeRef?
        if AXUIElementCopyAttributeValue(element, kAXRoleAttribute as CFString, &role) == .invalidUIElement {
            elements[id] = nil
            watchedElements.remove(id)
            return axCache[id]?.facts
        }
        let facts = Self.read(element)
        axCache[id] = (facts, now, frame)
        dirty.remove(id)
        return facts
    }

    /// What Accessibility says of a window: its title, subrole, states, whether its size is settable, its
    /// traffic lights, frame, and among its children a toolbar, sheets and popovers.
    private static func read(_ e: AXUIElement) -> AXFacts {
        var f = AXFacts()
        f.title = string(e, kAXTitleAttribute)
        f.subrole = string(e, kAXSubroleAttribute)
        f.minimized = bool(e, kAXMinimizedAttribute) ?? false
        f.fullScreen = bool(e, "AXFullScreen") ?? false
        f.modal = bool(e, kAXModalAttribute) ?? false
        var settable = DarwinBoolean(false)
        if AXUIElementIsAttributeSettable(e, kAXSizeAttribute as CFString, &settable) == .success { f.resizable = settable.boolValue }
        f.close = button(e, kAXCloseButtonAttribute)
        f.minimize = button(e, kAXMinimizeButtonAttribute)
        f.zoom = button(e, kAXZoomButtonAttribute)
        f.frame = WindowSizer.frame(of: e)
        var value: CFTypeRef?
        if AXUIElementCopyAttributeValue(e, kAXChildrenAttribute as CFString, &value) == .success, let children = value as? [AXUIElement] {
            for child in children {
                AXUIElementSetMessagingTimeout(child, axTimeout)
                guard let role = string(child, kAXRoleAttribute) else { continue }
                if role == kAXToolbarRole as String {
                    f.toolbar = true
                } else if role == kAXSheetRole as String, let r = WindowSizer.frame(of: child) {
                    f.sheets.append(r)
                } else if role == "AXPopover", let r = WindowSizer.frame(of: child) {
                    f.popovers.append(r)
                }
            }
        }
        return f
    }

    private static func string(_ e: AXUIElement, _ attribute: String) -> String? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(e, attribute as CFString, &value) == .success else { return nil }
        return value as? String
    }

    private static func bool(_ e: AXUIElement, _ attribute: String) -> Bool? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(e, attribute as CFString, &value) == .success else { return nil }
        return value as? Bool
    }

    /// A traffic light's frame (global points), from the window's button attribute.
    private static func button(_ e: AXUIElement, _ attribute: String) -> CGRect? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(e, attribute as CFString, &value) == .success, let value,
              CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        let b = value as! AXUIElement
        AXUIElementSetMessagingTimeout(b, axTimeout)
        return WindowSizer.frame(of: b)
    }

    // MARK: Elements

    /// The Accessibility element of a window: matched once per app (its windows' IDs, `AXWindowID`; else by
    /// frame), at most once a second per app, and kept while it answers.
    func element(for id: UInt32) -> AXUIElement? {
        if let e = elements[id] { return e }
        guard let w = last.windows[id] else { return nil }
        return element(for: id, pid: w.pid, frame: w.frame)
    }

    private func element(for id: UInt32, pid: pid_t, frame: CGRect?) -> AXUIElement? {
        if let e = elements[id] { return e }
        guard AXIsProcessTrusted() else { return nil }
        let now = CFAbsoluteTimeGetCurrent()
        if let at = elementsLookedAt[pid], now - at < 1 { return nil }
        elementsLookedAt[pid] = now
        let app = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(app, Self.axTimeout)
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(app, kAXWindowsAttribute as CFString, &value) == .success,
              let windows = value as? [AXUIElement] else { return nil }
        var unnamed: [AXUIElement] = []
        for w in windows {
            AXUIElementSetMessagingTimeout(w, Self.axTimeout)
            if let wid = AXWindowID.of(w) { elements[wid] = w } else { unnamed.append(w) }
        }
        // Without the window's ID (the private call missing): the one whose frame is the window server's.
        if elements[id] == nil, let frame {
            elements[id] = unnamed.first { w in
                guard let f = WindowSizer.frame(of: w) else { return false }
                return abs(f.minX - frame.minX) <= 4 && abs(f.minY - frame.minY) <= 4
                    && abs(f.width - frame.width) <= 4 && abs(f.height - frame.height) <= 4
            }
        }
        if let e = elements[id], !watchedElements.contains(id), let observer = observers[pid] {
            let refcon = Unmanaged.passUnretained(relay).toOpaque()
            for n in Self.windowNotifications { AXObserverAddNotification(observer, e, n as CFString, refcon) }
            watchedElements.insert(id)
        }
        return elements[id]
    }

    /// The key window now, read afresh: the frontmost app's focused window.
    func readKeyWindow() -> UInt32? { keyWindow(order: last.order, windows: last.windows) }

    private func keyWindow(order: [UInt32], windows: [UInt32: CGFacts]) -> UInt32? {
        let front: pid_t?
        if trustAppKit, let pid = NSWorkspace.shared.frontmostApplication?.processIdentifier {
            front = pid
        } else {
            front = order.lazy.compactMap { windows[$0] }.first { $0.layer == 0 && $0.alpha > 0 }?.pid
        }
        guard let pid = front, AXIsProcessTrusted() else { return nil }
        let app = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(app, Self.axTimeout)
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(app, kAXFocusedWindowAttribute as CFString, &value) == .success, let value,
              CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        let w = value as! AXUIElement
        if let id = AXWindowID.of(w) { return id }
        return elements.first { CFEqual($0.value, w) }?.key
    }

    /// The active app's pid, as StreamCoordinator's `activePID` reads it.
    func activePID() -> pid_t? {
        if trustAppKit, let pid = NSWorkspace.shared.frontmostApplication?.processIdentifier { return pid }
        let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] ?? []
        return list.lazy.compactMap { Self.facts($0) }.first { $0.layer == 0 && $0.alpha > 0 }?.pid
    }

    // MARK: Notifications

    /// Notifications of an app's windows, on the main run loop.
    private func observe(_ pid: pid_t) {
        guard running, observers[pid] == nil, pid != ProcessInfo.processInfo.processIdentifier, AXIsProcessTrusted() else { return }
        var made: AXObserver?
        guard AXObserverCreate(pid, registryCallback, &made) == .success, let observer = made else { return }
        let app = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(app, Self.axTimeout)
        let refcon = Unmanaged.passUnretained(relay).toOpaque()
        for n in Self.appNotifications { AXObserverAddNotification(observer, app, n as CFString, refcon) }
        CFRunLoopAddSource(CFRunLoopGetMain(), AXObserverGetRunLoopSource(observer), CFRunLoopMode.commonModes)
        observers[pid] = observer
    }

    /// An Accessibility notification from `pid`: its windows of interest are read again, and a look soon. A
    /// window destroyed drops that app's elements (matched again at the next look).
    fileprivate func notified(pid: pid_t, name: String) {
        guard running else { return }
        if name == kAXUIElementDestroyedNotification as String {
            for (id, _) in elements where last.windows[id]?.pid == pid { elements[id] = nil; watchedElements.remove(id) }
            elementsLookedAt[pid] = nil
        }
        for id in interest() where last.windows[id]?.pid == pid { dirty.insert(id) }
        lookSoon()
    }

    /// The callback's way back to the registry (its refcon): weak, so a notification late after a stop is dropped.
    final class Relay: @unchecked Sendable {
        weak var registry: WindowRegistry?
        func heard(pid: pid_t, name: String) {
            Task { @MainActor [weak self] in self?.registry?.notified(pid: pid, name: name) }
        }
    }
}

/// AXObserver's callback: on the main run loop; hands the notification to the registry through its relay.
private let registryCallback: AXObserverCallback = { _, element, notification, refcon in
    guard let refcon else { return }
    var pid: pid_t = 0
    AXUIElementGetPid(element, &pid)
    let relay = Unmanaged<WindowRegistry.Relay>.fromOpaque(refcon).takeUnretainedValue()
    relay.heard(pid: pid, name: notification as String)
}

/// A window element's CGWindowID: `_AXUIElementGetWindow`, HIServices' private call (window managers have
/// used it for years), looked up at run time as SymbolicHotKeys' are. Missing: nil, and elements are matched
/// by frame.
enum AXWindowID {
    private typealias GetWindow = @convention(c) (AXUIElement, UnsafeMutablePointer<UInt32>) -> AXError
    private static let call: GetWindow? = {
        guard let h = dlopen("/System/Library/Frameworks/ApplicationServices.framework/Frameworks/HIServices.framework/HIServices", RTLD_NOW),
              let f = dlsym(h, "_AXUIElementGetWindow") else { return nil }
        return unsafeBitCast(f, to: GetWindow.self)
    }()

    static func of(_ e: AXUIElement) -> UInt32? {
        guard let call else { return nil }
        var id: UInt32 = 0
        return call(e, &id) == .success && id != 0 ? id : nil
    }
}

/// macOS's Secure Event Input (a password field has the keyboard: typed keys may not land), from HIToolbox's
/// `IsSecureEventInputEnabled`, looked up at run time (no Carbon import). Missing: off.
enum SecureInput {
    private typealias IsEnabled = @convention(c) () -> UInt8
    private static let call: IsEnabled? = {
        guard let h = dlopen("/System/Library/Frameworks/Carbon.framework/Frameworks/HIToolbox.framework/HIToolbox", RTLD_NOW),
              let f = dlsym(h, "IsSecureEventInputEnabled") else { return nil }
        return unsafeBitCast(f, to: IsEnabled.self)
    }()

    static var on: Bool { (call?() ?? 0) != 0 }
}
