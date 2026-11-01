// Stub of dnssd (dns_sd.h): the NAT port mapping the router probe asks for, as Swift imports it.
import Dispatch

public typealias DNSServiceRef = OpaquePointer
public typealias DNSServiceFlags = UInt32
public typealias DNSServiceErrorType = Int32
public typealias DNSServiceProtocol = UInt32
public var kDNSServiceErr_NoError: Int { 0 }
public var kDNSServiceErr_Unknown: Int { -65537 }
public var kDNSServiceErr_NoSuchName: Int { -65538 }
public var kDNSServiceErr_Unsupported: Int { -65544 }
public var kDNSServiceErr_NATPortMappingUnsupported: Int { -65564 }
public var kDNSServiceErr_NATPortMappingDisabled: Int { -65565 }
public var kDNSServiceErr_DoubleNAT: Int { -65563 }
public var kDNSServiceErr_Timeout: Int { -65568 }
public var kDNSServiceProtocol_IPv4: Int { 1 }
public var kDNSServiceProtocol_TCP: Int { 0x20 }
public typealias DNSServiceNATPortMappingReply = @convention(c) (DNSServiceRef?, DNSServiceFlags, UInt32, DNSServiceErrorType, UInt32,
                                                                DNSServiceProtocol, UInt16, UInt16, UInt32, UnsafeMutableRawPointer?) -> Void
public func DNSServiceNATPortMappingCreate(_ sdRef: UnsafeMutablePointer<DNSServiceRef?>!, _ flags: DNSServiceFlags, _ interfaceIndex: UInt32,
                                           _ protocol: DNSServiceProtocol, _ internalPort: UInt16, _ externalPort: UInt16, _ ttl: UInt32,
                                           _ callBack: DNSServiceNATPortMappingReply!, _ context: UnsafeMutableRawPointer!) -> DNSServiceErrorType { 0 }
public func DNSServiceSetDispatchQueue(_ service: DNSServiceRef!, _ queue: DispatchQueue!) -> DNSServiceErrorType { 0 }
public func DNSServiceRefDeallocate(_ sdRef: DNSServiceRef!) {}
public func DNSServiceRefSockFD(_ sdRef: DNSServiceRef!) -> Int32 { 0 }
