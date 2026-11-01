// Stub of ScreenCaptureKit (macOS 14+): shareable content, content filters, the stream and its
// configuration and outputs, screenshots. ScreenCaptureKit re-exports Foundation, CoreMedia and
// CoreGraphics, as its headers import them.
@_exported import Foundation
@_exported import CoreMedia
@_exported import CoreGraphics
import Dispatch

public final class SCRunningApplication: NSObject {
    public var bundleIdentifier: String { "" }
    public var applicationName: String { "" }
    public var processID: pid_t { 0 }
}
public final class SCWindow: NSObject {
    public var windowID: CGWindowID { 0 }
    public var frame: CGRect { .zero }
    public var title: String? { nil }
    public var windowLayer: Int { 0 }
    public var owningApplication: SCRunningApplication? { nil }
    public var isOnScreen: Bool { false }
    public var isActive: Bool { false }
}
public final class SCDisplay: NSObject {
    public var displayID: CGDirectDisplayID { 0 }
    public var width: Int { 0 }
    public var height: Int { 0 }
    public var frame: CGRect { .zero }
}
public final class SCShareableContent: NSObject {
    public var windows: [SCWindow] { [] }
    public var displays: [SCDisplay] { [] }
    public var applications: [SCRunningApplication] { [] }
    public static func getWithCompletionHandler(_ completionHandler: @escaping (SCShareableContent?, Error?) -> Void) {}
    public static func getExcludingDesktopWindows(_ excludeDesktopWindows: Bool, onScreenWindowsOnly: Bool,
                                                  completionHandler: @escaping (SCShareableContent?, Error?) -> Void) {}
    public static func getExcludingDesktopWindows(_ excludeDesktopWindows: Bool, onScreenWindowsOnlyBelow window: SCWindow,
                                                  completionHandler: @escaping (SCShareableContent?, Error?) -> Void) {}
    public static func getExcludingDesktopWindows(_ excludeDesktopWindows: Bool, onScreenWindowsOnlyAbove window: SCWindow,
                                                  completionHandler: @escaping (SCShareableContent?, Error?) -> Void) {}
    public static var current: SCShareableContent { get async throws { fatalError() } }
    public static func excludingDesktopWindows(_ excludeDesktopWindows: Bool, onScreenWindowsOnly: Bool) async throws -> SCShareableContent { fatalError() }
    public static func excludingDesktopWindows(_ excludeDesktopWindows: Bool, onScreenWindowsOnlyBelow window: SCWindow) async throws -> SCShareableContent { fatalError() }
    public static func excludingDesktopWindows(_ excludeDesktopWindows: Bool, onScreenWindowsOnlyAbove window: SCWindow) async throws -> SCShareableContent { fatalError() }
}

public enum SCShareableContentStyle: Int { case none, window, display, application }
public enum SCStreamType: Int { case window, display }
public final class SCContentFilter: NSObject {
    public init(desktopIndependentWindow window: SCWindow) {}
    public init(display: SCDisplay, excludingWindows excluded: [SCWindow]) {}
    public init(display: SCDisplay, including includedWindows: [SCWindow]) {}
    public init(display: SCDisplay, including applications: [SCRunningApplication], exceptingWindows: [SCWindow]) {}
    public init(display: SCDisplay, excludingApplications applications: [SCRunningApplication], exceptingWindows: [SCWindow]) {}
    public var style: SCShareableContentStyle { .none }
    public var streamType: SCStreamType { .window }
    public var contentRect: CGRect { .zero }
    public var pointPixelScale: Float { 1 }
    public var includeMenuBar: Bool = false
}

public enum SCCaptureResolutionType: Int { case automatic, best, nominal }
public final class SCStreamConfiguration: NSObject {
    public override init() {}
    public var width: Int = 0
    public var height: Int = 0
    public var minimumFrameInterval: CMTime = .zero
    public var pixelFormat: OSType = 0
    public var scalesToFit: Bool = false
    public var preservesAspectRatio: Bool = true
    public var sourceRect: CGRect = .zero
    public var destinationRect: CGRect = .zero
    public var queueDepth: Int = 8
    public var showsCursor: Bool = true
    public var showMouseClicks: Bool = false
    public unowned(unsafe) var backgroundColor: CGColor = CGColor.black
    public var colorSpaceName: CFString = "kCGColorSpaceSRGB"
    public var colorMatrix: CFString = ""
    public var capturesAudio: Bool = false
    public var sampleRate: Int = 48000
    public var channelCount: Int = 2
    public var excludesCurrentProcessAudio: Bool = false
    public var captureMicrophone: Bool = false
    public var ignoreShadowsDisplay: Bool = false
    public var ignoreShadowsSingleWindow: Bool = false
    public var ignoreGlobalClipDisplay: Bool = false
    public var ignoreGlobalClipSingleWindow: Bool = false
    public var shouldBeOpaque: Bool = false
    public var capturesShadowsOnly: Bool = false
    public var includeChildWindows: Bool = true
    public var captureResolution: SCCaptureResolutionType = .automatic
    public var streamName: String?
}

public enum SCStreamOutputType: Int { case screen = 0, audio = 1, microphone = 2 }
public enum SCFrameStatus: Int { case complete = 0, idle, blank, suspended, started, stopped }
public struct SCStreamFrameInfo: RawRepresentable, Hashable {
    public var rawValue: String
    public init(rawValue: String) { self.rawValue = rawValue }
    public init(_ rawValue: String) { self.rawValue = rawValue }
    public static let status = SCStreamFrameInfo(rawValue: "SCStreamUpdateFrameStatus")
    public static let displayTime = SCStreamFrameInfo(rawValue: "SCStreamUpdateFrameDisplayTime")
    public static let scaleFactor = SCStreamFrameInfo(rawValue: "SCStreamUpdateFrameScaleFactor")
    public static let contentScale = SCStreamFrameInfo(rawValue: "SCStreamUpdateFrameContentScale")
    public static let contentRect = SCStreamFrameInfo(rawValue: "SCStreamUpdateFrameContentRect")
    public static let dirtyRects = SCStreamFrameInfo(rawValue: "SCStreamUpdateFrameDirtyRects")
    public static let screenRect = SCStreamFrameInfo(rawValue: "SCStreamUpdateFrameScreenRect")
}
public let SCStreamErrorDomain: String = "com.apple.ScreenCaptureKit.SCStreamErrorDomain"
public struct SCStreamError: CustomNSError, Hashable {
    public enum Code: Int, Equatable {
        case userDeclined = -3801, failedToStart = -3802, missingEntitlements = -3803, failedApplicationConnectionInvalid = -3804,
             failedApplicationConnectionInterrupted = -3805, failedNoMatchingApplicationContext = -3806,
             attemptToStartStreamState = -3807, attemptToStopStreamState = -3808, attemptToUpdateFilterState = -3809,
             attemptToConfigState = -3810, internalError = -3811, invalidParameter = -3812, noWindowList = -3813,
             noDisplayList = -3814, noCaptureSource = -3815, removingStream = -3816, userStopped = -3817,
             failedToStartAudioCapture = -3818, failedToStopAudioCapture = -3819, failedToStartMicrophoneCapture = -3820,
             systemStoppedStream = -3821
    }
    public let code: Code
    public static var errorDomain: String { SCStreamErrorDomain }
    public var errorCode: Int { code.rawValue }
    public static var userDeclined: Code { .userDeclined }
    public static var userStopped: Code { .userStopped }
}

public protocol SCStreamOutput: NSObjectProtocol {
    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType)
}
extension SCStreamOutput {
    public func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {}
}
public protocol SCStreamDelegate: NSObjectProtocol {
    func stream(_ stream: SCStream, didStopWithError error: Error)
    func outputVideoEffectDidStart(for stream: SCStream)
    func outputVideoEffectDidStop(for stream: SCStream)
}
extension SCStreamDelegate {
    public func stream(_ stream: SCStream, didStopWithError error: Error) {}
    public func outputVideoEffectDidStart(for stream: SCStream) {}
    public func outputVideoEffectDidStop(for stream: SCStream) {}
}

public final class SCStream: NSObject {
    public init(filter contentFilter: SCContentFilter, configuration streamConfig: SCStreamConfiguration, delegate: SCStreamDelegate?) {}
    public func addStreamOutput(_ output: SCStreamOutput, type: SCStreamOutputType, sampleHandlerQueue: DispatchQueue?) throws {}
    public func removeStreamOutput(_ output: SCStreamOutput, type: SCStreamOutputType) throws {}
    public func startCapture(completionHandler: ((Error?) -> Void)? = nil) {}
    public func stopCapture(completionHandler: ((Error?) -> Void)? = nil) {}
    public func startCapture() async throws {}
    public func stopCapture() async throws {}
    public func updateConfiguration(_ streamConfig: SCStreamConfiguration, completionHandler: ((Error?) -> Void)? = nil) {}
    public func updateConfiguration(_ streamConfig: SCStreamConfiguration) async throws {}
    public func updateContentFilter(_ contentFilter: SCContentFilter, completionHandler: ((Error?) -> Void)? = nil) {}
    public func updateContentFilter(_ contentFilter: SCContentFilter) async throws {}
}

public final class SCScreenshotManager: NSObject {
    public static func captureImage(contentFilter: SCContentFilter, configuration config: SCStreamConfiguration,
                                    completionHandler: ((CGImage?, Error?) -> Void)? = nil) {}
    public static func captureImage(contentFilter: SCContentFilter, configuration config: SCStreamConfiguration) async throws -> CGImage { fatalError() }
    public static func captureSampleBuffer(contentFilter: SCContentFilter, configuration config: SCStreamConfiguration) async throws -> CMSampleBuffer { fatalError() }
}
