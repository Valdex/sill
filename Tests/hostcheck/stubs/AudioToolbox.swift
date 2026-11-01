// Stub of AudioToolbox: AudioConverter and AudioFormat, as Swift imports AudioConverter.h and
// AudioFormat.h (the converter, its callback and its structs are C, from c/coreaudio.h).
@_exported import CoreAudio

public typealias AudioConverterPropertyID = UInt32
public typealias AudioFormatPropertyID = UInt32
public var kAudioConverterEncodeBitRate: AudioConverterPropertyID { 0x62726174 }
public var kAudioConverterCurrentOutputStreamDescription: AudioConverterPropertyID { 0x61636f64 }
public var kAudioConverterCurrentInputStreamDescription: AudioConverterPropertyID { 0x61636964 }
public var kAudioConverterPrimeInfo: AudioConverterPropertyID { 0x7072696d }
public var kAudioConverterCompressionMagicCookie: AudioConverterPropertyID { 0x636d676b }
public var kAudioConverterPropertyMaximumOutputPacketSize: AudioConverterPropertyID { 0x786f7073 }
public var kAudioConverterCodecQuality: AudioConverterPropertyID { 0x63647175 }
public var kAudioFormatProperty_FormatInfo: AudioFormatPropertyID { 0x666d7469 }

public func AudioConverterNew(_ inSourceFormat: UnsafePointer<AudioStreamBasicDescription>, _ inDestinationFormat: UnsafePointer<AudioStreamBasicDescription>,
                              _ outAudioConverter: UnsafeMutablePointer<AudioConverterRef?>) -> OSStatus { 0 }
public func AudioConverterDispose(_ inAudioConverter: AudioConverterRef) -> OSStatus { 0 }
public func AudioConverterReset(_ inAudioConverter: AudioConverterRef) -> OSStatus { 0 }
public func AudioConverterSetProperty(_ inAudioConverter: AudioConverterRef, _ inPropertyID: AudioConverterPropertyID,
                                      _ inPropertyDataSize: UInt32, _ inPropertyData: UnsafeRawPointer) -> OSStatus { 0 }
public func AudioConverterGetProperty(_ inAudioConverter: AudioConverterRef, _ inPropertyID: AudioConverterPropertyID,
                                      _ ioPropertyDataSize: UnsafeMutablePointer<UInt32>, _ outPropertyData: UnsafeMutableRawPointer) -> OSStatus { 0 }
public func AudioConverterGetPropertyInfo(_ inAudioConverter: AudioConverterRef, _ inPropertyID: AudioConverterPropertyID,
                                          _ outSize: UnsafeMutablePointer<UInt32>?, _ outWritable: UnsafeMutablePointer<DarwinBoolean>?) -> OSStatus { 0 }
public func AudioConverterFillComplexBuffer(_ inAudioConverter: AudioConverterRef, _ inInputDataProc: AudioConverterComplexInputDataProc,
                                            _ inInputDataProcUserData: UnsafeMutableRawPointer?, _ ioOutputDataPacketSize: UnsafeMutablePointer<UInt32>,
                                            _ outOutputData: UnsafeMutablePointer<AudioBufferList>,
                                            _ outPacketDescription: UnsafeMutablePointer<AudioStreamPacketDescription>?) -> OSStatus { 0 }
public func AudioFormatGetProperty(_ inPropertyID: AudioFormatPropertyID, _ inSpecifierSize: UInt32, _ inSpecifier: UnsafeRawPointer?,
                                   _ ioPropertyDataSize: UnsafeMutablePointer<UInt32>?, _ outPropertyData: UnsafeMutableRawPointer?) -> OSStatus { 0 }
