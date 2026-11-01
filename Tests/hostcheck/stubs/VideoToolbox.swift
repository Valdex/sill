// Stub of VideoToolbox: the compression session (VTCompressionSession.h, VTSession.h and the
// property keys), as Swift imports it.
@_exported import CoreMedia
import Foundation

public final class VTCompressionSession {}
public typealias VTSession = CFTypeRef
public struct VTEncodeInfoFlags: OptionSet {
    public var rawValue: UInt32
    public init(rawValue: UInt32) { self.rawValue = rawValue }
    public static let asynchronous = VTEncodeInfoFlags(rawValue: 1)
    public static let frameDropped = VTEncodeInfoFlags(rawValue: 2)
}
// A C function pointer on a Mac; a Swift function type here (its parameters are classes in this stub).
public typealias VTCompressionOutputCallback = (UnsafeMutableRawPointer?, UnsafeMutableRawPointer?, OSStatus, VTEncodeInfoFlags, CMSampleBuffer?) -> Void
public typealias VTCompressionOutputHandler = (OSStatus, VTEncodeInfoFlags, CMSampleBuffer?) -> Void

public func VTCompressionSessionCreate(allocator: CFAllocator?, width: Int32, height: Int32, codecType: CMVideoCodecType,
                                       encoderSpecification: CFDictionary?, imageBufferAttributes: CFDictionary?,
                                       compressedDataAllocator: CFAllocator?, outputCallback: VTCompressionOutputCallback?,
                                       refcon: UnsafeMutableRawPointer?, compressionSessionOut: UnsafeMutablePointer<VTCompressionSession?>) -> OSStatus { 0 }
public func VTSessionSetProperty(_ session: VTSession, key: CFString, value: CFTypeRef?) -> OSStatus { 0 }
public func VTSessionCopyProperty(_ session: VTSession, key: CFString, allocator: CFAllocator?, valueOut: UnsafeMutableRawPointer?) -> OSStatus { 0 }
public func VTCompressionSessionPrepareToEncodeFrames(_ session: VTCompressionSession) -> OSStatus { 0 }
public func VTCompressionSessionEncodeFrame(_ session: VTCompressionSession, imageBuffer: CVImageBuffer, presentationTimeStamp: CMTime,
                                            duration: CMTime, frameProperties: CFDictionary?, infoFlagsOut: UnsafeMutablePointer<VTEncodeInfoFlags>?,
                                            outputHandler: @escaping VTCompressionOutputHandler) -> OSStatus { 0 }
public func VTCompressionSessionCompleteFrames(_ session: VTCompressionSession, untilPresentationTimeStamp completeUntilPresentationTimeStamp: CMTime) -> OSStatus { 0 }
public func VTCompressionSessionInvalidate(_ session: VTCompressionSession) {}

public let kVTVideoEncoderSpecification_EnableHardwareAcceleratedVideoEncoder: CFString = "EnableHardwareAcceleratedVideoEncoder"
public let kVTVideoEncoderSpecification_RequireHardwareAcceleratedVideoEncoder: CFString = "RequireHardwareAcceleratedVideoEncoder"
public let kVTVideoEncoderSpecification_EnableLowLatencyRateControl: CFString = "EnableLowLatencyRateControl"
public let kVTCompressionPropertyKey_RealTime: CFString = "RealTime"
public let kVTCompressionPropertyKey_ProfileLevel: CFString = "ProfileLevel"
public let kVTCompressionPropertyKey_AllowFrameReordering: CFString = "AllowFrameReordering"
public let kVTCompressionPropertyKey_MaxKeyFrameInterval: CFString = "MaxKeyFrameInterval"
public let kVTCompressionPropertyKey_MaxKeyFrameIntervalDuration: CFString = "MaxKeyFrameIntervalDuration"
public let kVTCompressionPropertyKey_ExpectedFrameRate: CFString = "ExpectedFrameRate"
public let kVTCompressionPropertyKey_AverageBitRate: CFString = "AverageBitRate"
public let kVTCompressionPropertyKey_DataRateLimits: CFString = "DataRateLimits"
public let kVTCompressionPropertyKey_PrioritizeEncodingSpeedOverQuality: CFString = "PrioritizeEncodingSpeedOverQuality"
public let kVTCompressionPropertyKey_Quality: CFString = "Quality"
public let kVTCompressionPropertyKey_EncoderID: CFString = "EncoderID"
public let kVTCompressionPropertyKey_UsingHardwareAcceleratedVideoEncoder: CFString = "UsingHardwareAcceleratedVideoEncoder"
public let kVTProfileLevel_HEVC_Main_AutoLevel: CFString = "HEVC_Main_AutoLevel"
public let kVTEncodeFrameOptionKey_ForceKeyFrame: CFString = "ForceKeyFrame"
