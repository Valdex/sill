// Stub of QuartzCore: the media clock and the layers the app's views hold.
@_exported import CoreGraphics
@_exported import CoreVideo
@_exported import Foundation
public func CACurrentMediaTime() -> CFTimeInterval { 0 }
open class CALayer: NSObject {
    public override init() {}
    open var contents: Any?
    open var frame: CGRect = .zero
    open var bounds: CGRect = .zero
    open var position: CGPoint = .zero
    open var backgroundColor: CGColor?
    open var borderColor: CGColor?
    open var borderWidth: CGFloat = 0
    open var cornerRadius: CGFloat = 0
    open var masksToBounds: Bool = false
    open var contentsScale: CGFloat = 1
    open var contentsGravity: CALayerContentsGravity = .resize
    open var opacity: Float = 1
    open var isHidden: Bool = false
    open var sublayers: [CALayer]?
    open func addSublayer(_ layer: CALayer) {}
    open func removeFromSuperlayer() {}
    open func setNeedsDisplay() {}
}
public struct CALayerContentsGravity: RawRepresentable, Hashable { public var rawValue: String; public init(rawValue: String) { self.rawValue = rawValue }
    public static let resize = CALayerContentsGravity(rawValue: "resize"); public static let resizeAspect = CALayerContentsGravity(rawValue: "resizeAspect")
    public static let center = CALayerContentsGravity(rawValue: "center") }
