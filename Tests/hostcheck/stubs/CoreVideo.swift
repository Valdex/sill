// Stub of CoreVideo: pixel buffers and their pools (CVPixelBuffer.h, CVPixelBufferPool.h), as Swift
// imports them. CVPixelBuffer is CVImageBuffer is CVBuffer, as on a Mac.
@_exported import CoreGraphics
@_exported import IOSurface
import Foundation

public final class CVBuffer {}
public typealias CVImageBuffer = CVBuffer
public typealias CVPixelBuffer = CVImageBuffer
public final class CVPixelBufferPool {}
public typealias CVReturn = Int32
public typealias CVOptionFlags = UInt64
public let kCVReturnSuccess: CVReturn = 0
public let kCVReturnError: CVReturn = -6660

public struct CVPixelBufferLockFlags: OptionSet {
    public var rawValue: CVOptionFlags
    public init(rawValue: CVOptionFlags) { self.rawValue = rawValue }
    public static let readOnly = CVPixelBufferLockFlags(rawValue: 1)
}

public var kCVPixelFormatType_32BGRA: OSType { 0x42475241 }
public var kCVPixelFormatType_32ARGB: OSType { 0x00000020 }
public var kCVPixelFormatType_420YpCbCr8BiPlanarFullRange: OSType { 0x34323066 }
public var kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange: OSType { 0x34323076 }
public let kCVPixelBufferPixelFormatTypeKey: CFString = "PixelFormatType"
public let kCVPixelBufferWidthKey: CFString = "Width"
public let kCVPixelBufferHeightKey: CFString = "Height"
public let kCVPixelBufferIOSurfacePropertiesKey: CFString = "IOSurfaceProperties"
public let kCVPixelBufferBytesPerRowAlignmentKey: CFString = "BytesPerRowAlignment"
public let kCVPixelBufferPoolMinimumBufferCountKey: CFString = "MinimumBufferCount"
public let kCVPixelBufferMetalCompatibilityKey: CFString = "MetalCompatibility"

public func CVPixelBufferCreate(_ allocator: CFAllocator?, _ width: Int, _ height: Int, _ pixelFormatType: OSType,
                                _ pixelBufferAttributes: CFDictionary?, _ pixelBufferOut: UnsafeMutablePointer<CVPixelBuffer?>) -> CVReturn { 0 }
public func CVPixelBufferPoolCreate(_ allocator: CFAllocator?, _ poolAttributes: CFDictionary?, _ pixelBufferAttributes: CFDictionary?,
                                    _ poolOut: UnsafeMutablePointer<CVPixelBufferPool?>) -> CVReturn { 0 }
public func CVPixelBufferPoolCreatePixelBuffer(_ allocator: CFAllocator?, _ pixelBufferPool: CVPixelBufferPool,
                                               _ pixelBufferOut: UnsafeMutablePointer<CVPixelBuffer?>) -> CVReturn { 0 }
public func CVPixelBufferLockBaseAddress(_ pixelBuffer: CVPixelBuffer, _ lockFlags: CVPixelBufferLockFlags) -> CVReturn { 0 }
public func CVPixelBufferUnlockBaseAddress(_ pixelBuffer: CVPixelBuffer, _ unlockFlags: CVPixelBufferLockFlags) -> CVReturn { 0 }
public func CVPixelBufferGetWidth(_ pixelBuffer: CVPixelBuffer) -> Int { 0 }
public func CVPixelBufferGetHeight(_ pixelBuffer: CVPixelBuffer) -> Int { 0 }
public func CVPixelBufferGetBytesPerRow(_ pixelBuffer: CVPixelBuffer) -> Int { 0 }
public func CVPixelBufferGetBaseAddress(_ pixelBuffer: CVPixelBuffer) -> UnsafeMutableRawPointer? { nil }
public func CVPixelBufferGetPixelFormatType(_ pixelBuffer: CVPixelBuffer) -> OSType { 0 }
public func CVPixelBufferGetPlaneCount(_ pixelBuffer: CVPixelBuffer) -> Int { 0 }
public func CVPixelBufferGetWidthOfPlane(_ pixelBuffer: CVPixelBuffer, _ planeIndex: Int) -> Int { 0 }
public func CVPixelBufferGetHeightOfPlane(_ pixelBuffer: CVPixelBuffer, _ planeIndex: Int) -> Int { 0 }
public func CVPixelBufferGetBytesPerRowOfPlane(_ pixelBuffer: CVPixelBuffer, _ planeIndex: Int) -> Int { 0 }
public func CVPixelBufferGetBaseAddressOfPlane(_ pixelBuffer: CVPixelBuffer, _ planeIndex: Int) -> UnsafeMutableRawPointer? { nil }
public func CVPixelBufferIsPlanar(_ pixelBuffer: CVPixelBuffer) -> Bool { false }
public func CVPixelBufferGetIOSurface(_ pixelBuffer: CVPixelBuffer?) -> Unmanaged<IOSurfaceRef>? { nil }
public func CVBufferSetAttachment(_ buffer: CVBuffer, _ key: CFString, _ value: CFTypeRef, _ attachmentMode: CVAttachmentMode) {}
public func CVBufferGetAttachment(_ buffer: CVBuffer, _ key: CFString, _ attachmentMode: UnsafeMutablePointer<CVAttachmentMode>?) -> Unmanaged<CFTypeRef>? { nil }
public enum CVAttachmentMode: UInt32 { case shouldNotPropagate = 0, shouldPropagate = 1 }
