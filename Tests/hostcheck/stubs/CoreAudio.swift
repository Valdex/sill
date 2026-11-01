// Stub of CoreAudio and CoreAudioTypes: the stream description, buffers and format constants (the
// structs are C, from c/coreaudio.h) and the overlay's UnsafeMutableAudioBufferListPointer.
@_exported import SillStubCoreAudioC

public typealias AudioFormatID = UInt32
public typealias AudioFormatFlags = UInt32
public var kAudioFormatLinearPCM: AudioFormatID { 0x6C70636D }
public var kAudioFormatMPEG4AAC: AudioFormatID { 0x61616320 }
public var kAudioFormatMPEG4AAC_ELD: AudioFormatID { 0x61616365 }
public var kAudioFormatOpus: AudioFormatID { 0x6F707573 }
public var kAudioFormatFlagIsFloat: AudioFormatFlags { 1 << 0 }
public var kAudioFormatFlagIsBigEndian: AudioFormatFlags { 1 << 1 }
public var kAudioFormatFlagIsSignedInteger: AudioFormatFlags { 1 << 2 }
public var kAudioFormatFlagIsPacked: AudioFormatFlags { 1 << 3 }
public var kAudioFormatFlagIsAlignedHigh: AudioFormatFlags { 1 << 4 }
public var kAudioFormatFlagIsNonInterleaved: AudioFormatFlags { 1 << 5 }
public var kAudioFormatFlagIsNonMixable: AudioFormatFlags { 1 << 6 }
public var kAudioFormatFlagsNativeFloatPacked: AudioFormatFlags { kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked }

public struct UnsafeMutableAudioBufferListPointer: MutableCollection, RandomAccessCollection {
    public init(_ p: UnsafeMutablePointer<AudioBufferList>) { unsafeMutablePointer = p }
    public init?(_ p: UnsafeMutablePointer<AudioBufferList>?) { guard let p else { return nil }; unsafeMutablePointer = p }
    public static func allocate(maximumBuffers: Int) -> UnsafeMutableAudioBufferListPointer { fatalError() }
    public var count: Int { get { Int(unsafeMutablePointer.pointee.mNumberBuffers) } nonmutating set {} }
    public var unsafePointer: UnsafePointer<AudioBufferList> { UnsafePointer(unsafeMutablePointer) }
    public var unsafeMutablePointer: UnsafeMutablePointer<AudioBufferList>
    public var startIndex: Int { 0 }
    public var endIndex: Int { count }
    public subscript(index: Int) -> AudioBuffer { get { fatalError() } nonmutating set {} }
}
