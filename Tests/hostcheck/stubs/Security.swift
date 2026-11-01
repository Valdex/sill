// Stub of Security: keys, certificates, identities, the keychain, code signing and the TLS options'
// sec_protocol_* calls (which Network's NWProtocolTLS hands out). Signatures as Swift imports
// Security's headers on macOS 14+. CF types are classes here, as on a Mac.
import Foundation
import Dispatch

public final class SecKey {}
public final class SecCertificate {}
public final class SecIdentity {}
public final class SecTrust {}
public final class SecAccessControl {}
public final class SecCode {}
public final class SecStaticCode {}
public final class SecTask {}
public final class SecPolicy {}
public typealias SecRandomRef = OpaquePointer

public let errSecSuccess: OSStatus = 0
public let errSecItemNotFound: OSStatus = -25300
public let errSecDuplicateItem: OSStatus = -25299
public let errSecAuthFailed: OSStatus = -25293
public let errSecInteractionNotAllowed: OSStatus = -25308
public let errSecMissingEntitlement: OSStatus = -34018
public let errSecUserCanceled: OSStatus = -128
public let errSecParam: OSStatus = -50
public let errSSLBadCert: OSStatus = -9808
public let errSSLPeerCertUnknown: OSStatus = -9829

public let kSecRandomDefault: SecRandomRef? = nil
public func SecRandomCopyBytes(_ rnd: SecRandomRef?, _ count: Int, _ bytes: UnsafeMutableRawPointer) -> Int32 { 0 }

public struct SecKeyAlgorithm: RawRepresentable, Hashable {
    public var rawValue: CFString
    public init(rawValue: CFString) { self.rawValue = rawValue }
    public static let ecdsaSignatureMessageX962SHA256 = SecKeyAlgorithm(rawValue: "algid:sign:ECDSA:message-X962:SHA256")
    public static let ecdsaSignatureDigestX962SHA256 = SecKeyAlgorithm(rawValue: "algid:sign:ECDSA:digest-X962:SHA256")
}

public func SecKeyGetTypeID() -> CFTypeID { 100 }
public func SecCertificateGetTypeID() -> CFTypeID { 101 }
public func SecIdentityGetTypeID() -> CFTypeID { 102 }
public func SecKeyCopyPublicKey(_ key: SecKey) -> SecKey? { nil }
public func SecKeyCopyAttributes(_ key: SecKey) -> CFDictionary? { nil }
public func SecKeyCopyExternalRepresentation(_ key: SecKey, _ error: UnsafeMutablePointer<Unmanaged<CFError>?>?) -> CFData? { nil }
public func SecKeyCreateSignature(_ key: SecKey, _ algorithm: SecKeyAlgorithm, _ dataToSign: CFData, _ error: UnsafeMutablePointer<Unmanaged<CFError>?>?) -> CFData? { nil }
public func SecKeyVerifySignature(_ key: SecKey, _ algorithm: SecKeyAlgorithm, _ signedData: CFData, _ signature: CFData, _ error: UnsafeMutablePointer<Unmanaged<CFError>?>?) -> Bool { false }
public func SecKeyCreateRandomKey(_ parameters: CFDictionary, _ error: UnsafeMutablePointer<Unmanaged<CFError>?>?) -> SecKey? { nil }
public func SecKeyCreateWithData(_ keyData: CFData, _ attributes: CFDictionary, _ error: UnsafeMutablePointer<Unmanaged<CFError>?>?) -> SecKey? { nil }
public func SecCertificateCreateWithData(_ allocator: CFAllocator?, _ data: CFData) -> SecCertificate? { nil }
public func SecCertificateCopyKey(_ certificate: SecCertificate) -> SecKey? { nil }
public func SecCertificateCopyData(_ certificate: SecCertificate) -> CFData { NSData() }
public func SecCertificateCopySubjectSummary(_ certificate: SecCertificate) -> CFString? { nil }
public func SecIdentityCreate(_ allocator: CFAllocator?, _ certificate: SecCertificate, _ privateKey: SecKey) -> SecIdentity? { nil }
public func SecIdentityCopyCertificate(_ identityRef: SecIdentity, _ certificateRef: UnsafeMutablePointer<SecCertificate?>) -> OSStatus { 0 }
public func SecIdentityCopyPrivateKey(_ identityRef: SecIdentity, _ privateKeyRef: UnsafeMutablePointer<SecKey?>) -> OSStatus { 0 }

public func SecItemCopyMatching(_ query: CFDictionary, _ result: UnsafeMutablePointer<CFTypeRef?>?) -> OSStatus { 0 }
public func SecItemAdd(_ attributes: CFDictionary, _ result: UnsafeMutablePointer<CFTypeRef?>?) -> OSStatus { 0 }
public func SecItemUpdate(_ query: CFDictionary, _ attributesToUpdate: CFDictionary) -> OSStatus { 0 }
public func SecItemDelete(_ query: CFDictionary) -> OSStatus { 0 }
public func SecCopyErrorMessageString(_ status: OSStatus, _ reserved: UnsafeMutableRawPointer?) -> CFString? { nil }

public let kSecClass: CFString = "class"
public let kSecClassKey: CFString = "keys"
public let kSecClassGenericPassword: CFString = "genp"
public let kSecClassCertificate: CFString = "cert"
public let kSecClassIdentity: CFString = "idnt"
public let kSecAttrKeyType: CFString = "type"
public let kSecAttrKeyTypeECSECPrimeRandom: CFString = "73"
public let kSecAttrKeyTypeEC: CFString = "73"
public let kSecAttrKeySizeInBits: CFString = "bsiz"
public let kSecAttrKeyClass: CFString = "kcls"
public let kSecAttrKeyClassPrivate: CFString = "1"
public let kSecAttrKeyClassPublic: CFString = "0"
public let kSecAttrIsPermanent: CFString = "perm"
public let kSecAttrApplicationTag: CFString = "atag"
public let kSecAttrApplicationLabel: CFString = "klbl"
public let kSecAttrLabel: CFString = "labl"
public let kSecAttrService: CFString = "svce"
public let kSecAttrAccount: CFString = "acct"
public let kSecAttrAccessible: CFString = "pdmn"
public let kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly: CFString = "cku"
public let kSecAttrAccessibleAfterFirstUnlock: CFString = "ck"
public let kSecAttrAccessibleWhenUnlocked: CFString = "ak"
public let kSecAttrAccessibleWhenUnlockedThisDeviceOnly: CFString = "aku"
public let kSecAttrAccessGroup: CFString = "agrp"
public let kSecAttrSynchronizable: CFString = "sync"
public let kSecAttrTokenID: CFString = "tkid"
public let kSecAttrTokenIDSecureEnclave: CFString = "com.apple.setoken"
public let kSecAttrAccessControl: CFString = "accc"
public let kSecPrivateKeyAttrs: CFString = "private"
public let kSecPublicKeyAttrs: CFString = "public"
public let kSecUseDataProtectionKeychain: CFString = "nleg"
public let kSecMatchLimit: CFString = "m_Limit"
public let kSecMatchLimitOne: CFString = "m_LimitOne"
public let kSecMatchLimitAll: CFString = "m_LimitAll"
public let kSecReturnRef: CFString = "r_Ref"
public let kSecReturnData: CFString = "r_Data"
public let kSecReturnAttributes: CFString = "r_Attributes"
public let kSecReturnPersistentRef: CFString = "r_PersistentRef"
public let kSecValueData: CFString = "v_Data"
public let kSecValueRef: CFString = "v_Ref"
public let kSecUseAuthenticationUI: CFString = "u_AuthUI"
public let kSecUseAuthenticationUIFail: CFString = "u_AuthUIF"

// Code signing (CSCommon.h, SecCode.h, SecStaticCode.h, SecTask.h).
public struct SecCSFlags: OptionSet {
    public var rawValue: UInt32
    public init(rawValue: UInt32) { self.rawValue = rawValue }
}
public let kSecCSSigningInformation: UInt32 = 1 << 1
public let kSecCSRequirementInformation: UInt32 = 1 << 2
public let kSecCSDynamicInformation: UInt32 = 1 << 3
public struct SecCodeSignatureFlags: OptionSet {
    public var rawValue: UInt32
    public init(rawValue: UInt32) { self.rawValue = rawValue }
    public static let host = SecCodeSignatureFlags(rawValue: 0x0001)
    public static let adhoc = SecCodeSignatureFlags(rawValue: 0x0002)
    public static let runtime = SecCodeSignatureFlags(rawValue: 0x10000)
}
public let kSecCodeInfoIdentifier: CFString = "identifier"
public let kSecCodeInfoFlags: CFString = "flags"
public let kSecCodeInfoCertificates: CFString = "certificates"
public let kSecCodeInfoTeamIdentifier: CFString = "teamid"
public let kSecCodeInfoEntitlementsDict: CFString = "entitlements-dict"
public func SecCodeCopySelf(_ flags: SecCSFlags, _ self: UnsafeMutablePointer<SecCode?>) -> OSStatus { 0 }
public func SecCodeCopyStaticCode(_ code: SecCode, _ flags: SecCSFlags, _ staticCode: UnsafeMutablePointer<SecStaticCode?>) -> OSStatus { 0 }
public func SecCodeCopySigningInformation(_ code: SecStaticCode, _ flags: SecCSFlags, _ information: UnsafeMutablePointer<CFDictionary?>) -> OSStatus { 0 }
public func SecTaskCreateFromSelf(_ allocator: CFAllocator?) -> SecTask? { nil }
public func SecTaskCopySigningIdentifier(_ task: SecTask, _ error: UnsafeMutablePointer<Unmanaged<CFError>?>?) -> CFString? { nil }
public func SecTaskCopyValueForEntitlement(_ task: SecTask, _ entitlement: CFString, _ error: UnsafeMutablePointer<Unmanaged<CFError>?>?) -> CFTypeRef? { nil }

// The TLS options and metadata (SecProtocolOptions.h, SecProtocolMetadata.h, SecProtocolObject.h):
// Objective-C protocol objects on a Mac, classes here.
public final class sec_protocol_options_t {}
public final class sec_protocol_metadata_t {}
public final class sec_identity_t {}
public final class sec_certificate_t {}
public final class sec_trust_t {}
public enum tls_protocol_version_t: UInt16 {
    case TLSv10 = 0x0301, TLSv11 = 0x0302, TLSv12 = 0x0303, TLSv13 = 0x0304, DTLSv10 = 0xfeff, DTLSv12 = 0xfefd
}
public typealias sec_protocol_verify_complete_t = (Bool) -> Void
public typealias sec_protocol_verify_t = (sec_protocol_metadata_t, sec_trust_t, @escaping sec_protocol_verify_complete_t) -> Void
public func sec_identity_create(_ identity: SecIdentity) -> sec_identity_t? { nil }
public func sec_identity_copy_ref(_ identity: sec_identity_t) -> Unmanaged<SecIdentity>? { nil }
public func sec_certificate_copy_ref(_ certificate: sec_certificate_t) -> Unmanaged<SecCertificate> { fatalError() }
public func sec_trust_copy_ref(_ trust: sec_trust_t) -> Unmanaged<SecTrust> { fatalError() }
public func sec_protocol_options_set_min_tls_protocol_version(_ options: sec_protocol_options_t, _ version: tls_protocol_version_t) {}
public func sec_protocol_options_set_max_tls_protocol_version(_ options: sec_protocol_options_t, _ version: tls_protocol_version_t) {}
public func sec_protocol_options_set_local_identity(_ options: sec_protocol_options_t, _ identity: sec_identity_t) {}
public func sec_protocol_options_set_tls_tickets_enabled(_ options: sec_protocol_options_t, _ tickets_enabled: Bool) {}
public func sec_protocol_options_set_tls_resumption_enabled(_ options: sec_protocol_options_t, _ resumption_enabled: Bool) {}
public func sec_protocol_options_set_peer_authentication_required(_ options: sec_protocol_options_t, _ peer_authentication_required: Bool) {}
public func sec_protocol_options_add_tls_application_protocol(_ options: sec_protocol_options_t, _ application_protocol: UnsafePointer<CChar>) {}
public func sec_protocol_options_set_tls_server_name(_ options: sec_protocol_options_t, _ server_name: UnsafePointer<CChar>) {}
public func sec_protocol_options_set_verify_block(_ options: sec_protocol_options_t, _ verify_block: @escaping sec_protocol_verify_t, _ verify_block_queue: DispatchQueue) {}
public func sec_protocol_metadata_access_peer_certificate_chain(_ metadata: sec_protocol_metadata_t, _ handler: @escaping (sec_certificate_t) -> Void) -> Bool { false }
@available(macOS 15.5, iOS 18.5, watchOS 11.5, tvOS 18.5, *)
public func sec_protocol_metadata_copy_negotiated_protocol(_ metadata: sec_protocol_metadata_t) -> UnsafePointer<CChar>? { nil }
@available(macOS, deprecated: 15.5)
public func sec_protocol_metadata_get_negotiated_protocol(_ metadata: sec_protocol_metadata_t) -> UnsafePointer<CChar>? { nil }
public func sec_protocol_metadata_get_negotiated_tls_protocol_version(_ metadata: sec_protocol_metadata_t) -> tls_protocol_version_t { .TLSv13 }
