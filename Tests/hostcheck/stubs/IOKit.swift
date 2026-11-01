// Stub of IOKit: the registry walk (IOKitLib.h) and power management's assertions (IOPMLib.h,
// imported on a Mac as IOKit.pwr_mgt), as Swift imports them.
import Foundation

public typealias io_object_t = mach_port_t
public typealias io_registry_entry_t = io_object_t
public typealias io_service_t = io_object_t
public typealias io_iterator_t = io_object_t
public typealias IOReturn = kern_return_t
public typealias IOOptionBits = UInt32
public var kIOReturnSuccess: Int32 { 0 }
public var kIOReturnError: Int32 { -536870212 }
public let kIOMainPortDefault: mach_port_t = 0
public let kIOMasterPortDefault: mach_port_t = 0
public var kIOServicePlane: String { "IOService" }
public var kIORegistryIterateRecursively: Int32 { 1 }
public var kIORegistryIterateParents: Int32 { 2 }

public func IOBSDNameMatching(_ mainPort: mach_port_t, _ options: UInt32, _ bsdName: UnsafePointer<CChar>!) -> CFMutableDictionary! { nil }
public func IOServiceMatching(_ name: UnsafePointer<CChar>!) -> CFMutableDictionary! { nil }
public func IOServiceGetMatchingService(_ mainPort: mach_port_t, _ matching: CFDictionary!) -> io_service_t { 0 }
public func IOServiceGetMatchingServices(_ mainPort: mach_port_t, _ matching: CFDictionary!, _ existing: UnsafeMutablePointer<io_iterator_t>!) -> kern_return_t { 0 }
public func IOIteratorNext(_ iterator: io_iterator_t) -> io_object_t { 0 }
public func IOObjectRelease(_ object: io_object_t) -> kern_return_t { 0 }
public func IOObjectRetain(_ object: io_object_t) -> kern_return_t { 0 }
public func IOObjectConformsTo(_ object: io_object_t, _ className: UnsafePointer<CChar>!) -> boolean_t { 0 }
public func IORegistryEntryGetParentEntry(_ entry: io_registry_entry_t, _ plane: UnsafePointer<CChar>!, _ parent: UnsafeMutablePointer<io_registry_entry_t>!) -> kern_return_t { 0 }
public func IORegistryEntryCreateCFProperty(_ entry: io_registry_entry_t, _ key: CFString!, _ allocator: CFAllocator!, _ options: IOOptionBits) -> Unmanaged<CFTypeRef>! { nil }
public func IORegistryEntrySearchCFProperty(_ entry: io_registry_entry_t, _ plane: UnsafePointer<CChar>!, _ key: CFString!, _ allocator: CFAllocator!, _ options: IOOptionBits) -> CFTypeRef! { nil }
public func IORegistryEntryGetName(_ entry: io_registry_entry_t, _ name: UnsafeMutablePointer<CChar>!) -> kern_return_t { 0 }

// Power management (IOPMLib.h).
public typealias IOPMAssertionID = UInt32
public typealias IOPMAssertionLevel = UInt32
public var kIOPMNullAssertionID: Int32 { 0 }
public var kIOPMAssertionLevelOff: Int32 { 0 }
public var kIOPMAssertionLevelOn: Int32 { 255 }
public struct IOPMUserActiveType: RawRepresentable, Equatable {
    public var rawValue: UInt32
    public init(rawValue: UInt32) { self.rawValue = rawValue }
    public init(_ rawValue: UInt32) { self.rawValue = rawValue }
}
public var kIOPMUserActiveLocal: IOPMUserActiveType { IOPMUserActiveType(0) }
public var kIOPMUserActiveRemote: IOPMUserActiveType { IOPMUserActiveType(1) }
public var kIOPMAssertionTypePreventUserIdleSystemSleep: String { "PreventUserIdleSystemSleep" }
public var kIOPMAssertionTypePreventUserIdleDisplaySleep: String { "PreventUserIdleDisplaySleep" }
public var kIOPMAssertPreventUserIdleSystemSleep: String { "PreventUserIdleSystemSleep" }
public func IOPMAssertionDeclareUserActivity(_ AssertionName: CFString!, _ userType: IOPMUserActiveType, _ AssertionID: UnsafeMutablePointer<IOPMAssertionID>!) -> IOReturn { 0 }
public func IOPMAssertionCreateWithName(_ AssertionType: CFString!, _ AssertionLevel: IOPMAssertionLevel, _ AssertionName: CFString!, _ AssertionID: UnsafeMutablePointer<IOPMAssertionID>!) -> IOReturn { 0 }
public func IOPMAssertionRelease(_ AssertionID: IOPMAssertionID) -> IOReturn { 0 }
