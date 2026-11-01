// Stub of Network.framework: connections, listeners, endpoints, paths, parameters and the TCP and
// TLS options, as Swift sees them on macOS 14+. Network re-exports Security's sec_protocol_* types.
// hostcheck: swift-api
@_exported import Security
import Foundation
import Dispatch

public typealias DNSServiceErrorType = Int32

public enum NWError: Error, Equatable, CustomDebugStringConvertible, LocalizedError {
    case posix(POSIXErrorCode)
    case dns(DNSServiceErrorType)
    case tls(OSStatus)
    @available(macOS 15.0, *) case wifiAware(Int32)
    public var debugDescription: String { "" }
    public var errorCode: Int32 { 0 }
}

public final class NWInterface: Hashable, CustomDebugStringConvertible {
    public enum InterfaceType: Hashable { case other, wifi, cellular, wiredEthernet, loopback }
    public var name: String { "" }
    public var type: InterfaceType { .other }
    public var index: Int { 0 }
    public var debugDescription: String { name }
    public static func == (a: NWInterface, b: NWInterface) -> Bool { a === b }
    public func hash(into h: inout Hasher) {}
}

public protocol IPAddress {
    init?(_ rawValue: Data, _ interface: NWInterface?)
    init?(_ string: String)
    var rawValue: Data { get }
    var interface: NWInterface? { get }
    var isLoopback: Bool { get }
    var isLinkLocal: Bool { get }
    var isMulticast: Bool { get }
}
public struct IPv4Address: IPAddress, Hashable, CustomDebugStringConvertible {
    public init?(_ rawValue: Data, _ interface: NWInterface? = nil) {}
    public init?(_ string: String) {}
    public var rawValue: Data { Data() }
    public var interface: NWInterface? { nil }
    public var isLoopback: Bool { false }
    public var isLinkLocal: Bool { false }
    public var isMulticast: Bool { false }
    public var debugDescription: String { "" }
    public static let any = IPv4Address("0.0.0.0")!
    public static let broadcast = IPv4Address("255.255.255.255")!
    public static let loopback = IPv4Address("127.0.0.1")!
    public static let allHostsGroup = IPv4Address("224.0.0.1")!
}
public struct IPv6Address: IPAddress, Hashable, CustomDebugStringConvertible {
    public init?(_ rawValue: Data, _ interface: NWInterface? = nil) {}
    public init?(_ string: String) {}
    public var rawValue: Data { Data() }
    public var interface: NWInterface? { nil }
    public var isLoopback: Bool { false }
    public var isLinkLocal: Bool { false }
    public var isMulticast: Bool { false }
    public var isAny: Bool { false }
    public var isIPv4Mapped: Bool { false }
    public var isIPv4Compatabile: Bool { false }
    public var isUniqueLocal: Bool { false }
    public var asIPv4: IPv4Address? { nil }
    public var debugDescription: String { "" }
    public static let any = IPv6Address("::")!
    public static let loopback = IPv6Address("::1")!
}

public enum NWEndpoint: Hashable, CustomDebugStringConvertible {
    public enum Host: Hashable, CustomDebugStringConvertible, ExpressibleByStringLiteral {
        case name(String, NWInterface?)
        case ipv4(IPv4Address)
        case ipv6(IPv6Address)
        public init(_ string: String) { self = .name(string, nil) }
        public init(stringLiteral value: String) { self = .name(value, nil) }
        public var interface: NWInterface? { nil }
        public var debugDescription: String { "" }
    }
    public struct Port: Hashable, CustomDebugStringConvertible, ExpressibleByIntegerLiteral, RawRepresentable {
        public static let any = Port(integerLiteral: 0)
        public static let ssh = Port(integerLiteral: 22)
        public static let http = Port(integerLiteral: 80)
        public static let https = Port(integerLiteral: 443)
        public var rawValue: UInt16
        public init?(rawValue: UInt16) { self.rawValue = rawValue }
        public init?(_ string: String) { guard let v = UInt16(string) else { return nil }; rawValue = v }
        public init(integerLiteral value: UInt16) { rawValue = value }
        public var debugDescription: String { "\(rawValue)" }
    }
    case hostPort(host: Host, port: Port)
    case service(name: String, type: String, domain: String, interface: NWInterface?)
    case unix(path: String)
    case url(URL)
    case opaque(AnyObject)
    public var interface: NWInterface? { nil }
    public var debugDescription: String { "" }
    public static func == (a: NWEndpoint, b: NWEndpoint) -> Bool { false }
    public func hash(into h: inout Hasher) {}
}

public struct NWPath: Equatable, CustomDebugStringConvertible {
    public enum Status: Equatable { case satisfied, unsatisfied, requiresConnection }
    public enum UnsatisfiedReason: Equatable { case notAvailable, cellularDenied, wifiDenied, localNetworkDenied, vpnInactive }
    public let status: Status
    public var unsatisfiedReason: UnsatisfiedReason { .notAvailable }
    public let availableInterfaces: [NWInterface]
    public let isExpensive: Bool
    public let isConstrained: Bool
    public let supportsIPv4: Bool
    public let supportsIPv6: Bool
    public let supportsDNS: Bool
    public let localEndpoint: NWEndpoint?
    public let remoteEndpoint: NWEndpoint?
    public let gateways: [NWEndpoint]
    public func usesInterfaceType(_ type: NWInterface.InterfaceType) -> Bool { false }
    public var debugDescription: String { "" }
    public static func == (a: NWPath, b: NWPath) -> Bool { false }
}

public final class NWPathMonitor {
    public init() {}
    public init(requiredInterfaceType: NWInterface.InterfaceType) {}
    public init(prohibitedInterfaceTypes: [NWInterface.InterfaceType]) {}
    public var pathUpdateHandler: ((NWPath) -> Void)?
    public var currentPath: NWPath { fatalError() }
    public var queue: DispatchQueue? { nil }
    public func start(queue: DispatchQueue) {}
    public func cancel() {}
}

public class NWProtocolDefinition: Equatable, CustomDebugStringConvertible {
    public let name: String = ""
    public var debugDescription: String { name }
    public static func == (a: NWProtocolDefinition, b: NWProtocolDefinition) -> Bool { a === b }
}
public class NWProtocolOptions {}
public class NWProtocolMetadata {}
public class NWProtocol {}

public class NWProtocolTCP: NWProtocol {
    public static let definition = NWProtocolDefinition()
    public class Options: NWProtocolOptions {
        public override init() {}
        public var noDelay: Bool = false
        public var noPush: Bool = false
        public var noOptions: Bool = false
        public var enableKeepalive: Bool = false
        public var keepaliveCount: Int = 0
        public var keepaliveIdle: Int = 0
        public var keepaliveInterval: Int = 0
        public var maximumSegmentSize: Int = 0
        public var connectionTimeout: Int = 0
        public var persistTimeout: Int = 0
        public var connectionDropTime: Int = 0
        public var retransmitFinDrop: Bool = false
        public var disableAckStretching: Bool = false
        public var enableFastOpen: Bool = false
        public var disableECN: Bool = false
    }
    public class Metadata: NWProtocolMetadata {
        public var availableReceiveBuffer: UInt32 { 0 }
        public var availableSendBuffer: UInt32 { 0 }
    }
}
public class NWProtocolTLS: NWProtocol {
    public static let definition = NWProtocolDefinition()
    public class Options: NWProtocolOptions {
        public override init() {}
        public var securityProtocolOptions: sec_protocol_options_t { fatalError() }
    }
    public class Metadata: NWProtocolMetadata {
        public var securityProtocolMetadata: sec_protocol_metadata_t { fatalError() }
    }
}
public class NWProtocolIP: NWProtocol {
    public static let definition = NWProtocolDefinition()
    public class Options: NWProtocolOptions {
        public enum Version { case any, v4, v6 }
        public var version: Version = .any
        public var hopLimit: UInt8 = 0
        public var useMinimumMTU: Bool = false
        public var disableFragmentation: Bool = false
        public var shouldCalculateReceiveTime: Bool = false
    }
}

public final class NWParameters: CustomDebugStringConvertible {
    public enum ServiceClass { case bestEffort, background, interactiveVideo, interactiveVoice, responsiveData, signaling }
    public enum MultipathServiceType { case disabled, handover, interactive, aggregate }
    public enum ExpiredDNSBehavior { case systemDefault, allow, prohibit }
    public final class ProtocolStack {
        public var applicationProtocols: [NWProtocolOptions] = []
        public var transportProtocol: NWProtocolOptions?
        public var internetProtocol: NWProtocolOptions?
    }
    public init() {}
    public convenience init(tls: NWProtocolTLS.Options?, tcp: NWProtocolTCP.Options = .init()) { self.init() }
    public convenience init(dtls: NWProtocolTLS.Options?, udp: NWProtocolOptions) { self.init() }
    public static var tcp: NWParameters { NWParameters() }
    public static var tls: NWParameters { NWParameters() }
    public static var udp: NWParameters { NWParameters() }
    public var requiredInterface: NWInterface?
    public var requiredInterfaceType: NWInterface.InterfaceType = .other
    public var prohibitedInterfaces: [NWInterface]?
    public var prohibitedInterfaceTypes: [NWInterface.InterfaceType]?
    public var prohibitExpensivePaths: Bool = false
    public var prohibitConstrainedPaths: Bool = false
    public var preferNoProxies: Bool = false
    public var requiredLocalEndpoint: NWEndpoint?
    public var allowLocalEndpointReuse: Bool = false
    public var acceptLocalOnly: Bool = false
    public var includePeerToPeer: Bool = false
    public var serviceClass: ServiceClass = .bestEffort
    public var multipathServiceType: MultipathServiceType = .disabled
    public var expiredDNSBehavior: ExpiredDNSBehavior = .systemDefault
    public var allowFastOpen: Bool = false
    public var defaultProtocolStack: ProtocolStack = ProtocolStack()
    public func copy() -> NWParameters { self }
    public var debugDescription: String { "" }
}

public final class NWConnection: CustomDebugStringConvertible {
    public enum State: Equatable {
        case setup, waiting(NWError), preparing, ready, failed(NWError), cancelled
    }
    public final class ContentContext {
        public static let defaultMessage = ContentContext(identifier: "default")
        public static let finalMessage = ContentContext(identifier: "final")
        public static let defaultStream = ContentContext(identifier: "stream")
        public let identifier: String
        public let isFinal: Bool = false
        public init(identifier: String, expiration: UInt64 = 0, priority: Double = 0.5, isFinal: Bool = false,
                    antecedent: ContentContext? = nil, metadata: [NWProtocolMetadata]? = []) { self.identifier = identifier }
        public var protocolMetadata: [NWProtocolMetadata] { [] }
        public func protocolMetadata(definition: NWProtocolDefinition) -> NWProtocolMetadata? { nil }
    }
    public enum SendCompletion {
        case contentProcessed((NWError?) -> Void)
        case idempotent
    }
    public init(host: NWEndpoint.Host, port: NWEndpoint.Port, using: NWParameters) { endpoint = .hostPort(host: host, port: port); parameters = using }
    public init(to: NWEndpoint, using: NWParameters) { endpoint = to; parameters = using }
    public let endpoint: NWEndpoint
    public let parameters: NWParameters
    public var state: State { .setup }
    public var currentPath: NWPath? { nil }
    public var queue: DispatchQueue? { nil }
    public var maximumDatagramSize: Int { 0 }
    public var stateUpdateHandler: ((State) -> Void)?
    public var viabilityUpdateHandler: ((Bool) -> Void)?
    public var betterPathUpdateHandler: ((Bool) -> Void)?
    public var pathUpdateHandler: ((NWPath) -> Void)?
    public func start(queue: DispatchQueue) {}
    public func restart() {}
    public func cancel() {}
    public func forceCancel() {}
    public func cancelCurrentEndpoint() {}
    public func send(content: Data?, contentContext: ContentContext = .defaultMessage, isComplete: Bool = true, completion: SendCompletion) {}
    public func batch(_ block: () -> Void) {}
    public func receive(minimumIncompleteLength: Int, maximumLength: Int, completion: @escaping (Data?, ContentContext?, Bool, NWError?) -> Void) {}
    public func receiveMessage(completion: @escaping (Data?, ContentContext?, Bool, NWError?) -> Void) {}
    public func metadata(definition: NWProtocolDefinition) -> NWProtocolMetadata? { nil }
    public var debugDescription: String { "" }
}

public struct NWTXTRecord: Hashable, Collection, CustomDebugStringConvertible {
    public enum Entry: Hashable { case none, empty, string(String), data(Data) }
    public typealias Index = Dictionary<String, Entry>.Index
    public typealias Element = (key: String, value: Entry)
    public init(_ dictionary: [String: String] = [:]) {}
    public init?(_ data: Data) {}
    public var dictionary: [String: String] { [:] }
    public var data: Data { Data() }
    public var startIndex: Index { [String: Entry]().startIndex }
    public var endIndex: Index { [String: Entry]().endIndex }
    public func index(after i: Index) -> Index { i }
    public subscript(position: Index) -> Element { fatalError() }
    public subscript(key: String) -> String? { get { nil } set {} }
    public func getEntry(for key: String) -> Entry? { nil }
    @discardableResult public mutating func setEntry(_ entry: Entry, for key: String) -> Bool { true }
    @discardableResult public mutating func removeEntry(key: String) -> Bool { true }
    public var debugDescription: String { "" }
    public static func == (a: NWTXTRecord, b: NWTXTRecord) -> Bool { true }
    public func hash(into h: inout Hasher) {}
}

public final class NWListener: CustomDebugStringConvertible {
    public enum State: Equatable { case setup, waiting(NWError), ready, failed(NWError), cancelled }
    public enum ServiceRegistrationChange { case add(NWEndpoint), remove(NWEndpoint) }
    public struct Service: Equatable, CustomDebugStringConvertible {
        public let name: String?
        public let type: String
        public let domain: String?
        public let txtRecord: Data?
        public init(name: String? = nil, type: String, domain: String? = nil, txtRecord: Data? = nil) {
            self.name = name; self.type = type; self.domain = domain; self.txtRecord = txtRecord
        }
        public init(name: String? = nil, type: String, domain: String? = nil, txtRecord: NWTXTRecord) {
            self.name = name; self.type = type; self.domain = domain; self.txtRecord = txtRecord.data
        }
        public var txtRecordObject: NWTXTRecord? { nil }
        public var debugDescription: String { "" }
    }
    public init(using parameters: NWParameters, on port: NWEndpoint.Port = .any) throws { self.parameters = parameters }
    public convenience init(service: Service, using parameters: NWParameters) throws { try self.init(using: parameters) }
    public let parameters: NWParameters
    public var port: NWEndpoint.Port? { nil }
    public var state: State { .setup }
    public var queue: DispatchQueue? { nil }
    public var service: Service?
    public var newConnectionLimit: Int = 0
    public static let InfiniteConnectionLimit = -1
    public var stateUpdateHandler: ((State) -> Void)?
    public var newConnectionHandler: ((NWConnection) -> Void)?
    public var serviceRegistrationUpdateHandler: ((ServiceRegistrationChange) -> Void)?
    public func start(queue: DispatchQueue) {}
    public func cancel() {}
    public var debugDescription: String { "" }
}
