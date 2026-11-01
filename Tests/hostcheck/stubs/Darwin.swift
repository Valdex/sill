// Stub of Darwin: Linux's Glibc plus what Darwin's C library has and glibc lacks, with Darwin's
// types (mach time, libproc, sysctl's kinfo_proc, the BSD sockaddr length fields).
@_exported import Glibc

public struct mach_timebase_info_data_t { public var numer: UInt32 = 1; public var denom: UInt32 = 1; public init() {}
    public init(numer: UInt32, denom: UInt32) { self.numer = numer; self.denom = denom } }
public typealias mach_timebase_info_t = UnsafeMutablePointer<mach_timebase_info_data_t>
public typealias kern_return_t = Int32
public typealias boolean_t = Int32
public typealias mach_port_t = UInt32
public let KERN_SUCCESS: Int32 = 0
public func mach_timebase_info(_ info: mach_timebase_info_t!) -> kern_return_t { 0 }
public func mach_absolute_time() -> UInt64 { 0 }
public func mach_continuous_time() -> UInt64 { 0 }
public func clock_gettime_nsec_np(_ clock_id: clockid_t) -> UInt64 { 0 }

public let PROC_PIDPATHINFO_MAXSIZE: Int32 = 4096
public func proc_pidpath(_ pid: Int32, _ buffer: UnsafeMutableRawPointer!, _ buffersize: UInt32) -> Int32 { 0 }
public func proc_name(_ pid: Int32, _ buffer: UnsafeMutableRawPointer!, _ buffersize: UInt32) -> Int32 { 0 }

public let CTL_KERN: Int32 = 1
public let KERN_PROC: Int32 = 14
public let KERN_PROC_PID: Int32 = 1
public struct eproc { public var e_ppid: pid_t = 0; public var e_pgid: pid_t = 0; public init() {} }
public struct extern_proc { public var p_pid: pid_t = 0; public var p_comm: (CChar, CChar, CChar, CChar) = (0, 0, 0, 0); public init() {} }
public struct kinfo_proc { public var kp_proc = extern_proc(); public var kp_eproc = eproc(); public init() {} }
public func sysctl(_ name: UnsafeMutablePointer<Int32>!, _ namelen: u_int, _ oldp: UnsafeMutableRawPointer!,
                   _ oldlenp: UnsafeMutablePointer<Int>!, _ newp: UnsafeMutableRawPointer!, _ newlen: Int) -> Int32 { 0 }
public func sysctlbyname(_ name: UnsafePointer<CChar>!, _ oldp: UnsafeMutableRawPointer!, _ oldlenp: UnsafeMutablePointer<Int>!,
                         _ newp: UnsafeMutableRawPointer!, _ newlen: Int) -> Int32 { 0 }

// BSD's length byte at the front of every sockaddr (Linux has none).
extension sockaddr_in { public var sin_len: UInt8 { get { 16 } set {} } }
extension sockaddr_in6 { public var sin6_len: UInt8 { get { 28 } set {} } }
extension sockaddr { public var sa_len: UInt8 { get { 16 } set {} } }

public typealias UniChar = UInt16

public struct DarwinBoolean: ExpressibleByBooleanLiteral, CustomStringConvertible, Equatable {
    var value: UInt8
    public init(_ value: Bool) { self.value = value ? 1 : 0 }
    public init(booleanLiteral value: Bool) { self.init(value) }
    public var boolValue: Bool { value != 0 }
    public var description: String { boolValue.description }
}

// Darwin's values where glibc has others' types (sys/param.h's MAXPATHLEN; SOCK_* are Int32 on a
// Mac, an enum in glibc). Declared here, they shadow Glibc's.
public var MAXPATHLEN: Int32 { 1024 }
public var SOCK_STREAM: Int32 { 1 }
public var SOCK_DGRAM: Int32 { 2 }
public var SOCK_RAW: Int32 { 3 }
