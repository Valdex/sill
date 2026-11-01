// Stub of CryptoKit: SHA-256, HMAC, symmetric keys and P-256 signatures, as the host and its
// protocol use them. Signatures as CryptoKit's (macOS 10.15+).
// hostcheck: swift-api
import Foundation

public protocol Digest: Hashable, ContiguousBytes, Sequence, CustomStringConvertible where Element == UInt8 {
    static var byteCount: Int { get }
}
public protocol HashFunction {
    associatedtype Digest: CryptoKit.Digest
    static var byteCount: Int { get }
    static var blockByteCount: Int { get }
    init()
    mutating func update(bufferPointer: UnsafeRawBufferPointer)
    func finalize() -> Digest
}
extension HashFunction {
    public static func hash<D: DataProtocol>(data: D) -> Digest { fatalError() }
    public mutating func update<D: DataProtocol>(data: D) {}
}

public struct SHA256Digest: Digest {
    public static var byteCount: Int { 32 }
    public func makeIterator() -> Array<UInt8>.Iterator { [UInt8]().makeIterator() }
    public func withUnsafeBytes<R>(_ body: (UnsafeRawBufferPointer) throws -> R) rethrows -> R { try [UInt8]().withUnsafeBytes(body) }
    public var description: String { "" }
}
public struct SHA256: HashFunction {
    public static var byteCount: Int { 32 }
    public static var blockByteCount: Int { 64 }
    public init() {}
    public mutating func update(bufferPointer: UnsafeRawBufferPointer) {}
    public func finalize() -> SHA256Digest { SHA256Digest() }
}
public struct SHA512Digest: Digest {
    public static var byteCount: Int { 64 }
    public func makeIterator() -> Array<UInt8>.Iterator { [UInt8]().makeIterator() }
    public func withUnsafeBytes<R>(_ body: (UnsafeRawBufferPointer) throws -> R) rethrows -> R { try [UInt8]().withUnsafeBytes(body) }
    public var description: String { "" }
}
public struct SHA512: HashFunction {
    public static var byteCount: Int { 64 }
    public static var blockByteCount: Int { 128 }
    public init() {}
    public mutating func update(bufferPointer: UnsafeRawBufferPointer) {}
    public func finalize() -> SHA512Digest { SHA512Digest() }
}

public struct SymmetricKeySize {
    public let bitCount: Int
    public init(bitCount: Int) { self.bitCount = bitCount }
    public static var bits128: SymmetricKeySize { .init(bitCount: 128) }
    public static var bits192: SymmetricKeySize { .init(bitCount: 192) }
    public static var bits256: SymmetricKeySize { .init(bitCount: 256) }
}
public struct SymmetricKey: ContiguousBytes {
    public init<D: ContiguousBytes>(data: D) {}
    public init(size: SymmetricKeySize) {}
    public var bitCount: Int { 0 }
    public func withUnsafeBytes<R>(_ body: (UnsafeRawBufferPointer) throws -> R) rethrows -> R { try [UInt8]().withUnsafeBytes(body) }
}

public protocol MessageAuthenticationCode: Hashable, ContiguousBytes, Sequence where Element == UInt8 {
    var byteCount: Int { get }
}
public struct HashedAuthenticationCode<H: HashFunction>: MessageAuthenticationCode, CustomStringConvertible {
    public var byteCount: Int { 0 }
    public func makeIterator() -> Array<UInt8>.Iterator { [UInt8]().makeIterator() }
    public func withUnsafeBytes<R>(_ body: (UnsafeRawBufferPointer) throws -> R) rethrows -> R { try [UInt8]().withUnsafeBytes(body) }
    public var description: String { "" }
}
public struct HMAC<H: HashFunction> {
    public typealias MAC = HashedAuthenticationCode<H>
    public init(key: SymmetricKey) {}
    public mutating func update<D: DataProtocol>(data: D) {}
    public func finalize() -> MAC { MAC() }
    public static func authenticationCode<D: DataProtocol>(for data: D, using key: SymmetricKey) -> MAC { MAC() }
    public static func isValidAuthenticationCode<D: DataProtocol>(_ mac: MAC, authenticating authenticatedData: D, using key: SymmetricKey) -> Bool { false }
    public static func isValidAuthenticationCode<C: ContiguousBytes, D: DataProtocol>(_ authenticationCode: C, authenticating authenticatedData: D, using key: SymmetricKey) -> Bool { false }
}

public enum CryptoKitError: Error { case incorrectKeySize, incorrectParameterSize, authenticationFailure, underlyingCoreCryptoError(error: Int32) }

public enum P256 {
    public enum Signing {
        public struct ECDSASignature: ContiguousBytes {
            public init<D: DataProtocol>(rawRepresentation: D) throws {}
            public init<D: DataProtocol>(derRepresentation: D) throws {}
            public var rawRepresentation: Data { Data() }
            public var derRepresentation: Data { Data() }
            public func withUnsafeBytes<R>(_ body: (UnsafeRawBufferPointer) throws -> R) rethrows -> R { try [UInt8]().withUnsafeBytes(body) }
        }
        public struct PublicKey {
            public init<D: ContiguousBytes>(rawRepresentation: D) throws {}
            public init<Bytes: ContiguousBytes>(x963Representation: Bytes) throws {}
            public init<Bytes: ContiguousBytes>(derRepresentation: Bytes) throws {}
            public var rawRepresentation: Data { Data() }
            public var x963Representation: Data { Data() }
            public var derRepresentation: Data { Data() }
            public func isValidSignature<D: DataProtocol>(_ signature: ECDSASignature, for data: D) -> Bool { false }
        }
        public struct PrivateKey {
            public init(compactRepresentable: Bool = true) {}
            public init<D: ContiguousBytes>(rawRepresentation: D) throws {}
            public init<Bytes: ContiguousBytes>(x963Representation: Bytes) throws {}
            public var publicKey: PublicKey { fatalError() }
            public var rawRepresentation: Data { Data() }
            public var x963Representation: Data { Data() }
            public func signature<D: DataProtocol>(for data: D) throws -> ECDSASignature { fatalError() }
        }
    }
}
