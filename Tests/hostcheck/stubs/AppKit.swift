// Stub of AppKit: the application, workspace, running applications, screens, events, the pasteboard,
// dragging, cursors, images and the windows and views the host and Sill.app use, as Swift sees them
// on macOS 14+. AppKit re-exports Foundation, CoreGraphics, CoreImage, QuartzCore and
// ApplicationServices (Accessibility), as its headers import them. Objective-C optional protocol
// requirements are requirements with default implementations here. @MainActor where the macOS 14
// SDK says NS_SWIFT_UI_ACTOR, and @preconcurrency, as Swift takes every Objective-C isolation (so in
// Swift 5 mode a nonisolated use is no error, as on a Mac): the responders (application, windows, views and their controllers),
// menus, screens, dragging and the delegate protocols; never the workspace, running applications,
// the pasteboard, images, colors or graphics contexts.
@_exported import Foundation
@_exported import CoreGraphics
@_exported import CoreImage
@_exported import QuartzCore
@_exported import ApplicationServices
@_exported import UniformTypeIdentifiers

public typealias NSRect = CGRect
public typealias NSPoint = CGPoint
public typealias NSSize = CGSize
public let NSZeroRect = CGRect.zero
public let NSZeroPoint = CGPoint.zero
public let NSZeroSize = CGSize.zero
public typealias NSTimeInterval = TimeInterval
public struct NSEdgeInsets { public var top, left, bottom, right: CGFloat
    public init() { top = 0; left = 0; bottom = 0; right = 0 }
    public init(top: CGFloat, left: CGFloat, bottom: CGFloat, right: CGFloat) { self.top = top; self.left = left; self.bottom = bottom; self.right = right } }

// MARK: Responders, the application

@preconcurrency @MainActor open class NSResponder: NSObject {
    public override init() {}
    open var nextResponder: NSResponder?
    open var acceptsFirstResponder: Bool { false }
    open func becomeFirstResponder() -> Bool { true }
    open func resignFirstResponder() -> Bool { true }
    open func mouseDown(with event: NSEvent) {}
    open func mouseDragged(with event: NSEvent) {}
    open func mouseUp(with event: NSEvent) {}
    open func mouseMoved(with event: NSEvent) {}
    open func mouseEntered(with event: NSEvent) {}
    open func mouseExited(with event: NSEvent) {}
    open func rightMouseDown(with event: NSEvent) {}
    open func scrollWheel(with event: NSEvent) {}
    open func keyDown(with event: NSEvent) {}
    open func keyUp(with event: NSEvent) {}
    open func flagsChanged(with event: NSEvent) {}
    open func cancelOperation(_ sender: Any?) {}
    open func performKeyEquivalent(with event: NSEvent) -> Bool { false }
}

@preconcurrency @MainActor public protocol NSApplicationDelegate: NSObjectProtocol {
    func applicationWillFinishLaunching(_ notification: Notification)
    func applicationDidFinishLaunching(_ notification: Notification)
    func applicationWillTerminate(_ notification: Notification)
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool
    func applicationDidBecomeActive(_ notification: Notification)
    func applicationDidResignActive(_ notification: Notification)
    func applicationSupportsSecureRestorableState(_ app: NSApplication) -> Bool
    func application(_ application: NSApplication, open urls: [URL])
}
extension NSApplicationDelegate {
    public func applicationWillFinishLaunching(_ notification: Notification) {}
    public func applicationDidFinishLaunching(_ notification: Notification) {}
    public func applicationWillTerminate(_ notification: Notification) {}
    public func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply { .terminateNow }
    public func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { true }
    public func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    public func applicationDidBecomeActive(_ notification: Notification) {}
    public func applicationDidResignActive(_ notification: Notification) {}
    public func applicationSupportsSecureRestorableState(_ app: NSApplication) -> Bool { false }
    public func application(_ application: NSApplication, open urls: [URL]) {}
}

open class NSApplication: NSResponder {
    public enum ActivationPolicy: Int { case regular = 0, accessory = 1, prohibited = 2 }
    public enum TerminateReply: UInt { case terminateCancel = 0, terminateNow = 1, terminateLater = 2 }
    public enum RequestUserAttentionType: UInt { case criticalRequest = 0, informationalRequest = 10 }
    public struct ModalResponse: RawRepresentable, Hashable { public var rawValue: Int; public init(rawValue: Int) { self.rawValue = rawValue }
        public static let OK = ModalResponse(rawValue: 1); public static let cancel = ModalResponse(rawValue: 0)
        public static let stop = ModalResponse(rawValue: -1000); public static let abort = ModalResponse(rawValue: -1001)
        public static let alertFirstButtonReturn = ModalResponse(rawValue: 1000); public static let alertSecondButtonReturn = ModalResponse(rawValue: 1001) }
    public class var shared: NSApplication { fatalError() }
    open weak var delegate: NSApplicationDelegate?
    open var mainMenu: NSMenu?
    open var windows: [NSWindow] { [] }
    open var keyWindow: NSWindow? { nil }
    open var mainWindow: NSWindow? { nil }
    open var isActive: Bool { false }
    open var isRunning: Bool { false }
    open var currentEvent: NSEvent? { nil }
    open var applicationIconImage: NSImage!
    open var modalWindow: NSWindow? { nil }
    open var effectiveAppearance: NSAppearance { fatalError() }
    open var appearance: NSAppearance?
    open func run() {}
    open func stop(_ sender: Any?) {}
    open func terminate(_ sender: Any?) {}
    open func reply(toApplicationShouldTerminate shouldTerminate: Bool) {}
    @discardableResult open func setActivationPolicy(_ activationPolicy: ActivationPolicy) -> Bool { true }
    open func activationPolicy() -> ActivationPolicy { .regular }
    open func activate(ignoringOtherApps flag: Bool) {}
    open func activate() {}
    open func deactivate() {}
    open func hide(_ sender: Any?) {}
    open func unhide(_ sender: Any?) {}
    open func runModal(for window: NSWindow) -> ModalResponse { .OK }
    open func stopModal() {}
    open func abortModal() {}
    open func postEvent(_ event: NSEvent, atStart flag: Bool) {}
    open func orderFrontStandardAboutPanel(_ sender: Any?) {}
    open func sendAction(_ action: Selector, to target: Any?, from sender: Any?) -> Bool { false }
    open func requestUserAttention(_ requestType: RequestUserAttentionType) -> Int { 0 }
    public static let didBecomeActiveNotification = Notification.Name("NSApplicationDidBecomeActiveNotification")
    public static let didResignActiveNotification = Notification.Name("NSApplicationDidResignActiveNotification")
    public static let willTerminateNotification = Notification.Name("NSApplicationWillTerminateNotification")
    public static let didChangeScreenParametersNotification = Notification.Name("NSApplicationDidChangeScreenParametersNotification")
    public static let didFinishLaunchingNotification = Notification.Name("NSApplicationDidFinishLaunchingNotification")
}
@preconcurrency @MainActor public var NSApp: NSApplication! { NSApplication.shared }

open class NSAppearance: NSObject {
    public struct Name: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue }
        public init(_ rawValue: String) { self.rawValue = rawValue }
        public static let aqua = Name("NSAppearanceNameAqua"); public static let darkAqua = Name("NSAppearanceNameDarkAqua")
        public static let vibrantLight = Name("NSAppearanceNameVibrantLight"); public static let vibrantDark = Name("NSAppearanceNameVibrantDark") }
    public init?(named name: Name) {}
    open var name: Name { .aqua }
    open func bestMatch(from appearances: [Name]) -> Name? { nil }
    open class var current: NSAppearance! { get { nil } set {} }
    open class var currentDrawing: NSAppearance { fatalError() }
    open func performAsCurrentDrawingAppearance(_ block: () -> Void) {}
}

// MARK: The workspace and running applications

open class NSRunningApplication: NSObject {
    public struct ActivationOptions: OptionSet { public var rawValue: UInt; public init(rawValue: UInt) { self.rawValue = rawValue }
        public static let activateAllWindows = ActivationOptions(rawValue: 1)
        public static let activateIgnoringOtherApps = ActivationOptions(rawValue: 2) }
    public init?(processIdentifier pid: pid_t) {}
    open class var current: NSRunningApplication { fatalError() }
    open class func runningApplications(withBundleIdentifier bundleIdentifier: String) -> [NSRunningApplication] { [] }
    open var processIdentifier: pid_t { 0 }
    open var bundleIdentifier: String? { nil }
    open var localizedName: String? { nil }
    open var bundleURL: URL? { nil }
    open var executableURL: URL? { nil }
    open var icon: NSImage? { nil }
    open var launchDate: Date? { nil }
    open var isActive: Bool { false }
    open var isHidden: Bool { false }
    open var isTerminated: Bool { false }
    open var isFinishedLaunching: Bool { false }
    open var ownsMenuBar: Bool { false }
    open var activationPolicy: NSApplication.ActivationPolicy { .regular }
    open var executableArchitecture: Int { 0 }
    @discardableResult open func activate(options: ActivationOptions = []) -> Bool { false }
    @discardableResult open func activate(from application: NSRunningApplication, options: ActivationOptions = []) -> Bool { false }
    @discardableResult open func hide() -> Bool { false }
    @discardableResult open func unhide() -> Bool { false }
    @discardableResult open func terminate() -> Bool { false }
    @discardableResult open func forceTerminate() -> Bool { false }
}

open class NSWorkspace: NSObject {
    open class OpenConfiguration: NSObject {
        public override init() {}
        open var activates: Bool = true
        open var addsToRecentItems: Bool = true
        open var promptsUserIfNeeded: Bool = true
        open var hides: Bool = false
        open var hidesOthers: Bool = false
        open var createsNewApplicationInstance: Bool = false
        open var allowsRunningApplicationSubstitution: Bool = true
        open var arguments: [String] = []
        open var environment: [String: String] = [:]
        open var isForPrinting: Bool = false
        open var requiresUniversalLinks: Bool = false
    }
    open class var shared: NSWorkspace { fatalError() }
    open var notificationCenter: NotificationCenter { fatalError() }
    open var frontmostApplication: NSRunningApplication? { nil }
    open var menuBarOwningApplication: NSRunningApplication? { nil }
    open var runningApplications: [NSRunningApplication] { [] }
    open func icon(forFile fullPath: String) -> NSImage { fatalError() }
    open func icon(for contentType: UTType) -> NSImage { fatalError() }
    open func urlForApplication(withBundleIdentifier bundleIdentifier: String) -> URL? { nil }
    open func urlForApplication(toOpen url: URL) -> URL? { nil }
    open func urlsForApplications(withBundleIdentifier bundleIdentifier: String) -> [URL] { [] }
    @discardableResult open func open(_ url: URL) -> Bool { false }
    open func open(_ url: URL, configuration: OpenConfiguration, completionHandler: ((NSRunningApplication?, Error?) -> Void)? = nil) {}
    open func open(_ urls: [URL], withApplicationAt applicationURL: URL, configuration: OpenConfiguration,
                   completionHandler: ((NSRunningApplication?, Error?) -> Void)? = nil) {}
    open func openApplication(at applicationURL: URL, configuration: OpenConfiguration,
                              completionHandler: ((NSRunningApplication?, Error?) -> Void)? = nil) {}
    open func recycle(_ URLs: [URL], completionHandler handler: (([URL: URL], Error?) -> Void)? = nil) {}
    open func activateFileViewerSelecting(_ fileURLs: [URL]) {}
    @discardableResult open func selectFile(_ fullPath: String?, inFileViewerRootedAtPath rootFullPath: String) -> Bool { false }
    public static let applicationUserInfoKey: String = "NSWorkspaceApplicationKey"
    public static let didActivateApplicationNotification = Notification.Name("NSWorkspaceDidActivateApplicationNotification")
    public static let didDeactivateApplicationNotification = Notification.Name("NSWorkspaceDidDeactivateApplicationNotification")
    public static let didLaunchApplicationNotification = Notification.Name("NSWorkspaceDidLaunchApplicationNotification")
    public static let willLaunchApplicationNotification = Notification.Name("NSWorkspaceWillLaunchApplicationNotification")
    public static let didTerminateApplicationNotification = Notification.Name("NSWorkspaceDidTerminateApplicationNotification")
    public static let didHideApplicationNotification = Notification.Name("NSWorkspaceDidHideApplicationNotification")
    public static let didUnhideApplicationNotification = Notification.Name("NSWorkspaceDidUnhideApplicationNotification")
    public static let activeSpaceDidChangeNotification = Notification.Name("NSWorkspaceActiveSpaceDidChangeNotification")
    public static let didWakeNotification = Notification.Name("NSWorkspaceDidWakeNotification")
    public static let willSleepNotification = Notification.Name("NSWorkspaceWillSleepNotification")
    public static let screensDidSleepNotification = Notification.Name("NSWorkspaceScreensDidSleepNotification")
    public static let screensDidWakeNotification = Notification.Name("NSWorkspaceScreensDidWakeNotification")
    public static let sessionDidBecomeActiveNotification = Notification.Name("NSWorkspaceSessionDidBecomeActiveNotification")
    public static let sessionDidResignActiveNotification = Notification.Name("NSWorkspaceSessionDidResignActiveNotification")
}

// MARK: Screens and events

public struct NSDeviceDescriptionKey: RawRepresentable, Hashable {
    public var rawValue: String
    public init(rawValue: String) { self.rawValue = rawValue }
    public init(_ rawValue: String) { self.rawValue = rawValue }
    public static let resolution = NSDeviceDescriptionKey("NSDeviceResolution")
    public static let size = NSDeviceDescriptionKey("NSDeviceSize")
    public static let isScreen = NSDeviceDescriptionKey("NSDeviceIsScreen")
}
@preconcurrency @MainActor open class NSScreen: NSObject {
    open class var screens: [NSScreen] { [] }
    open class var main: NSScreen? { nil }
    open var frame: NSRect { .zero }
    open var visibleFrame: NSRect { .zero }
    open var deviceDescription: [NSDeviceDescriptionKey: Any] { [:] }
    open var backingScaleFactor: CGFloat { 1 }
    open var localizedName: String { "" }
    open var safeAreaInsets: NSEdgeInsets { NSEdgeInsets() }
    open var maximumFramesPerSecond: Int { 60 }
    open var colorSpace: NSColorSpace? { nil }
}
open class NSColorSpace: NSObject {}

open class NSEvent: NSObject {
    public enum EventType: UInt {
        case leftMouseDown = 1, leftMouseUp = 2, rightMouseDown = 3, rightMouseUp = 4, mouseMoved = 5, leftMouseDragged = 6,
             rightMouseDragged = 7, mouseEntered = 8, mouseExited = 9, keyDown = 10, keyUp = 11, flagsChanged = 12,
             appKitDefined = 13, systemDefined = 14, applicationDefined = 15, periodic = 16, cursorUpdate = 17,
             scrollWheel = 22, tabletPoint = 23, tabletProximity = 24, otherMouseDown = 25, otherMouseUp = 26,
             otherMouseDragged = 27, gesture = 29, magnify = 30, swipe = 31, rotate = 18, beginGesture = 19, endGesture = 20,
             smartMagnify = 32, quickLook = 33, pressure = 34, directTouch = 37, changeMode = 38
    }
    public struct EventTypeMask: OptionSet { public var rawValue: UInt64; public init(rawValue: UInt64) { self.rawValue = rawValue }
        public static let leftMouseDown = EventTypeMask(rawValue: 1 << 1); public static let leftMouseUp = EventTypeMask(rawValue: 1 << 2)
        public static let rightMouseDown = EventTypeMask(rawValue: 1 << 3); public static let mouseMoved = EventTypeMask(rawValue: 1 << 5)
        public static let leftMouseDragged = EventTypeMask(rawValue: 1 << 6); public static let keyDown = EventTypeMask(rawValue: 1 << 10)
        public static let keyUp = EventTypeMask(rawValue: 1 << 11); public static let flagsChanged = EventTypeMask(rawValue: 1 << 12)
        public static let scrollWheel = EventTypeMask(rawValue: 1 << 22); public static let any = EventTypeMask(rawValue: .max) }
    public struct ModifierFlags: OptionSet, Hashable { public var rawValue: UInt; public init(rawValue: UInt) { self.rawValue = rawValue }
        public static let capsLock = ModifierFlags(rawValue: 1 << 16); public static let shift = ModifierFlags(rawValue: 1 << 17)
        public static let control = ModifierFlags(rawValue: 1 << 18); public static let option = ModifierFlags(rawValue: 1 << 19)
        public static let command = ModifierFlags(rawValue: 1 << 20); public static let numericPad = ModifierFlags(rawValue: 1 << 21)
        public static let help = ModifierFlags(rawValue: 1 << 22); public static let function = ModifierFlags(rawValue: 1 << 23)
        public static let deviceIndependentFlagsMask = ModifierFlags(rawValue: 0xffff0000) }
    open class var keyRepeatDelay: TimeInterval { 0.5 }
    open class var keyRepeatInterval: TimeInterval { 0.1 }
    open class var doubleClickInterval: TimeInterval { 0.5 }
    open class var mouseLocation: NSPoint { .zero }
    open class var modifierFlags: ModifierFlags { [] }
    open class var pressedMouseButtons: Int { 0 }
    open var type: EventType { .mouseMoved }
    open var modifierFlags: ModifierFlags { [] }
    open var locationInWindow: NSPoint { .zero }
    open var window: NSWindow? { nil }
    open var timestamp: TimeInterval { 0 }
    open var clickCount: Int { 0 }
    open var buttonNumber: Int { 0 }
    open var keyCode: UInt16 { 0 }
    open var characters: String? { nil }
    open var charactersIgnoringModifiers: String? { nil }
    open var isARepeat: Bool { false }
    open var scrollingDeltaX: CGFloat { 0 }
    open var scrollingDeltaY: CGFloat { 0 }
    open var cgEvent: CGEvent? { nil }
    open class func addGlobalMonitorForEvents(matching mask: EventTypeMask, handler block: @escaping (NSEvent) -> Void) -> Any? { nil }
    open class func addLocalMonitorForEvents(matching mask: EventTypeMask, handler block: @escaping (NSEvent) -> NSEvent?) -> Any? { nil }
    open class func removeMonitor(_ eventMonitor: Any) {}
}

// MARK: The pasteboard and dragging

public protocol NSPasteboardWriting: NSObjectProtocol {}
public protocol NSPasteboardReading: NSObjectProtocol {}
extension NSURL: NSPasteboardWriting, NSPasteboardReading {}
extension NSString: NSPasteboardWriting, NSPasteboardReading {}

open class NSPasteboard: NSObject {
    public struct Name: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue }
        public init(_ rawValue: String) { self.rawValue = rawValue }
        public static let general = Name("Apple CFPasteboard general"); public static let drag = Name("Apple CFPasteboard drag")
        public static let find = Name("Apple CFPasteboard find"); public static let font = Name("Apple CFPasteboard font")
        public static let ruler = Name("Apple CFPasteboard ruler") }
    public struct PasteboardType: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue }
        public init(_ rawValue: String) { self.rawValue = rawValue }
        public static let string = PasteboardType("public.utf8-plain-text"); public static let png = PasteboardType("public.png")
        public static let tiff = PasteboardType("public.tiff"); public static let rtf = PasteboardType("public.rtf")
        public static let rtfd = PasteboardType("com.apple.flat-rtfd"); public static let html = PasteboardType("public.html")
        public static let pdf = PasteboardType("com.adobe.pdf"); public static let fileURL = PasteboardType("public.file-url")
        public static let URL = PasteboardType("public.url"); public static let color = PasteboardType("com.apple.cocoa.pasteboard.color")
        public static let sound = PasteboardType("com.apple.cocoa.pasteboard.sound"); public static let tabularText = PasteboardType("public.utf8-tab-separated-values-text")
        public static let fileContents = PasteboardType("com.apple.cocoa.pasteboard.file-contents")
        public static let multipleTextSelection = PasteboardType("com.apple.cocoa.pasteboard.multiple-text-selection") }
    public struct ReadingOptionKey: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue }
        public init(_ rawValue: String) { self.rawValue = rawValue }
        public static let urlReadingFileURLsOnly = ReadingOptionKey("NSPasteboardURLReadingFileURLsOnlyKey")
        public static let urlReadingContentsConformToTypes = ReadingOptionKey("NSPasteboardURLReadingContentsConformToTypesKey") }
    public enum AccessBehavior: Int { case `default` = 0, ask = 1, alwaysAllow = 2, alwaysDeny = 3 }
    open class var general: NSPasteboard { fatalError() }
    public init(name: Name) {}
    open class func withUniqueName() -> NSPasteboard { fatalError() }
    open var name: Name { .general }
    open var changeCount: Int { 0 }
    open var types: [PasteboardType]? { nil }
    open var pasteboardItems: [NSPasteboardItem]? { nil }
    @available(macOS 15.4, *) open var accessBehavior: AccessBehavior { .default }
    @discardableResult open func clearContents() -> Int { 0 }
    @discardableResult open func declareTypes(_ newTypes: [PasteboardType], owner newOwner: Any?) -> Int { 0 }
    @discardableResult open func addTypes(_ newTypes: [PasteboardType], owner newOwner: Any?) -> Int { 0 }
    @discardableResult open func writeObjects(_ objects: [NSPasteboardWriting]) -> Bool { false }
    open func readObjects(forClasses classArray: [AnyClass], options: [ReadingOptionKey: Any]? = nil) -> [Any]? { nil }
    open func canReadObject(forClasses classArray: [AnyClass], options: [ReadingOptionKey: Any]? = nil) -> Bool { false }
    open func canReadItem(withDataConformingToTypes types: [String]) -> Bool { false }
    open func availableType(from types: [PasteboardType]) -> PasteboardType? { nil }
    open func string(forType dataType: PasteboardType) -> String? { nil }
    open func data(forType dataType: PasteboardType) -> Data? { nil }
    open func propertyList(forType dataType: PasteboardType) -> Any? { nil }
    @discardableResult open func setString(_ string: String, forType dataType: PasteboardType) -> Bool { false }
    @discardableResult open func setData(_ data: Data?, forType dataType: PasteboardType) -> Bool { false }
    @discardableResult open func setPropertyList(_ plist: Any, forType dataType: PasteboardType) -> Bool { false }
    @discardableResult open func prepareForNewContents(with options: ContentsOptions = []) -> Int { 0 }
    public struct ContentsOptions: OptionSet { public var rawValue: UInt; public init(rawValue: UInt) { self.rawValue = rawValue }
        public static let currentHostOnly = ContentsOptions(rawValue: 1) }
    open func releaseGlobally() {}
}
open class NSPasteboardItem: NSObject, NSPasteboardWriting, NSPasteboardReading {
    public override init() {}
    open var types: [NSPasteboard.PasteboardType] { [] }
    open func availableType(from types: [NSPasteboard.PasteboardType]) -> NSPasteboard.PasteboardType? { nil }
    open func string(forType type: NSPasteboard.PasteboardType) -> String? { nil }
    open func data(forType type: NSPasteboard.PasteboardType) -> Data? { nil }
    open func propertyList(forType type: NSPasteboard.PasteboardType) -> Any? { nil }
    @discardableResult open func setString(_ string: String, forType type: NSPasteboard.PasteboardType) -> Bool { false }
    @discardableResult open func setData(_ data: Data, forType type: NSPasteboard.PasteboardType) -> Bool { false }
    @discardableResult open func setPropertyList(_ propertyList: Any, forType type: NSPasteboard.PasteboardType) -> Bool { false }
}

public struct NSDragOperation: OptionSet { public var rawValue: UInt; public init(rawValue: UInt) { self.rawValue = rawValue }
    public static let copy = NSDragOperation(rawValue: 1); public static let link = NSDragOperation(rawValue: 2)
    public static let generic = NSDragOperation(rawValue: 4); public static let `private` = NSDragOperation(rawValue: 8)
    public static let move = NSDragOperation(rawValue: 16); public static let delete = NSDragOperation(rawValue: 32)
    public static let every = NSDragOperation(rawValue: .max) }
public enum NSDraggingContext: Int { case outsideApplication = 0, withinApplication = 1 }
public enum NSDraggingFormation: Int { case `default` = 0, none, pile, list, stack }
@preconcurrency @MainActor open class NSDraggingItem: NSObject {
    public init(pasteboardWriter: NSPasteboardWriting) {}
    open var item: Any { 0 }
    open var draggingFrame: NSRect = .zero
    open func setDraggingFrame(_ frame: NSRect, contents: Any?) {}
}
@preconcurrency @MainActor open class NSDraggingSession: NSObject {
    open var animatesToStartingPositionsOnCancelOrFail: Bool = true
    open var draggingFormation: NSDraggingFormation = .default
    open var draggingPasteboard: NSPasteboard { fatalError() }
    open var draggingLocation: NSPoint { .zero }
    open var draggingSequenceNumber: Int { 0 }
}
@preconcurrency @MainActor public protocol NSDraggingSource: NSObjectProtocol {
    func draggingSession(_ session: NSDraggingSession, sourceOperationMaskFor context: NSDraggingContext) -> NSDragOperation
    func draggingSession(_ session: NSDraggingSession, willBeginAt screenPoint: NSPoint)
    func draggingSession(_ session: NSDraggingSession, movedTo screenPoint: NSPoint)
    func draggingSession(_ session: NSDraggingSession, endedAt screenPoint: NSPoint, operation: NSDragOperation)
    func ignoreModifierKeys(for session: NSDraggingSession) -> Bool
}
extension NSDraggingSource {
    public func draggingSession(_ session: NSDraggingSession, willBeginAt screenPoint: NSPoint) {}
    public func draggingSession(_ session: NSDraggingSession, movedTo screenPoint: NSPoint) {}
    public func draggingSession(_ session: NSDraggingSession, endedAt screenPoint: NSPoint, operation: NSDragOperation) {}
    public func ignoreModifierKeys(for session: NSDraggingSession) -> Bool { false }
}

// MARK: Cursors, images, color, graphics contexts

@preconcurrency @MainActor open class NSCursor: NSObject {
    public init(image newImage: NSImage, hotSpot point: NSPoint) {}
    open class var currentSystem: NSCursor? { nil }
    open class var current: NSCursor { fatalError() }
    open class var arrow: NSCursor { fatalError() }
    open class var iBeam: NSCursor { fatalError() }
    open class var pointingHand: NSCursor { fatalError() }
    open class var crosshair: NSCursor { fatalError() }
    open class var openHand: NSCursor { fatalError() }
    open class var closedHand: NSCursor { fatalError() }
    open var image: NSImage { fatalError() }
    open var hotSpot: NSPoint { .zero }
    open func set() {}
    open func push() {}
    open func pop() {}
    open class func hide() {}
    open class func unhide() {}
}

open class NSImageRep: NSObject {
    open var size: NSSize = .zero
    open var pixelsWide: Int = 0
    open var pixelsHigh: Int = 0
    open var hasAlpha: Bool = false
    open var isOpaque: Bool = false
    @discardableResult open func draw() -> Bool { false }
    @discardableResult open func draw(in rect: NSRect) -> Bool { false }
    open func cgImage(forProposedRect proposedDestRect: UnsafeMutablePointer<NSRect>?, context: NSGraphicsContext?, hints: [NSImageRep.HintKey: Any]?) -> CGImage? { nil }
    public struct HintKey: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue }
        public static let interpolation = HintKey(rawValue: "NSImageHintInterpolation"); public static let ctm = HintKey(rawValue: "NSImageHintCTM") }
}
public struct NSColorSpaceName: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue }
    public init(_ rawValue: String) { self.rawValue = rawValue }
    public static let calibratedRGB = NSColorSpaceName("NSCalibratedRGBColorSpace"); public static let deviceRGB = NSColorSpaceName("NSDeviceRGBColorSpace")
    public static let calibratedWhite = NSColorSpaceName("NSCalibratedWhiteColorSpace"); public static let deviceWhite = NSColorSpaceName("NSDeviceWhiteColorSpace") }
open class NSBitmapImageRep: NSImageRep {
    public enum FileType: UInt { case tiff = 0, bmp, gif, jpeg, png, jpeg2000 }
    public struct PropertyKey: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue }
        public static let compressionFactor = PropertyKey(rawValue: "NSImageCompressionFactor"); public static let interlaced = PropertyKey(rawValue: "NSImageInterlaced") }
    public struct Format: OptionSet { public var rawValue: UInt; public init(rawValue: UInt) { self.rawValue = rawValue }
        public static let alphaFirst = Format(rawValue: 1); public static let alphaNonpremultiplied = Format(rawValue: 2) }
    public init(cgImage: CGImage) {}
    public init?(data: Data) {}
    public init?(bitmapDataPlanes planes: UnsafeMutablePointer<UnsafeMutablePointer<UInt8>?>?, pixelsWide width: Int, pixelsHigh height: Int,
                 bitsPerSample bps: Int, samplesPerPixel spp: Int, hasAlpha alpha: Bool, isPlanar: Bool, colorSpaceName: NSColorSpaceName,
                 bytesPerRow rBytes: Int, bitsPerPixel pBits: Int) {}
    public init?(bitmapDataPlanes planes: UnsafeMutablePointer<UnsafeMutablePointer<UInt8>?>?, pixelsWide width: Int, pixelsHigh height: Int,
                 bitsPerSample bps: Int, samplesPerPixel spp: Int, hasAlpha alpha: Bool, isPlanar: Bool, colorSpaceName: NSColorSpaceName,
                 bitmapFormat: Format, bytesPerRow rBytes: Int, bitsPerPixel pBits: Int) {}
    open func representation(using storageType: FileType, properties: [PropertyKey: Any]) -> Data? { nil }
    open var cgImage: CGImage? { nil }
    open var bitmapData: UnsafeMutablePointer<UInt8>? { nil }
    open var bytesPerRow: Int { 0 }
    open var bitsPerPixel: Int { 0 }
    open var tiffRepresentation: Data? { nil }
}
open class NSImage: NSObject {
    public struct Name: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue }
        public init(_ rawValue: String) { self.rawValue = rawValue }
        public static let applicationIcon = Name("NSApplicationIcon"); public static let caution = Name("NSCaution") }
    public init(size: NSSize) {}
    public init(cgImage: CGImage, size: NSSize) {}
    public init?(contentsOf url: URL) {}
    public init?(contentsOfFile fileName: String) {}
    public init?(data: Data) {}
    public init?(named name: Name) {}
    public init?(systemSymbolName symbolName: String, accessibilityDescription description: String?) {}
    public convenience init(size: NSSize, flipped drawingHandlerShouldBeCalledWithFlippedContext: Bool, drawingHandler: @escaping (NSRect) -> Bool) { self.init(size: size) }
    open var size: NSSize = .zero
    open var isTemplate: Bool = false
    open var representations: [NSImageRep] { [] }
    open var tiffRepresentation: Data? { nil }
    open var accessibilityDescription: String?
    open func addRepresentation(_ imageRep: NSImageRep) {}
    open func cgImage(forProposedRect proposedDestRect: UnsafeMutablePointer<NSRect>?, context referenceContext: NSGraphicsContext?, hints: [NSImageRep.HintKey: Any]?) -> CGImage? { nil }
    open func lockFocus() {}
    open func unlockFocus() {}
    open func draw(in rect: NSRect) {}
    open func draw(in dstSpacePortionRect: NSRect, from srcSpacePortionRect: NSRect, operation op: NSCompositingOperation, fraction requestedAlpha: CGFloat) {}
    open func draw(at point: NSPoint, from fromRect: NSRect, operation op: NSCompositingOperation, fraction delta: CGFloat) {}
    open func withSymbolConfiguration(_ configuration: SymbolConfiguration) -> NSImage? { nil }
    open class SymbolConfiguration: NSObject {
        public init(pointSize: CGFloat, weight: NSFont.Weight) {}
        public init(scale: NSImage.SymbolScale) {}
        public init(paletteColors: [NSColor]) {}
        public init(hierarchicalColor: NSColor) {}
        open func applying(_ configuration: SymbolConfiguration) -> SymbolConfiguration { self }
    }
    public enum SymbolScale: Int { case `default` = 0, small, medium, large }
}
public enum NSCompositingOperation: UInt { case clear = 0, copy, sourceOver, sourceIn, sourceOut, sourceAtop, destinationOver,
    destinationIn, destinationOut, destinationAtop, xor, plusDarker, plusLighter }

open class NSColor: NSObject {
    open class var black: NSColor { fatalError() }
    open class var white: NSColor { fatalError() }
    open class var clear: NSColor { fatalError() }
    open class var red: NSColor { fatalError() }
    open class var green: NSColor { fatalError() }
    open class var blue: NSColor { fatalError() }
    open class var orange: NSColor { fatalError() }
    open class var yellow: NSColor { fatalError() }
    open class var gray: NSColor { fatalError() }
    open class var labelColor: NSColor { fatalError() }
    open class var secondaryLabelColor: NSColor { fatalError() }
    open class var tertiaryLabelColor: NSColor { fatalError() }
    open class var quaternaryLabelColor: NSColor { fatalError() }
    open class var controlAccentColor: NSColor { fatalError() }
    open class var controlBackgroundColor: NSColor { fatalError() }
    open class var windowBackgroundColor: NSColor { fatalError() }
    open class var textBackgroundColor: NSColor { fatalError() }
    open class var textColor: NSColor { fatalError() }
    open class var separatorColor: NSColor { fatalError() }
    open class var systemRed: NSColor { fatalError() }
    open class var systemGreen: NSColor { fatalError() }
    open class var systemOrange: NSColor { fatalError() }
    open class var systemYellow: NSColor { fatalError() }
    open class var systemBlue: NSColor { fatalError() }
    open class var systemGray: NSColor { fatalError() }
    public init(calibratedWhite white: CGFloat, alpha: CGFloat) {}
    public init(white: CGFloat, alpha: CGFloat) {}
    public init(red: CGFloat, green: CGFloat, blue: CGFloat, alpha: CGFloat) {}
    public init(srgbRed red: CGFloat, green: CGFloat, blue: CGFloat, alpha: CGFloat) {}
    public init(calibratedRed red: CGFloat, green: CGFloat, blue: CGFloat, alpha: CGFloat) {}
    public init(name: NSColor.Name?, dynamicProvider: @escaping (NSAppearance) -> NSColor) {}
    public init?(cgColor: CGColor) {}
    public typealias Name = String
    open var cgColor: CGColor { fatalError() }
    open var alphaComponent: CGFloat { 1 }
    open func withAlphaComponent(_ alpha: CGFloat) -> NSColor { self }
    open func blended(withFraction fraction: CGFloat, of color: NSColor) -> NSColor? { nil }
    open func setFill() {}
    open func setStroke() {}
    open func set() {}
}

public enum NSImageInterpolation: UInt { case `default` = 0, none = 1, low = 2, medium = 4, high = 3 }
open class NSGraphicsContext: NSObject {
    public init?(bitmapImageRep bitmapRep: NSBitmapImageRep) {}
    public init(cgContext graphicsPort: CGContext, flipped initialFlippedState: Bool) {}
    open class var current: NSGraphicsContext? { get { nil } set {} }
    open class func saveGraphicsState() {}
    open class func restoreGraphicsState() {}
    open var cgContext: CGContext { fatalError() }
    open var imageInterpolation: NSImageInterpolation = .default
    open var shouldAntialias: Bool = true
    open func flushGraphics() {}
}

// MARK: Windows, views, menus

open class NSView: NSResponder {
    public init(frame frameRect: NSRect) {}
    public required init?(coder: NSCoder) {}
    public override convenience init() { self.init(frame: .zero) }
    open var appearance: NSAppearance?
    open var effectiveAppearance: NSAppearance { fatalError() }
    open var enclosingScrollView: NSScrollView? { nil }
    open func setContentHuggingPriority(_ priority: NSLayoutConstraint.Priority, for orientation: NSLayoutConstraint.Orientation) {}
    open func setContentCompressionResistancePriority(_ priority: NSLayoutConstraint.Priority, for orientation: NSLayoutConstraint.Orientation) {}
    open func bitmapImageRepForCachingDisplay(in rect: NSRect) -> NSBitmapImageRep? { nil }
    open func cacheDisplay(in rect: NSRect, to bitmapImageRep: NSBitmapImageRep) {}
    open func display() {}
    open func setNeedsDisplay(_ invalidRect: NSRect) {}
    open func scroll(_ point: NSPoint) {}
    open func setAccessibilityTitle(_ accessibilityTitle: String?) {}
    open func setAccessibilityRole(_ accessibilityRole: NSAccessibility.Role?) {}
    open var frame: NSRect = .zero
    open var bounds: NSRect = .zero
    open var window: NSWindow? { nil }
    open var superview: NSView? { nil }
    open var subviews: [NSView] = []
    open var isHidden: Bool = false
    open var wantsLayer: Bool = false
    open var layer: CALayer?
    open var alphaValue: CGFloat = 1
    open var needsDisplay: Bool = false
    open var needsLayout: Bool = false
    open var translatesAutoresizingMaskIntoConstraints: Bool = true
    open var autoresizingMask: AutoresizingMask = []
    open var isFlipped: Bool { false }
    open var fittingSize: NSSize { .zero }
    open var intrinsicContentSize: NSSize { .zero }
    open var toolTip: String?
    public struct AutoresizingMask: OptionSet { public var rawValue: UInt; public init(rawValue: UInt) { self.rawValue = rawValue }
        public static let width = AutoresizingMask(rawValue: 2); public static let height = AutoresizingMask(rawValue: 16) }
    open func addSubview(_ view: NSView) {}
    open func removeFromSuperview() {}
    open func draw(_ dirtyRect: NSRect) {}
    open func layout() {}
    open func updateConstraints() {}
    open func setFrameSize(_ newSize: NSSize) {}
    open func acceptsFirstMouse(for event: NSEvent?) -> Bool { false }
    open func hitTest(_ point: NSPoint) -> NSView? { nil }
    open func convert(_ point: NSPoint, from view: NSView?) -> NSPoint { point }
    open func convert(_ point: NSPoint, to view: NSView?) -> NSPoint { point }
    open func convert(_ rect: NSRect, to view: NSView?) -> NSRect { rect }
    open func viewDidMoveToWindow() {}
    open func beginDraggingSession(with items: [NSDraggingItem], event: NSEvent, source: NSDraggingSource) -> NSDraggingSession { fatalError() }
    open func layoutSubtreeIfNeeded() {}
    open var leadingAnchor: NSLayoutXAxisAnchor { fatalError() }
    open var trailingAnchor: NSLayoutXAxisAnchor { fatalError() }
    open var topAnchor: NSLayoutYAxisAnchor { fatalError() }
    open var bottomAnchor: NSLayoutYAxisAnchor { fatalError() }
    open var widthAnchor: NSLayoutDimension { fatalError() }
    open var heightAnchor: NSLayoutDimension { fatalError() }
    open var centerXAnchor: NSLayoutXAxisAnchor { fatalError() }
    open var centerYAnchor: NSLayoutYAxisAnchor { fatalError() }
    open var constraints: [NSLayoutConstraint] { [] }
    open func addConstraint(_ constraint: NSLayoutConstraint) {}
    open func removeConstraint(_ constraint: NSLayoutConstraint) {}
    open func setAccessibilityElement(_ accessibilityElement: Bool) {}
    open func setAccessibilityLabel(_ accessibilityLabel: String?) {}
}
open class NSLayoutAnchor<AnchorType: AnyObject>: NSObject {
    open func constraint(equalTo anchor: NSLayoutAnchor<AnchorType>) -> NSLayoutConstraint { fatalError() }
    open func constraint(equalTo anchor: NSLayoutAnchor<AnchorType>, constant c: CGFloat) -> NSLayoutConstraint { fatalError() }
    open func constraint(lessThanOrEqualTo anchor: NSLayoutAnchor<AnchorType>, constant c: CGFloat) -> NSLayoutConstraint { fatalError() }
    open func constraint(greaterThanOrEqualTo anchor: NSLayoutAnchor<AnchorType>, constant c: CGFloat) -> NSLayoutConstraint { fatalError() }
}
open class NSLayoutXAxisAnchor: NSLayoutAnchor<NSLayoutXAxisAnchor> {}
open class NSLayoutYAxisAnchor: NSLayoutAnchor<NSLayoutYAxisAnchor> {}
open class NSLayoutDimension: NSLayoutAnchor<NSLayoutDimension> {
    open func constraint(equalToConstant c: CGFloat) -> NSLayoutConstraint { fatalError() }
    open func constraint(lessThanOrEqualToConstant c: CGFloat) -> NSLayoutConstraint { fatalError() }
    open func constraint(greaterThanOrEqualToConstant c: CGFloat) -> NSLayoutConstraint { fatalError() }
}
@preconcurrency @MainActor open class NSLayoutConstraint: NSObject {
    public struct Priority: RawRepresentable, Hashable, ExpressibleByFloatLiteral, ExpressibleByIntegerLiteral {
        public var rawValue: Float; public init(rawValue: Float) { self.rawValue = rawValue }; public init(_ rawValue: Float) { self.rawValue = rawValue }
        public init(floatLiteral value: Float) { rawValue = value }; public init(integerLiteral value: Int) { rawValue = Float(value) }
        public static let required = Priority(1000); public static let defaultHigh = Priority(750); public static let defaultLow = Priority(250)
        public static let dragThatCanResizeWindow = Priority(510); public static let windowSizeStayPut = Priority(500)
        public static let fittingSizeCompression = Priority(50) }
    public enum Orientation: Int { case horizontal = 0, vertical = 1 }
    public enum Attribute: Int { case left = 1, right, top, bottom, leading, trailing, width, height, centerX, centerY, lastBaseline, firstBaseline, notAnAttribute = 0 }
    public enum Relation: Int { case lessThanOrEqual = -1, equal = 0, greaterThanOrEqual = 1 }
    public convenience init(item view1: Any, attribute attr1: Attribute, relatedBy relation: Relation, toItem view2: Any?, attribute attr2: Attribute, multiplier: CGFloat, constant c: CGFloat) { self.init() }
    open var isActive: Bool = false
    open var constant: CGFloat = 0
    open var priority: Priority = .required
    open var identifier: String?
    open var firstItem: AnyObject? { nil }
    open var firstAttribute: Attribute { .notAnAttribute }
    open class func activate(_ constraints: [NSLayoutConstraint]) {}
    open class func deactivate(_ constraints: [NSLayoutConstraint]) {}
}

open class NSWindow: NSResponder {
    public struct StyleMask: OptionSet { public var rawValue: UInt; public init(rawValue: UInt) { self.rawValue = rawValue }
        public static let borderless = StyleMask([]); public static let titled = StyleMask(rawValue: 1)
        public static let closable = StyleMask(rawValue: 2); public static let miniaturizable = StyleMask(rawValue: 4)
        public static let resizable = StyleMask(rawValue: 8); public static let unifiedTitleAndToolbar = StyleMask(rawValue: 1 << 12)
        public static let fullScreen = StyleMask(rawValue: 1 << 14); public static let fullSizeContentView = StyleMask(rawValue: 1 << 15)
        public static let utilityWindow = StyleMask(rawValue: 1 << 4); public static let docModalWindow = StyleMask(rawValue: 1 << 6)
        public static let nonactivatingPanel = StyleMask(rawValue: 1 << 7); public static let hudWindow = StyleMask(rawValue: 1 << 13) }
    public enum BackingStoreType: UInt { case retained = 0, nonretained = 1, buffered = 2 }
    public struct Level: RawRepresentable, Hashable, Comparable { public var rawValue: Int; public init(rawValue: Int) { self.rawValue = rawValue }
        public init(_ rawValue: Int) { self.rawValue = rawValue }
        public static let normal = Level(0); public static let floating = Level(3); public static let submenu = Level(3)
        public static let tornOffMenu = Level(3); public static let modalPanel = Level(8); public static let mainMenu = Level(24)
        public static let statusBar = Level(25); public static let popUpMenu = Level(101); public static let screenSaver = Level(1000)
        public static func < (a: Level, b: Level) -> Bool { a.rawValue < b.rawValue } }
    public struct CollectionBehavior: OptionSet { public var rawValue: UInt; public init(rawValue: UInt) { self.rawValue = rawValue }
        public static let canJoinAllSpaces = CollectionBehavior(rawValue: 1); public static let moveToActiveSpace = CollectionBehavior(rawValue: 2)
        public static let managed = CollectionBehavior(rawValue: 4); public static let transient = CollectionBehavior(rawValue: 8)
        public static let stationary = CollectionBehavior(rawValue: 16); public static let participatesInCycle = CollectionBehavior(rawValue: 32)
        public static let ignoresCycle = CollectionBehavior(rawValue: 64); public static let fullScreenPrimary = CollectionBehavior(rawValue: 128)
        public static let fullScreenAuxiliary = CollectionBehavior(rawValue: 256); public static let fullScreenNone = CollectionBehavior(rawValue: 512)
        public static let fullScreenAllowsTiling = CollectionBehavior(rawValue: 1 << 11); public static let fullScreenDisallowsTiling = CollectionBehavior(rawValue: 1 << 12)
        public static let canJoinAllApplications = CollectionBehavior(rawValue: 1 << 18) }
    public enum SharingType: UInt { case none = 0, readOnly = 1, readWrite = 2 }
    public enum AnimationBehavior: Int { case `default` = 0, none = 2, documentWindow = 3, utilityWindow = 4, alertPanel = 5 }
    public enum TitleVisibility: Int { case visible = 0, hidden = 1 }
    public enum ButtonType: UInt { case closeButton, miniaturizeButton, zoomButton, toolbarButton, documentIconButton, documentVersionsButton }
    public enum ToolbarStyle: Int { case automatic, expanded, preference, unified, unifiedCompact }
    public init(contentRect: NSRect, styleMask style: StyleMask, backing backingStoreType: BackingStoreType, defer flag: Bool) {}
    public init(contentRect: NSRect, styleMask style: StyleMask, backing backingStoreType: BackingStoreType, defer flag: Bool, screen: NSScreen?) {}
    public convenience init(contentViewController: NSViewController) { self.init(contentRect: .zero, styleMask: [], backing: .buffered, defer: false) }
    open var contentView: NSView?
    open var contentViewController: NSViewController?
    open var windowController: NSWindowController?
    open weak var delegate: NSWindowDelegate?
    open var title: String = ""
    open var subtitle: String = ""
    open var frame: NSRect { .zero }
    open var screen: NSScreen? { nil }
    open var level: Level = .normal
    open var styleMask: StyleMask = []
    open var collectionBehavior: CollectionBehavior = []
    open var sharingType: SharingType = .readOnly
    open var animationBehavior: AnimationBehavior = .default
    open var titleVisibility: TitleVisibility = .visible
    open var titlebarAppearsTransparent: Bool = false
    open var toolbarStyle: ToolbarStyle = .automatic
    open var toolbar: NSToolbar?
    open var backgroundColor: NSColor!
    open var isOpaque: Bool = true
    open var hasShadow: Bool = true
    open var alphaValue: CGFloat = 1
    open var ignoresMouseEvents: Bool = false
    open var isReleasedWhenClosed: Bool = true
    open var isMovableByWindowBackground: Bool = false
    open var hidesOnDeactivate: Bool = false
    open var canHide: Bool = true
    open var isRestorable: Bool = true
    open var isVisible: Bool { false }
    open var isKeyWindow: Bool { false }
    open var isMainWindow: Bool { false }
    open var isMiniaturized: Bool { false }
    open var occlusionState: OcclusionState { [] }
    public struct OcclusionState: OptionSet { public var rawValue: UInt; public init(rawValue: UInt) { self.rawValue = rawValue }
        public static let visible = OcclusionState(rawValue: 2) }
    open var windowNumber: Int { 0 }
    open var appearance: NSAppearance?
    open var effectiveAppearance: NSAppearance { fatalError() }
    open var initialFirstResponder: NSView?
    open var firstResponder: NSResponder? { nil }
    open var contentMinSize: NSSize = .zero
    open var contentMaxSize: NSSize = .zero
    open var minSize: NSSize = .zero
    open var maxSize: NSSize = .zero
    open var frameAutosaveName: String { "" }
    open var tabbingMode: TabbingMode = .automatic
    public enum TabbingMode: Int { case automatic, preferred, disallowed }
    open var identifier: NSUserInterfaceItemIdentifier?
    open func setFrame(_ frameRect: NSRect, display flag: Bool) {}
    open func setFrame(_ frameRect: NSRect, display displayFlag: Bool, animate animateFlag: Bool) {}
    open func setFrameOrigin(_ point: NSPoint) {}
    open func setFrameTopLeftPoint(_ point: NSPoint) {}
    open func setContentSize(_ size: NSSize) {}
    @discardableResult open func setFrameAutosaveName(_ name: String) -> Bool { true }
    open func setFrameUsingName(_ name: String) -> Bool { false }
    open func frameRect(forContentRect contentRect: NSRect) -> NSRect { contentRect }
    open func contentRect(forFrameRect frameRect: NSRect) -> NSRect { frameRect }
    open func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect { frameRect }
    open func center() {}
    open func makeKeyAndOrderFront(_ sender: Any?) {}
    open func makeKey() {}
    open func makeMain() {}
    open func orderFront(_ sender: Any?) {}
    open func orderFrontRegardless() {}
    open func orderBack(_ sender: Any?) {}
    open func orderOut(_ sender: Any?) {}
    open func order(_ place: NSWindow.OrderingMode, relativeTo otherWin: Int) {}
    public enum OrderingMode: Int { case above = 1, below = -1, out = 0 }
    open func close() {}
    open func performClose(_ sender: Any?) {}
    open func miniaturize(_ sender: Any?) {}
    open func performMiniaturize(_ sender: Any?) {}
    open func performZoom(_ sender: Any?) {}
    open func toggleFullScreen(_ sender: Any?) {}
    open func deminiaturize(_ sender: Any?) {}
    @discardableResult open func makeFirstResponder(_ responder: NSResponder?) -> Bool { true }
    open func standardWindowButton(_ b: ButtonType) -> NSButton? { nil }
    open func invalidateShadow() {}
    open func display() {}
    open func layoutIfNeeded() {}
    open func beginSheet(_ sheetWindow: NSWindow, completionHandler handler: ((NSApplication.ModalResponse) -> Void)? = nil) {}
    open func endSheet(_ sheetWindow: NSWindow) {}
    open func convertPoint(toScreen point: NSPoint) -> NSPoint { point }
    open func convertToScreen(_ rect: NSRect) -> NSRect { rect }
    open func addChildWindow(_ childWin: NSWindow, ordered place: OrderingMode) {}
    open func removeChildWindow(_ childWin: NSWindow) {}
    public static let didResizeNotification = Notification.Name("NSWindowDidResizeNotification")
    public static let didMoveNotification = Notification.Name("NSWindowDidMoveNotification")
    public static let willCloseNotification = Notification.Name("NSWindowWillCloseNotification")
    public static let didBecomeKeyNotification = Notification.Name("NSWindowDidBecomeKeyNotification")
    public static let didResignKeyNotification = Notification.Name("NSWindowDidResignKeyNotification")
    public static let didChangeScreenNotification = Notification.Name("NSWindowDidChangeScreenNotification")
    public static let didChangeOcclusionStateNotification = Notification.Name("NSWindowDidChangeOcclusionStateNotification")
}
public struct NSUserInterfaceItemIdentifier: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue }
    public init(_ rawValue: String) { self.rawValue = rawValue } }
@preconcurrency @MainActor public protocol NSWindowDelegate: NSObjectProtocol {
    func windowWillClose(_ notification: Notification)
    func windowShouldClose(_ sender: NSWindow) -> Bool
    func windowDidResize(_ notification: Notification)
    func windowDidMove(_ notification: Notification)
    func windowDidBecomeKey(_ notification: Notification)
    func windowDidResignKey(_ notification: Notification)
    func windowDidChangeScreen(_ notification: Notification)
    func windowDidChangeBackingProperties(_ notification: Notification)
    func windowWillResize(_ sender: NSWindow, to frameSize: NSSize) -> NSSize
}
extension NSWindowDelegate {
    public func windowWillClose(_ notification: Notification) {}
    public func windowShouldClose(_ sender: NSWindow) -> Bool { true }
    public func windowDidResize(_ notification: Notification) {}
    public func windowDidMove(_ notification: Notification) {}
    public func windowDidBecomeKey(_ notification: Notification) {}
    public func windowDidResignKey(_ notification: Notification) {}
    public func windowDidChangeScreen(_ notification: Notification) {}
    public func windowDidChangeBackingProperties(_ notification: Notification) {}
    public func windowWillResize(_ sender: NSWindow, to frameSize: NSSize) -> NSSize { frameSize }
}
open class NSPanel: NSWindow {
    open var becomesKeyOnlyIfNeeded: Bool = false
    open var isFloatingPanel: Bool = false
    open var worksWhenModal: Bool = false
}
open class NSToolbar: NSObject {}
open class NSViewController: NSResponder {
    public init(nibName nibNameOrNil: String?, bundle nibBundleOrNil: Bundle?) {}
    public required init?(coder: NSCoder) {}
    open var view: NSView { get { fatalError() } set {} }
    open var title: String?
    open var preferredContentSize: NSSize = .zero
    open var representedObject: Any?
    open func loadView() {}
    open func viewDidLoad() {}
    open func viewWillAppear() {}
    open func viewDidAppear() {}
    open func viewWillDisappear() {}
    open func viewDidDisappear() {}
    open func viewDidLayout() {}
    open var children: [NSViewController] = []
    open func addChild(_ childViewController: NSViewController) {}
}
open class NSWindowController: NSResponder {
    public init(window: NSWindow?) {}
    public required init?(coder: NSCoder) {}
    open var window: NSWindow?
    open var contentViewController: NSViewController?
    open func showWindow(_ sender: Any?) {}
    open func close() {}
    open func windowDidLoad() {}
}
open class NSControl: NSView {
    public struct StateValue: RawRepresentable, Hashable { public var rawValue: Int; public init(rawValue: Int) { self.rawValue = rawValue }
        public init(_ rawValue: Int) { self.rawValue = rawValue }
        public static let off = StateValue(0); public static let on = StateValue(1); public static let mixed = StateValue(-1) }
    public enum ControlSize: UInt { case regular = 0, small = 1, mini = 2, large = 3 }
    open weak var target: AnyObject?
    open var action: Selector?
    open var isEnabled: Bool = true
    open var stringValue: String = ""
    open var integerValue: Int = 0
    open var doubleValue: Double = 0
    open var font: NSFont?
    open var alignment: NSTextAlignment = .natural
    open var controlSize: ControlSize = .regular
    open func sizeToFit() {}
    @discardableResult open func sendAction(_ action: Selector?, to target: Any?) -> Bool { false }
}
public enum NSTextAlignment: Int { case left = 0, right = 1, center = 2, justified = 3, natural = 4 }
open class NSButton: NSControl {
    public enum BezelStyle: UInt { case automatic = 0, push = 1, flexiblePush = 2, disclosure = 5, circular = 7, helpButton = 9,
        smallSquare = 10, toolbar = 11, accessoryBarAction = 12, accessoryBar = 13, pushDisclosure = 14, badge = 15
        public static var rounded: BezelStyle { .push }; public static var regularSquare: BezelStyle { .flexiblePush } }
    public enum ButtonType: UInt { case momentaryLight = 0, pushOnPushOff = 1, toggle = 2, `switch` = 3, radio = 4, momentaryChange = 5, onOff = 6, momentaryPushIn = 7 }
    public convenience init(title: String, target: Any?, action: Selector?) { self.init(frame: .zero) }
    public convenience init(checkboxWithTitle title: String, target: Any?, action: Selector?) { self.init(frame: .zero) }
    public convenience init(radioButtonWithTitle title: String, target: Any?, action: Selector?) { self.init(frame: .zero) }
    public convenience init(image: NSImage, target: Any?, action: Selector?) { self.init(frame: .zero) }
    public convenience init(title: String, image: NSImage, target: Any?, action: Selector?) { self.init(frame: .zero) }
    open var title: String = ""
    open var image: NSImage?
    open var state: NSControl.StateValue = .off
    open var bezelStyle: BezelStyle = .automatic
    open var isBordered: Bool = true
    open var imagePosition: NSControl.ImagePosition = .noImage
    open var keyEquivalent: String = ""
    open var appearsDisabled: Bool = false
    open func setButtonType(_ type: ButtonType) {}
    open func performClick(_ sender: Any?) {}
}
extension NSControl {
    public enum ImagePosition: UInt { case noImage = 0, imageOnly = 1, imageLeft = 2, imageRight = 3, imageBelow = 4, imageAbove = 5, imageOverlaps = 6, imageLeading = 7, imageTrailing = 8 }
}
open class NSStatusBarButton: NSButton {}
@preconcurrency @MainActor open class NSStatusBar: NSObject {
    open class var system: NSStatusBar { fatalError() }
    open func statusItem(withLength length: CGFloat) -> NSStatusItem { fatalError() }
    open func removeStatusItem(_ item: NSStatusItem) {}
    open var thickness: CGFloat { 24 }
}
@preconcurrency @MainActor open class NSStatusItem: NSObject {
    public struct Behavior: OptionSet { public var rawValue: UInt; public init(rawValue: UInt) { self.rawValue = rawValue }
        public static let removalAllowed = Behavior(rawValue: 2); public static let terminationOnRemoval = Behavior(rawValue: 4) }
    public static let variableLength: CGFloat = -1
    public static let squareLength: CGFloat = -2
    open var button: NSStatusBarButton? { nil }
    open var menu: NSMenu?
    open var length: CGFloat = 0
    open var isVisible: Bool = true
    open var behavior: Behavior = []
    open var autosaveName: String! = ""
    open var statusBar: NSStatusBar? { nil }
}
open class NSText: NSView {
    open var string: String = ""
    open var font: NSFont?
    open var textColor: NSColor?
    open var backgroundColor: NSColor?
    open var drawsBackground: Bool = true
    open var isEditable: Bool = true
    open var isSelectable: Bool = true
    open var isRichText: Bool = true
    open var isFieldEditor: Bool = false
    open var alignment: NSTextAlignment = .natural
    open func cut(_ sender: Any?) {}
    open func copy(_ sender: Any?) {}
    open func paste(_ sender: Any?) {}
    open func selectAll(_ sender: Any?) {}
    open func delete(_ sender: Any?) {}
    open func scrollRangeToVisible(_ range: NSRange) {}
}
public enum NSFindPanelAction: UInt { case showFindPanel = 1, next = 2, previous = 3, replaceAll = 4, replace = 5, replaceAndFind = 6,
    setFindString = 7, replaceAllInSelection = 8, selectAll = 9, selectAllInSelection = 10 }
open class NSTextStorage: NSMutableAttributedString {}
open class NSTextContainer: NSObject { open var widthTracksTextView: Bool = true; open var containerSize: NSSize = .zero; open var lineFragmentPadding: CGFloat = 5 }
open class NSLayoutManager: NSObject {}
open class NSTextView: NSText {
    open class func scrollableTextView() -> NSScrollView { fatalError() }
    open var textStorage: NSTextStorage? { nil }
    open var textContainer: NSTextContainer?
    open var layoutManager: NSLayoutManager? { nil }
    open var textContainerInset: NSSize = .zero
    open var usesFindBar: Bool = false
    open var isIncrementalSearchingEnabled: Bool = false
    open var isAutomaticQuoteSubstitutionEnabled: Bool = false
    open var isAutomaticSpellingCorrectionEnabled: Bool = false
    open var allowsUndo: Bool = false
    open var typingAttributes: [NSAttributedString.Key: Any] = [:]
    open func performFindPanelAction(_ sender: Any?) {}
    open func scrollToEndOfDocument(_ sender: Any?) {}
    open func setSelectedRange(_ charRange: NSRange) {}
    open func selectedRange() -> NSRange { NSRange(location: 0, length: 0) }
}
open class NSClipView: NSView {
    open var documentView: NSView?
    open var documentVisibleRect: NSRect { .zero }
    open func scroll(to newOrigin: NSPoint) {}
}
open class NSScrollView: NSView {
    open var documentView: NSView?
    open var contentView: NSClipView { fatalError() }
    open var hasVerticalScroller: Bool = false
    open var hasHorizontalScroller: Bool = false
    open var autohidesScrollers: Bool = false
    open var drawsBackground: Bool = true
    open var borderType: NSBorderType = .noBorder
    open var documentVisibleRect: NSRect { .zero }
    open func reflectScrolledClipView(_ cView: NSClipView) {}
}
public enum NSBorderType: UInt { case noBorder = 0, lineBorder = 1, bezelBorder = 2, grooveBorder = 3 }
public enum NSUserInterfaceLayoutOrientation: Int { case horizontal = 0, vertical = 1 }
open class NSStackView: NSView {
    public enum Distribution: Int { case gravityAreas = -1, fill = 0, fillEqually, fillProportionally, equalSpacing, equalCentering }
    public enum Gravity: Int { case top = 1, center = 2, bottom = 3
        public static var leading: Gravity { .top }; public static var trailing: Gravity { .bottom } }
    public convenience init(views: [NSView]) { self.init(frame: .zero) }
    open var orientation: NSUserInterfaceLayoutOrientation = .horizontal
    open var edgeInsets: NSEdgeInsets = NSEdgeInsets()
    open var spacing: CGFloat = 8
    open var alignment: NSLayoutConstraint.Attribute = .centerY
    open var distribution: Distribution = .gravityAreas
    open var views: [NSView] { [] }
    open var arrangedSubviews: [NSView] { [] }
    open func addArrangedSubview(_ view: NSView) {}
    open func addView(_ view: NSView, in gravity: Gravity) {}
}
open class NSTabView: NSView {
    open var tabViewItems: [NSTabViewItem] { [] }
    open var selectedTabViewItem: NSTabViewItem? { nil }
}
open class NSTabViewItem: NSObject {
    public init(identifier: Any?) {}
    public convenience init(viewController: NSViewController) { self.init(identifier: nil) }
    open var label: String = ""
    open var image: NSImage?
    open var identifier: Any?
    open var viewController: NSViewController?
    open var view: NSView?
    open var toolTip: String?
}
open class NSTabViewController: NSViewController {
    public enum TabStyle: Int { case segmentedControlOnTop = 0, segmentedControlOnBottom, toolbar, unspecified = -1 }
    open var tabStyle: TabStyle = .segmentedControlOnTop
    open var tabView: NSTabView { fatalError() }
    open var tabViewItems: [NSTabViewItem] = []
    open var selectedTabViewItemIndex: Int = 0
    open var canPropagateSelectedChildViewControllerTitle: Bool = true
    open func addTabViewItem(_ tabViewItem: NSTabViewItem) {}
    open func insertTabViewItem(_ tabViewItem: NSTabViewItem, at index: Int) {}
    open func removeTabViewItem(_ tabViewItem: NSTabViewItem) {}
    open func tabViewItem(for viewController: NSViewController) -> NSTabViewItem? { nil }
    open func tabView(_ tabView: NSTabView, willSelect tabViewItem: NSTabViewItem?) {}
    open func tabView(_ tabView: NSTabView, didSelect tabViewItem: NSTabViewItem?) {}
    open func tabView(_ tabView: NSTabView, shouldSelect tabViewItem: NSTabViewItem?) -> Bool { true }
}

// MARK: Accessibility (NSAccessibility.h) and text attributes

public enum NSAccessibility {
    public struct Notification: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue }
        public static let announcementRequested = Notification(rawValue: "AXAnnouncementRequested")
        public static let layoutChanged = Notification(rawValue: "AXLayoutChanged")
        public static let valueChanged = Notification(rawValue: "AXValueChanged") }
    public struct NotificationUserInfoKey: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue }
        public static let announcement = NotificationUserInfoKey(rawValue: "AXAnnouncementKey")
        public static let priority = NotificationUserInfoKey(rawValue: "AXPriorityKey") }
    public struct Role: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue }
        public static let button = Role(rawValue: "AXButton"); public static let image = Role(rawValue: "AXImage")
        public static let staticText = Role(rawValue: "AXStaticText"); public static let group = Role(rawValue: "AXGroup") }
    public static func post(element: Any, notification: Notification, userInfo: [NotificationUserInfoKey: Any]? = nil) {}
    public static func post(element: Any, notification: Notification) {}
}
public enum NSAccessibilityPriorityLevel: Int { case low = 10, medium = 50, high = 90 }
extension NSAttributedString.Key {
    public static let font = NSAttributedString.Key("NSFont")
    public static let foregroundColor = NSAttributedString.Key("NSColor")
    public static let backgroundColor = NSAttributedString.Key("NSBackgroundColor")
    public static let paragraphStyle = NSAttributedString.Key("NSParagraphStyle")
    public static let link = NSAttributedString.Key("NSLink")
    public static let underlineStyle = NSAttributedString.Key("NSUnderline")
    public static let kern = NSAttributedString.Key("NSKern")
}
extension NSRect {
    public func fill(using operation: NSCompositingOperation = .sourceOver) {}
    public func frame(withWidth width: CGFloat = 1.0, using operation: NSCompositingOperation = .sourceOver) {}
}
open class NSFont: NSObject {
    public struct Weight: RawRepresentable, Hashable { public var rawValue: CGFloat; public init(rawValue: CGFloat) { self.rawValue = rawValue }
        public static let regular = Weight(rawValue: 0); public static let medium = Weight(rawValue: 0.23); public static let semibold = Weight(rawValue: 0.3)
        public static let bold = Weight(rawValue: 0.4); public static let light = Weight(rawValue: -0.4); public static let heavy = Weight(rawValue: 0.56) }
    open class func systemFont(ofSize fontSize: CGFloat) -> NSFont { fatalError() }
    open class func systemFont(ofSize fontSize: CGFloat, weight: Weight) -> NSFont { fatalError() }
    open class func boldSystemFont(ofSize fontSize: CGFloat) -> NSFont { fatalError() }
    open class func monospacedSystemFont(ofSize fontSize: CGFloat, weight: Weight) -> NSFont { fatalError() }
    open class func monospacedDigitSystemFont(ofSize fontSize: CGFloat, weight: Weight) -> NSFont { fatalError() }
    open class var systemFontSize: CGFloat { 13 }
    open class var smallSystemFontSize: CGFloat { 11 }
    open var pointSize: CGFloat { 0 }
}

@preconcurrency @MainActor open class NSMenu: NSObject {
    public init(title: String) {}
    public required init(coder: NSCoder) {}
    public override convenience init() { self.init(title: "") }
    open var title: String = ""
    open var items: [NSMenuItem] = []
    open var numberOfItems: Int { 0 }
    open weak var delegate: NSMenuDelegate?
    open var autoenablesItems: Bool = true
    open var minimumWidth: CGFloat = 0
    open var font: NSFont!
    open var supermenu: NSMenu? { nil }
    open var highlightedItem: NSMenuItem? { nil }
    open func addItem(_ newItem: NSMenuItem) {}
    @discardableResult open func addItem(withTitle string: String, action selector: Selector?, keyEquivalent charCode: String) -> NSMenuItem { fatalError() }
    open func insertItem(_ newItem: NSMenuItem, at index: Int) {}
    open func removeItem(_ item: NSMenuItem) {}
    open func removeItem(at index: Int) {}
    open func removeAllItems() {}
    open func item(at index: Int) -> NSMenuItem? { nil }
    open func item(withTitle title: String) -> NSMenuItem? { nil }
    open func item(withTag tag: Int) -> NSMenuItem? { nil }
    open func index(of item: NSMenuItem) -> Int { -1 }
    open func setSubmenu(_ menu: NSMenu?, for item: NSMenuItem) {}
    open func update() {}
    open func cancelTracking() {}
    open func cancelTrackingWithoutAnimation() {}
    @discardableResult open func popUp(positioning item: NSMenuItem?, at location: NSPoint, in view: NSView?) -> Bool { false }
}
@preconcurrency @MainActor public protocol NSMenuDelegate: NSObjectProtocol {
    func menuWillOpen(_ menu: NSMenu)
    func menuDidClose(_ menu: NSMenu)
    func menuNeedsUpdate(_ menu: NSMenu)
    func menu(_ menu: NSMenu, willHighlight item: NSMenuItem?)
}
extension NSMenuDelegate {
    public func menuWillOpen(_ menu: NSMenu) {}
    public func menuDidClose(_ menu: NSMenu) {}
    public func menuNeedsUpdate(_ menu: NSMenu) {}
    public func menu(_ menu: NSMenu, willHighlight item: NSMenuItem?) {}
}
@preconcurrency @MainActor open class NSMenuItem: NSObject {
    public init(title string: String, action selector: Selector?, keyEquivalent charCode: String) {}
    public required init(coder: NSCoder) {}
    public override convenience init() { self.init(title: "", action: nil, keyEquivalent: "") }
    open class func separator() -> NSMenuItem { fatalError() }
    open class func sectionHeader(title: String) -> NSMenuItem { fatalError() }
    open var title: String = ""
    open var attributedTitle: NSAttributedString?
    open var subtitle: String?
    open var action: Selector?
    open weak var target: AnyObject?
    open var keyEquivalent: String = ""
    open var keyEquivalentModifierMask: NSEvent.ModifierFlags = []
    open var isEnabled: Bool = true
    open var isHidden: Bool = false
    open var isAlternate: Bool = false
    open var isSeparatorItem: Bool { false }
    open var state: NSControl.StateValue = .off
    open var image: NSImage?
    open var onStateImage: NSImage!
    open var offStateImage: NSImage?
    open var submenu: NSMenu?
    open var menu: NSMenu? { nil }
    open var tag: Int = 0
    open var toolTip: String?
    open var representedObject: Any?
    open var view: NSView?
    open var indentationLevel: Int = 0
    open var badge: NSMenuItemBadge?
}
open class NSMenuItemBadge: NSObject {
    public init(count itemCount: Int) {}
    public init(string: String) {}
}
public protocol NSUserInterfaceValidations {}
