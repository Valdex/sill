// Stub of CommonCrypto: PBKDF2 (CommonKeyDerivation.h) as Swift imports it.
import Foundation

public typealias CCPBKDFAlgorithm = UInt32
public typealias CCPseudoRandomAlgorithm = UInt32
public typealias CCStatus = Int32
public var kCCPBKDF2: Int { 2 }
public var kCCPRFHmacAlgSHA1: Int { 1 }
public var kCCPRFHmacAlgSHA256: Int { 3 }
public var kCCSuccess: Int { 0 }
public var kCCParamError: Int { -4300 }
public func CCKeyDerivationPBKDF(_ algorithm: CCPBKDFAlgorithm, _ password: UnsafePointer<CChar>!, _ passwordLen: Int,
                                 _ salt: UnsafePointer<UInt8>!, _ saltLen: Int, _ prf: CCPseudoRandomAlgorithm,
                                 _ rounds: UInt32, _ derivedKey: UnsafeMutablePointer<UInt8>!, _ derivedKeyLen: Int) -> Int32 { 0 }
