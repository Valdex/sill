// Stub of CoreMedia: CMTime and its overlay, the host clock, sample, block and format buffers, as
// Swift imports CMTime.h, CMSync.h, CMSampleBuffer.h, CMBlockBuffer.h and CMFormatDescription.h.
@_exported import CoreAudio
@_exported import CoreVideo
import Foundation

public typealias CMTimeValue = Int64
public typealias CMTimeScale = Int32
public typealias CMTimeEpoch = Int64
public typealias CMItemCount = Int
public typealias CMVideoCodecType = FourCharCode
public typealias CMVideoFormatDescription = CMFormatDescription
public typealias CMAudioFormatDescription = CMFormatDescription

public struct CMTimeFlags: OptionSet, Hashable {
    public var rawValue: UInt32
    public init(rawValue: UInt32) { self.rawValue = rawValue }
    public static let valid = CMTimeFlags(rawValue: 1)
    public static let hasBeenRounded = CMTimeFlags(rawValue: 2)
    public static let positiveInfinity = CMTimeFlags(rawValue: 4)
    public static let negativeInfinity = CMTimeFlags(rawValue: 8)
    public static let indefinite = CMTimeFlags(rawValue: 16)
}
public struct CMTime: Hashable, Comparable, CustomStringConvertible {
    public var value: CMTimeValue
    public var timescale: CMTimeScale
    public var flags: CMTimeFlags
    public var epoch: CMTimeEpoch
    public init() { value = 0; timescale = 0; flags = []; epoch = 0 }
    public init(value: CMTimeValue, timescale: CMTimeScale, flags: CMTimeFlags, epoch: CMTimeEpoch) {
        self.value = value; self.timescale = timescale; self.flags = flags; self.epoch = epoch }
    public init(value: CMTimeValue, timescale: CMTimeScale) { self.init(value: value, timescale: timescale, flags: .valid, epoch: 0) }
    public init(seconds: Double, preferredTimescale: CMTimeScale) { self.init(value: 0, timescale: preferredTimescale) }
    public var seconds: Double { 0 }
    public var isValid: Bool { flags.contains(.valid) }
    public var isNumeric: Bool { isValid }
    public var isIndefinite: Bool { false }
    public var isPositiveInfinity: Bool { false }
    public var isNegativeInfinity: Bool { false }
    public var hasBeenRounded: Bool { false }
    public func convertScale(_ newTimescale: Int32, method: CMTimeRoundingMethod) -> CMTime { self }
    public static let invalid = CMTime()
    public static let zero = CMTime(value: 0, timescale: 1)
    public static let indefinite = CMTime()
    public static let positiveInfinity = CMTime()
    public static let negativeInfinity = CMTime()
    public static func < (a: CMTime, b: CMTime) -> Bool { false }
    public static func + (a: CMTime, b: CMTime) -> CMTime { a }
    public static func - (a: CMTime, b: CMTime) -> CMTime { a }
    public static func += (a: inout CMTime, b: CMTime) {}
    public var description: String { "" }
}
public enum CMTimeRoundingMethod: UInt32 { case roundHalfAwayFromZero = 1, roundTowardZero, roundAwayFromZero, quickTime, roundTowardPositiveInfinity, roundTowardNegativeInfinity
    public static var `default`: CMTimeRoundingMethod { .roundHalfAwayFromZero } }
public func CMTimeMake(value: Int64, timescale: Int32) -> CMTime { CMTime(value: value, timescale: timescale) }
public func CMTimeMakeWithSeconds(_ seconds: Float64, preferredTimescale: Int32) -> CMTime { CMTime() }
public func CMTimeGetSeconds(_ time: CMTime) -> Float64 { 0 }
public func CMTimeAdd(_ lhs: CMTime, _ rhs: CMTime) -> CMTime { lhs }
public func CMTimeSubtract(_ lhs: CMTime, _ rhs: CMTime) -> CMTime { lhs }
public func CMTimeCompare(_ time1: CMTime, _ time2: CMTime) -> Int32 { 0 }
public func CMTimeConvertScale(_ time: CMTime, timescale newTimescale: Int32, method: CMTimeRoundingMethod) -> CMTime { time }

public final class CMClock {}
public func CMClockGetHostTimeClock() -> CMClock { fatalError() }
public func CMClockGetTime(_ clock: CMClock) -> CMTime { CMTime() }
public func CMClockMakeHostTimeFromSystemUnits(_ hostTime: UInt64) -> CMTime { CMTime() }

public final class CMFormatDescription {}
public final class CMBlockBuffer {}
public final class CMSampleBuffer {}
public struct CMVideoDimensions { public var width: Int32; public var height: Int32; public init(width: Int32, height: Int32) { self.width = width; self.height = height } }

public var kCMVideoCodecType_HEVC: CMVideoCodecType { 0x68766331 }
public var kCMVideoCodecType_H264: CMVideoCodecType { 0x61766331 }
public let kCMSampleAttachmentKey_NotSync: CFString = "NotSync"
public let kCMSampleAttachmentKey_DependsOnOthers: CFString = "DependsOnOthers"
public var kCMSampleBufferFlag_AudioBufferList_Assure16ByteAlignment: UInt32 { 1 }

public func CMSampleBufferIsValid(_ sbuf: CMSampleBuffer) -> Bool { false }
public func CMSampleBufferDataIsReady(_ sbuf: CMSampleBuffer) -> Bool { false }
public func CMSampleBufferGetFormatDescription(_ sbuf: CMSampleBuffer) -> CMFormatDescription? { nil }
public func CMSampleBufferGetNumSamples(_ sbuf: CMSampleBuffer) -> CMItemCount { 0 }
public func CMSampleBufferGetPresentationTimeStamp(_ sbuf: CMSampleBuffer) -> CMTime { CMTime() }
public func CMSampleBufferGetDuration(_ sbuf: CMSampleBuffer) -> CMTime { CMTime() }
public func CMSampleBufferGetTotalSampleSize(_ sbuf: CMSampleBuffer) -> Int { 0 }
public func CMSampleBufferGetDataBuffer(_ sbuf: CMSampleBuffer) -> CMBlockBuffer? { nil }
public func CMSampleBufferGetImageBuffer(_ sbuf: CMSampleBuffer) -> CVImageBuffer? { nil }
public func CMSampleBufferGetSampleAttachmentsArray(_ sbuf: CMSampleBuffer, createIfNecessary: Bool) -> CFArray? { nil }
public func CMSampleBufferGetAudioBufferListWithRetainedBlockBuffer(
    _ sbuf: CMSampleBuffer, bufferListSizeNeededOut: UnsafeMutablePointer<Int>?, bufferListOut: UnsafeMutablePointer<AudioBufferList>?,
    bufferListSize: Int, blockBufferAllocator: CFAllocator?, blockBufferMemoryAllocator: CFAllocator?, flags: UInt32,
    blockBufferOut: UnsafeMutablePointer<CMBlockBuffer?>?) -> OSStatus { 0 }
public func CMBlockBufferGetDataLength(_ theBuffer: CMBlockBuffer) -> Int { 0 }
public func CMBlockBufferCopyDataBytes(_ theSourceBuffer: CMBlockBuffer, atOffset offsetToData: Int, dataLength: Int,
                                       destination: UnsafeMutableRawPointer) -> OSStatus { 0 }
public func CMAudioFormatDescriptionGetStreamBasicDescription(_ desc: CMAudioFormatDescription) -> UnsafePointer<AudioStreamBasicDescription>? { nil }
public func CMVideoFormatDescriptionGetDimensions(_ videoDesc: CMVideoFormatDescription) -> CMVideoDimensions { CMVideoDimensions(width: 0, height: 0) }
public func CMVideoFormatDescriptionGetHEVCParameterSetAtIndex(
    _ videoDesc: CMFormatDescription, parameterSetIndex: Int, parameterSetPointerOut: UnsafeMutablePointer<UnsafePointer<UInt8>?>?,
    parameterSetSizeOut: UnsafeMutablePointer<Int>?, parameterSetCountOut: UnsafeMutablePointer<Int>?,
    nalUnitHeaderLengthOut: UnsafeMutablePointer<Int32>?) -> OSStatus { 0 }
