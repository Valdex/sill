// Stub of CoreImage: images, contexts and the built-in filters Sill.app's QR code uses
// (CIFilterBuiltins, a submodule on a Mac, comes with the module here).
@_exported import CoreGraphics
import Foundation

open class CIImage: NSObject {
    public init(cgImage image: CGImage) {}
    public init(cgImage image: CGImage, options: [CIImageOption: Any]?) {}
    open var extent: CGRect { .zero }
    open func transformed(by matrix: CGAffineTransform) -> CIImage { self }
    open func cropped(to rect: CGRect) -> CIImage { self }
    open func applyingFilter(_ filterName: String, parameters params: [String: Any]) -> CIImage { self }
}
public struct CIImageOption: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue } }
public struct CIContextOption: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue }
    public static let useSoftwareRenderer = CIContextOption(rawValue: "kCIContextUseSoftwareRenderer")
    public static let workingColorSpace = CIContextOption(rawValue: "kCIContextWorkingColorSpace")
    public static let outputColorSpace = CIContextOption(rawValue: "kCIContextOutputColorSpace") }
open class CIContext: NSObject {
    public override init() {}
    public init(options: [CIContextOption: Any]?) {}
    open func createCGImage(_ image: CIImage, from fromRect: CGRect) -> CGImage? { nil }
}
open class CIFilter: NSObject {
    public init?(name: String) {}
    public init?(name: String, parameters params: [String: Any]?) {}
    open var outputImage: CIImage? { nil }
    open func setValue(_ value: Any?, forKey key: String) {}
    open func setDefaults() {}
    public class func qrCodeGenerator() -> CIFilter & CIQRCodeGenerator { fatalError() }
}
public protocol CIFilterProtocol: AnyObject { var outputImage: CIImage? { get } }
public protocol CIQRCodeGenerator: CIFilterProtocol {
    var message: Data { get set }
    var correctionLevel: String { get set }
}
