// Stub of SystemConfiguration: the dynamic store (SCDynamicStore.h, SCDynamicStoreCopySpecific.h).
import Foundation
import Dispatch

public typealias CFPropertyList = CFTypeRef
public typealias CFStringEncoding = UInt32
public final class SCDynamicStore {}
// C function pointers on a Mac; Swift function types here (their parameters are classes in this stub).
public typealias SCDynamicStoreCallBack = (SCDynamicStore, CFArray, UnsafeMutableRawPointer?) -> Void
public struct SCDynamicStoreContext {
    public var version: CFIndex
    public var info: UnsafeMutableRawPointer?
    public var retain: (@convention(c) (UnsafeRawPointer) -> UnsafeRawPointer)?
    public var release: (@convention(c) (UnsafeRawPointer) -> Void)?
    public var copyDescription: ((UnsafeRawPointer) -> Unmanaged<CFString>)?
    public init() { version = 0; info = nil; retain = nil; release = nil; copyDescription = nil }
    public init(version: CFIndex, info: UnsafeMutableRawPointer?, retain: (@convention(c) (UnsafeRawPointer) -> UnsafeRawPointer)?,
                release: (@convention(c) (UnsafeRawPointer) -> Void)?, copyDescription: ((UnsafeRawPointer) -> Unmanaged<CFString>)?) {
        self.version = version; self.info = info; self.retain = retain; self.release = release; self.copyDescription = copyDescription
    }
}
public func SCDynamicStoreCreate(_ allocator: CFAllocator?, _ name: CFString, _ callout: SCDynamicStoreCallBack?, _ context: UnsafeMutablePointer<SCDynamicStoreContext>?) -> SCDynamicStore? { nil }
public func SCDynamicStoreSetNotificationKeys(_ store: SCDynamicStore, _ keys: CFArray?, _ patterns: CFArray?) -> Bool { false }
public func SCDynamicStoreSetDispatchQueue(_ store: SCDynamicStore, _ queue: DispatchQueue?) -> Bool { false }
public func SCDynamicStoreCopyValue(_ store: SCDynamicStore?, _ key: CFString) -> CFPropertyList? { nil }
public func SCDynamicStoreCopyKeyList(_ store: SCDynamicStore?, _ pattern: CFString) -> CFArray? { nil }
public func SCDynamicStoreCopyLocalHostName(_ store: SCDynamicStore?) -> CFString? { nil }
public func SCDynamicStoreCopyComputerName(_ store: SCDynamicStore?, _ nameEncoding: UnsafeMutablePointer<CFStringEncoding>?) -> CFString? { nil }
