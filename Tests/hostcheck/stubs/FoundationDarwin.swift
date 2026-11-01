// What Darwin's Foundation gives a file that Linux's does not: CoreFoundation's types and functions
// (Darwin's Foundation re-exports CoreFoundation), the Objective-C runtime's names and Darwin's C
// library (the Darwin stub). run.sh
// imports this module into every file (-import-module), as `import Foundation` would on a Mac.
//
// CoreFoundation's types are Foundation's classes here (CFString is NSString, CFDictionary is
// NSDictionary, …), which keeps toll-free bridging (`"x" as CFString`, `cf as? [String: Any]`)
// working as on a Mac. The one leniency: a CF value passes where its NS twin is wanted without the
// `as` a Mac would ask for.
@_exported import ObjectiveC
@_exported import Darwin
// URLSession and its kin, which Darwin's Foundation has itself.
@_exported import FoundationNetworking
import Foundation

public typealias CFTypeRef = AnyObject
public typealias CFTypeID = UInt
public typealias CFIndex = Int
public typealias CFOptionFlags = UInt
public typealias CFHashCode = UInt
public typealias CFTimeInterval = Double
public typealias CFAbsoluteTime = CFTimeInterval
public typealias CFString = NSString
public typealias CFMutableString = NSMutableString
public typealias CFData = NSData
public typealias CFMutableData = NSMutableData
public typealias CFDictionary = NSDictionary
public typealias CFMutableDictionary = NSMutableDictionary
public typealias CFArray = NSArray
public typealias CFMutableArray = NSMutableArray
public typealias CFNumber = NSNumber
public typealias CFBoolean = NSNumber
public typealias CFURL = NSURL
public typealias CFError = NSError
public typealias CFDate = NSDate
public typealias CFSet = NSSet
public typealias CFAttributedString = NSAttributedString

public final class CFNull {}
public final class CFAllocator {}
public final class CFRunLoop {}
public final class CFRunLoopSource {}
public final class CFMachPort {}
public final class CFBundle {}

public let kCFAllocatorDefault: CFAllocator! = nil
public let kCFBooleanTrue: CFBoolean! = NSNumber(value: true)
public let kCFBooleanFalse: CFBoolean! = NSNumber(value: false)
public let kCFNull: CFNull! = CFNull()

public struct CFRunLoopMode: RawRepresentable, Hashable {
    public var rawValue: CFString
    public init(rawValue: CFString) { self.rawValue = rawValue }
    public init(_ rawValue: CFString) { self.rawValue = rawValue }
    public static let commonModes: CFRunLoopMode! = CFRunLoopMode(rawValue: "kCFRunLoopCommonModes")
    public static let defaultMode: CFRunLoopMode! = CFRunLoopMode(rawValue: "kCFRunLoopDefaultMode")
}
public let kCFRunLoopCommonModes: CFRunLoopMode! = .commonModes
public let kCFRunLoopDefaultMode: CFRunLoopMode! = .defaultMode

public func CFAbsoluteTimeGetCurrent() -> CFAbsoluteTime { 0 }
public func CFGetTypeID(_ cf: CFTypeRef!) -> CFTypeID { 0 }
public func CFStringGetTypeID() -> CFTypeID { 1 }
public func CFNumberGetTypeID() -> CFTypeID { 2 }
public func CFBooleanGetTypeID() -> CFTypeID { 3 }
public func CFArrayGetTypeID() -> CFTypeID { 4 }
public func CFDictionaryGetTypeID() -> CFTypeID { 5 }
public func CFDataGetTypeID() -> CFTypeID { 6 }
public func CFNullGetTypeID() -> CFTypeID { 7 }
public func CFEqual(_ cf1: CFTypeRef!, _ cf2: CFTypeRef!) -> Bool { false }
public func CFHash(_ cf: CFTypeRef!) -> CFHashCode { 0 }
public func CFRunLoopGetMain() -> CFRunLoop! { CFRunLoop() }
public func CFRunLoopGetCurrent() -> CFRunLoop! { CFRunLoop() }
public func CFRunLoopAddSource(_ rl: CFRunLoop!, _ source: CFRunLoopSource!, _ mode: CFRunLoopMode!) {}
public func CFRunLoopRemoveSource(_ rl: CFRunLoop!, _ source: CFRunLoopSource!, _ mode: CFRunLoopMode!) {}
public func CFRunLoopRun() {}
public func CFRunLoopStop(_ rl: CFRunLoop!) {}
public func CFMachPortCreateRunLoopSource(_ allocator: CFAllocator!, _ port: CFMachPort!, _ order: CFIndex) -> CFRunLoopSource! { nil }

/// What stage.py makes of `#selector(X)`: X must name something that exists.
public func _selector(_ reference: @autoclosure () -> Any) -> Selector { Selector("") }

// MacTypes (CoreServices' CarbonCore), which Darwin's Foundation brings too.
public typealias OSStatus = Int32
public typealias OSType = UInt32
public typealias FourCharCode = UInt32
public typealias Boolean = UInt8
public typealias UTF32Char = UInt32
public let noErr: OSStatus = 0

// Darwin's Foundation only.
open class DistributedNotificationCenter: NotificationCenter, @unchecked Sendable {
    public struct CenterType: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue }
        public static let localNotificationCenterType = CenterType(rawValue: "NSLocalNotificationCenterType") }
    public struct Options: OptionSet { public var rawValue: UInt; public init(rawValue: UInt) { self.rawValue = rawValue }
        public static let deliverImmediately = Options(rawValue: 1); public static let postToAllSessions = Options(rawValue: 2) }
    open class func `default`() -> DistributedNotificationCenter { fatalError() }
    open func postNotificationName(_ name: NSNotification.Name, object: String?, userInfo: [AnyHashable: Any]? = nil, deliverImmediately: Bool = false) {}
}

extension NotificationCenter {
    public func addObserver(_ observer: Any, selector aSelector: Selector, name aName: NSNotification.Name?, object anObject: Any?) {}
}
extension ProcessInfo {
    public struct ActivityOptions: OptionSet { public var rawValue: UInt64; public init(rawValue: UInt64) { self.rawValue = rawValue }
        public static let idleDisplaySleepDisabled = ActivityOptions(rawValue: 1 << 40)
        public static let idleSystemSleepDisabled = ActivityOptions(rawValue: 1 << 20)
        public static let suddenTerminationDisabled = ActivityOptions(rawValue: 1 << 14)
        public static let automaticTerminationDisabled = ActivityOptions(rawValue: 1 << 15)
        public static let animationTrackingEnabled = ActivityOptions(rawValue: 1 << 45)
        public static let trackingEnabled = ActivityOptions(rawValue: 1 << 46)
        public static let userInitiated = ActivityOptions(rawValue: 0x00FFFFFF | (1 << 20))
        public static let userInitiatedAllowingIdleSystemSleep = ActivityOptions(rawValue: 0x00FFFFFF)
        public static let userInteractive = ActivityOptions(rawValue: 0x00FFFFFF | (1 << 40))
        public static let background = ActivityOptions(rawValue: 0x000000FF)
        public static let latencyCritical = ActivityOptions(rawValue: 0xFF00000000) }
    public func beginActivity(options: ActivityOptions = [], reason: String) -> NSObjectProtocol { NSObject() }
    public func endActivity(_ activity: NSObjectProtocol) {}
    public func performActivity(options: ActivityOptions = [], reason: String, using block: () -> Void) { block() }
    public func disableSuddenTermination() {}
    public func enableSuddenTermination() {}
    public func disableAutomaticTermination(_ reason: String) {}
    public func enableAutomaticTermination(_ reason: String) {}
    public var automaticTerminationSupportEnabled: Bool { get { false } set {} }
}
open class RelativeDateTimeFormatter: Formatter {
    public enum DateTimeStyle: Int { case numeric = 0, named = 1 }
    public enum UnitsStyle: Int { case full = 0, spellOut = 1, short = 2, abbreviated = 3 }
    public override init() { super.init() }
    public required init?(coder: NSCoder) { super.init(coder: coder) }
    open var dateTimeStyle: DateTimeStyle = .numeric
    open var unitsStyle: UnitsStyle = .full
    open var formattingContext: Formatter.Context = .unknown
    open var calendar: Calendar! = .current
    open var locale: Locale! = .current
    open func localizedString(from dateComponents: DateComponents) -> String { "" }
    open func localizedString(fromTimeInterval timeInterval: TimeInterval) -> String { "" }
    open func localizedString(for date: Date, relativeTo referenceDate: Date) -> String { "" }
}
extension URLSession {
    /// macOS 12+: the body as it arrives.
    public func bytes(for request: URLRequest, delegate: URLSessionTaskDelegate? = nil) async throws -> (AsyncBytes, URLResponse) { fatalError() }
    public func bytes(from url: URL, delegate: URLSessionTaskDelegate? = nil) async throws -> (AsyncBytes, URLResponse) { fatalError() }
    public struct AsyncBytes: AsyncSequence {
        public typealias Element = UInt8
        public struct AsyncIterator: AsyncIteratorProtocol { public mutating func next() async throws -> UInt8? { nil } }
        public func makeAsyncIterator() -> AsyncIterator { AsyncIterator() }
        public var task: URLSessionDataTask { fatalError() }
    }
}
