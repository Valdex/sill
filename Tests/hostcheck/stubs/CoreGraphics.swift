// Stub of CoreGraphics: displays, the window list, events, images, contexts and color, as Swift
// imports them on macOS 14+. The geometry structs (CGFloat, CGPoint, CGSize, CGRect) are Linux
// Foundation's, re-exported here as CoreGraphics re-exports its own; CGAffineTransform and
// CGVector, which Linux's Foundation lacks, are here.
@_exported import struct Foundation.CGFloat
@_exported import struct Foundation.CGPoint
@_exported import struct Foundation.CGSize
@_exported import struct Foundation.CGRect
@_exported import enum Foundation.CGRectEdge
import Foundation

public typealias CGDirectDisplayID = UInt32
public typealias CGWindowID = UInt32
public typealias CGKeyCode = UInt16
public typealias CGCharCode = UInt16
public typealias CGWindowLevel = Int32
public typealias CGEventTimestamp = UInt64
public typealias CGDisplayConfigRef = OpaquePointer
public let kCGNullWindowID: CGWindowID = 0
public let kCGNullDirectDisplay: CGDirectDisplayID = 0

public enum CGError: Int32 {
    case success = 0, failure = 1000, illegalArgument = 1001, invalidConnection = 1002, invalidContext = 1003,
         cannotComplete = 1004, notImplemented = 1006, rangeCheck = 1007, typeCheck = 1008, invalidOperation = 1010,
         noneAvailable = 1011
}

public struct CGVector: Equatable { public var dx: CGFloat; public var dy: CGFloat
    public init() { dx = 0; dy = 0 }
    public init(dx: CGFloat, dy: CGFloat) { self.dx = dx; self.dy = dy }
    public static var zero: CGVector { CGVector() } }

public struct CGAffineTransform: Equatable {
    public var a: CGFloat, b: CGFloat, c: CGFloat, d: CGFloat, tx: CGFloat, ty: CGFloat
    public init() { a = 1; b = 0; c = 0; d = 1; tx = 0; ty = 0 }
    public init(a: CGFloat, b: CGFloat, c: CGFloat, d: CGFloat, tx: CGFloat, ty: CGFloat) { self.a = a; self.b = b; self.c = c; self.d = d; self.tx = tx; self.ty = ty }
    public init(translationX tx: CGFloat, y ty: CGFloat) { self.init(); self.tx = tx; self.ty = ty }
    public init(scaleX sx: CGFloat, y sy: CGFloat) { self.init(); a = sx; d = sy }
    public init(rotationAngle angle: CGFloat) { self.init() }
    public static var identity: CGAffineTransform { CGAffineTransform() }
    public var isIdentity: Bool { self == .identity }
    public func translatedBy(x: CGFloat, y: CGFloat) -> CGAffineTransform { self }
    public func scaledBy(x: CGFloat, y: CGFloat) -> CGAffineTransform { self }
    public func rotated(by angle: CGFloat) -> CGAffineTransform { self }
    public func inverted() -> CGAffineTransform { self }
    public func concatenating(_ t2: CGAffineTransform) -> CGAffineTransform { self }
}
extension CGPoint { public func applying(_ t: CGAffineTransform) -> CGPoint { self } }
extension CGSize { public func applying(_ t: CGAffineTransform) -> CGSize { self } }
extension CGRect { public func applying(_ t: CGAffineTransform) -> CGRect { self }
    public func equalTo(_ rect2: CGRect) -> Bool { self == rect2 } }

// Displays.
public func CGMainDisplayID() -> CGDirectDisplayID { 1 }
public func CGDisplayBounds(_ display: CGDirectDisplayID) -> CGRect { .zero }
public func CGDisplayPixelsWide(_ display: CGDirectDisplayID) -> Int { 0 }
public func CGDisplayPixelsHigh(_ display: CGDirectDisplayID) -> Int { 0 }
public func CGDisplayScreenSize(_ display: CGDirectDisplayID) -> CGSize { .zero }
public func CGDisplayIsAsleep(_ display: CGDirectDisplayID) -> boolean_t { 0 }
public func CGDisplayIsActive(_ display: CGDirectDisplayID) -> boolean_t { 0 }
public func CGDisplayIsOnline(_ display: CGDirectDisplayID) -> boolean_t { 0 }
public func CGDisplayIsMain(_ display: CGDirectDisplayID) -> boolean_t { 0 }
public func CGDisplayIsBuiltin(_ display: CGDirectDisplayID) -> boolean_t { 0 }
public func CGDisplayIsInMirrorSet(_ display: CGDirectDisplayID) -> boolean_t { 0 }
public func CGDisplayMirrorsDisplay(_ display: CGDirectDisplayID) -> CGDirectDisplayID { 0 }
public func CGDisplayVendorNumber(_ display: CGDirectDisplayID) -> UInt32 { 0 }
public func CGDisplayModelNumber(_ display: CGDirectDisplayID) -> UInt32 { 0 }
public func CGDisplaySerialNumber(_ display: CGDirectDisplayID) -> UInt32 { 0 }
public func CGDisplayUnitNumber(_ display: CGDirectDisplayID) -> UInt32 { 0 }
public func CGGetOnlineDisplayList(_ maxDisplays: UInt32, _ onlineDisplays: UnsafeMutablePointer<CGDirectDisplayID>?, _ displayCount: UnsafeMutablePointer<UInt32>?) -> CGError { .success }
public func CGGetActiveDisplayList(_ maxDisplays: UInt32, _ activeDisplays: UnsafeMutablePointer<CGDirectDisplayID>?, _ displayCount: UnsafeMutablePointer<UInt32>?) -> CGError { .success }
public func CGGetDisplaysWithPoint(_ point: CGPoint, _ maxDisplays: UInt32, _ displays: UnsafeMutablePointer<CGDirectDisplayID>?, _ matchingDisplayCount: UnsafeMutablePointer<UInt32>?) -> CGError { .success }
public func CGGetDisplaysWithRect(_ rect: CGRect, _ maxDisplays: UInt32, _ displays: UnsafeMutablePointer<CGDirectDisplayID>?, _ matchingDisplayCount: UnsafeMutablePointer<UInt32>?) -> CGError { .success }
public func CGPreflightScreenCaptureAccess() -> Bool { false }
public func CGRequestScreenCaptureAccess() -> Bool { false }
public func CGWarpMouseCursorPosition(_ newCursorPosition: CGPoint) -> CGError { .success }
public func CGAssociateMouseAndMouseCursorPosition(_ connected: boolean_t) -> CGError { .success }
public func CGDisplayHideCursor(_ display: CGDirectDisplayID) -> CGError { .success }
public func CGDisplayShowCursor(_ display: CGDirectDisplayID) -> CGError { .success }

public final class CGDisplayMode {
    public var width: Int { 0 }
    public var height: Int { 0 }
    public var pixelWidth: Int { 0 }
    public var pixelHeight: Int { 0 }
    public var refreshRate: Double { 0 }
    public var ioDisplayModeID: Int32 { 0 }
    public var ioFlags: UInt32 { 0 }
    public var isUsableForDesktopGUI: Bool { false }
}
public let kCGDisplayShowDuplicateLowResolutionModes: CFString = "kCGDisplayShowDuplicateLowResolutionModes"
public func CGDisplayCopyDisplayMode(_ display: CGDirectDisplayID) -> CGDisplayMode? { nil }
public func CGDisplayCopyAllDisplayModes(_ display: CGDirectDisplayID, _ options: CFDictionary?) -> CFArray? { nil }
public func CGDisplaySetDisplayMode(_ display: CGDirectDisplayID, _ mode: CGDisplayMode?, _ options: CFDictionary?) -> CGError { .success }

public struct CGConfigureOption: OptionSet {
    public var rawValue: UInt32
    public init(rawValue: UInt32) { self.rawValue = rawValue }
    public static let forAppOnly = CGConfigureOption([])
    public static let forSession = CGConfigureOption(rawValue: 1)
    public static let permanently = CGConfigureOption(rawValue: 2)
}
public func CGBeginDisplayConfiguration(_ config: UnsafeMutablePointer<CGDisplayConfigRef?>?) -> CGError { .success }
public func CGCompleteDisplayConfiguration(_ config: CGDisplayConfigRef?, _ option: CGConfigureOption) -> CGError { .success }
public func CGCancelDisplayConfiguration(_ config: CGDisplayConfigRef?) -> CGError { .success }
public func CGConfigureDisplayMirrorOfDisplay(_ config: CGDisplayConfigRef?, _ display: CGDirectDisplayID, _ master: CGDirectDisplayID) -> CGError { .success }
public func CGConfigureDisplayWithDisplayMode(_ config: CGDisplayConfigRef?, _ display: CGDirectDisplayID, _ mode: CGDisplayMode?, _ options: CFDictionary?) -> CGError { .success }
public func CGConfigureDisplayOrigin(_ config: CGDisplayConfigRef?, _ display: CGDirectDisplayID, _ x: Int32, _ y: Int32) -> CGError { .success }

// The session and the window list (CGSession.h, CGWindow.h).
public func CGSessionCopyCurrentDictionary() -> CFDictionary? { nil }
public struct CGWindowListOption: OptionSet {
    public var rawValue: UInt32
    public init(rawValue: UInt32) { self.rawValue = rawValue }
    public static let optionAll = CGWindowListOption([])
    public static let optionOnScreenOnly = CGWindowListOption(rawValue: 1 << 0)
    public static let optionOnScreenAboveWindow = CGWindowListOption(rawValue: 1 << 1)
    public static let optionOnScreenBelowWindow = CGWindowListOption(rawValue: 1 << 2)
    public static let optionIncludingWindow = CGWindowListOption(rawValue: 1 << 3)
    public static let excludeDesktopElements = CGWindowListOption(rawValue: 1 << 4)
}
public func CGWindowListCopyWindowInfo(_ option: CGWindowListOption, _ relativeToWindow: CGWindowID) -> CFArray? { nil }
public func CGWindowListCreate(_ option: CGWindowListOption, _ relativeToWindow: CGWindowID) -> CFArray? { nil }
public let kCGWindowNumber: CFString = "kCGWindowNumber"
public let kCGWindowStoreType: CFString = "kCGWindowStoreType"
public let kCGWindowLayer: CFString = "kCGWindowLayer"
public let kCGWindowBounds: CFString = "kCGWindowBounds"
public let kCGWindowSharingState: CFString = "kCGWindowSharingState"
public let kCGWindowAlpha: CFString = "kCGWindowAlpha"
public let kCGWindowOwnerPID: CFString = "kCGWindowOwnerPID"
public let kCGWindowMemoryUsage: CFString = "kCGWindowMemoryUsage"
public let kCGWindowOwnerName: CFString = "kCGWindowOwnerName"
public let kCGWindowName: CFString = "kCGWindowName"
public let kCGWindowIsOnscreen: CFString = "kCGWindowIsOnscreen"
public let kCGWindowBackingLocationVideoMemory: CFString = "kCGWindowBackingLocationVideoMemory"
public enum CGWindowLevelKey: Int32 {
    case baseWindow = 0, minimumWindow, desktopWindow, backstopMenu, normalWindow, floatingWindow, tornOffMenuWindow,
         dockWindow, mainMenuWindow, statusWindow, modalPanelWindow, popUpMenuWindow, draggingWindow, screenSaverWindow,
         maximumWindow, overlayWindow, helpWindow, utilityWindow, desktopIconWindow, cursorWindow, assistiveTechHighWindow
}
public func CGWindowLevelForKey(_ key: CGWindowLevelKey) -> CGWindowLevel { 0 }
public func CGRectMakeWithDictionaryRepresentation(_ dict: CFDictionary?, _ rect: UnsafeMutablePointer<CGRect>?) -> Bool { false }
extension CGRect {
    public init?(dictionaryRepresentation dict: CFDictionary) { return nil }
    public var dictionaryRepresentation: CFDictionary { NSDictionary() }
}
extension CGPoint {
    public init?(dictionaryRepresentation dict: CFDictionary) { return nil }
    public var dictionaryRepresentation: CFDictionary { NSDictionary() }
}
extension CGSize {
    public init?(dictionaryRepresentation dict: CFDictionary) { return nil }
    public var dictionaryRepresentation: CFDictionary { NSDictionary() }
}

// Events (CGEvent.h, CGEventSource.h, CGEventTypes.h).
public enum CGEventType: UInt32 {
    case null = 0, leftMouseDown = 1, leftMouseUp = 2, rightMouseDown = 3, rightMouseUp = 4, mouseMoved = 5,
         leftMouseDragged = 6, rightMouseDragged = 7, keyDown = 10, keyUp = 11, flagsChanged = 12, scrollWheel = 22,
         tabletPointer = 23, tabletProximity = 24, otherMouseDown = 25, otherMouseUp = 26, otherMouseDragged = 27,
         tapDisabledByTimeout = 0xFFFFFFFE, tapDisabledByUserInput = 0xFFFFFFFF
}
public enum CGMouseButton: UInt32 { case left = 0, right = 1, center = 2 }
public enum CGEventTapLocation: UInt32 { case cghidEventTap = 0, cgSessionEventTap, cgAnnotatedSessionEventTap }
public enum CGEventSourceStateID: Int32 { case privateState = -1, combinedSessionState = 0, hidSystemState = 1 }
public enum CGScrollEventUnit: UInt32 { case pixel = 0, line = 1 }
public enum CGScrollPhase: UInt32 { case began = 1, changed = 2, ended = 4, cancelled = 8, mayBegin = 128 }
public enum CGMomentumScrollPhase: UInt32 { case none = 0, begin = 1, continuous = 2, end = 3 }
public struct CGEventFlags: OptionSet {
    public var rawValue: UInt64
    public init(rawValue: UInt64) { self.rawValue = rawValue }
    public static let maskAlphaShift = CGEventFlags(rawValue: 0x00010000)
    public static let maskShift = CGEventFlags(rawValue: 0x00020000)
    public static let maskControl = CGEventFlags(rawValue: 0x00040000)
    public static let maskAlternate = CGEventFlags(rawValue: 0x00080000)
    public static let maskCommand = CGEventFlags(rawValue: 0x00100000)
    public static let maskHelp = CGEventFlags(rawValue: 0x00400000)
    public static let maskSecondaryFn = CGEventFlags(rawValue: 0x00800000)
    public static let maskNumericPad = CGEventFlags(rawValue: 0x00200000)
    public static let maskNonCoalesced = CGEventFlags(rawValue: 0x00000100)
}
public enum CGEventField: UInt32 {
    case mouseEventNumber = 0, mouseEventClickState = 1, mouseEventPressure = 2, mouseEventButtonNumber = 3,
         mouseEventDeltaX = 4, mouseEventDeltaY = 5, mouseEventInstantMouser = 6, mouseEventSubtype = 7,
         keyboardEventAutorepeat = 8, keyboardEventKeycode = 9, keyboardEventKeyboardType = 10,
         scrollWheelEventDeltaAxis1 = 11, scrollWheelEventDeltaAxis2 = 12, scrollWheelEventDeltaAxis3 = 13,
         scrollWheelEventInstantMouser = 14, eventTargetProcessSerialNumber = 39, eventTargetUnixProcessID = 40,
         eventSourceUnixProcessID = 41, eventSourceUserData = 42, eventSourceUserID = 43, eventSourceGroupID = 44,
         eventSourceStateID = 45, scrollWheelEventIsContinuous = 88, mouseEventWindowUnderMousePointer = 91,
         mouseEventWindowUnderMousePointerThatCanHandleThisEvent = 92, scrollWheelEventFixedPtDeltaAxis1 = 93,
         scrollWheelEventFixedPtDeltaAxis2 = 94, scrollWheelEventFixedPtDeltaAxis3 = 95,
         scrollWheelEventPointDeltaAxis1 = 96, scrollWheelEventPointDeltaAxis2 = 97, scrollWheelEventPointDeltaAxis3 = 98,
         scrollWheelEventScrollPhase = 99, scrollWheelEventScrollCount = 100, scrollWheelEventMomentumPhase = 123,
         eventUnacceleratedPointerMovementX = 170, eventUnacceleratedPointerMovementY = 171
}
public final class CGEventSource {
    public init?(stateID: CGEventSourceStateID) {}
    public var keyboardType: UInt32 = 0
    public var pixelsPerLine: Double = 10
    public var userData: Int64 = 0
    public var localEventsSuppressionInterval: CFTimeInterval = 0.25
    public static func flagsState(_ stateID: CGEventSourceStateID) -> CGEventFlags { [] }
    public static func keyState(_ stateID: CGEventSourceStateID, key: CGKeyCode) -> Bool { false }
    public static func buttonState(_ stateID: CGEventSourceStateID, button: CGMouseButton) -> Bool { false }
    public static func secondsSinceLastEventType(_ stateID: CGEventSourceStateID, eventType: CGEventType) -> CFTimeInterval { 0 }
    public static func counterForEventType(_ stateID: CGEventSourceStateID, eventType: CGEventType) -> UInt32 { 0 }
}
public final class CGEvent {
    public init?(source: CGEventSource?) {}
    public init?(mouseEventSource source: CGEventSource?, mouseType: CGEventType, mouseCursorPosition: CGPoint, mouseButton: CGMouseButton) {}
    public init?(keyboardEventSource source: CGEventSource?, virtualKey: CGKeyCode, keyDown: Bool) {}
    public init?(scrollWheelEvent2Source source: CGEventSource?, units: CGScrollEventUnit, wheelCount: UInt32, wheel1: Int32, wheel2: Int32, wheel3: Int32) {}
    public var location: CGPoint { get { .zero } set {} }
    public var unflippedLocation: CGPoint { .zero }
    public var flags: CGEventFlags { get { [] } set {} }
    public var type: CGEventType { get { .null } set {} }
    public var timestamp: CGEventTimestamp { get { 0 } set {} }
    public func copy() -> CGEvent? { nil }
    public func post(tap: CGEventTapLocation) {}
    public func postToPid(_ pid: pid_t) {}
    public func getIntegerValueField(_ field: CGEventField) -> Int64 { 0 }
    public func setIntegerValueField(_ field: CGEventField, value: Int64) {}
    public func getDoubleValueField(_ field: CGEventField) -> Double { 0 }
    public func setDoubleValueField(_ field: CGEventField, value: Double) {}
    public func keyboardSetUnicodeString(stringLength: Int, unicodeString: UnsafePointer<UniChar>?) {}
    public func keyboardGetUnicodeString(maxStringLength: Int, actualStringLength: UnsafeMutablePointer<Int>?, unicodeString: UnsafeMutablePointer<UniChar>?) {}
}

// Images, contexts, color (CGImage.h, CGContext.h, CGColorSpace.h, CGColor.h, CGPath.h).
public enum CGImageAlphaInfo: UInt32 {
    case none = 0, premultipliedLast, premultipliedFirst, last, first, noneSkipLast, noneSkipFirst, alphaOnly
}
public struct CGBitmapInfo: OptionSet {
    public var rawValue: UInt32
    public init(rawValue: UInt32) { self.rawValue = rawValue }
    public static let alphaInfoMask = CGBitmapInfo(rawValue: 0x1F)
    public static let floatComponents = CGBitmapInfo(rawValue: 1 << 8)
    public static let byteOrderMask = CGBitmapInfo(rawValue: 0x7000)
    public static let byteOrder16Little = CGBitmapInfo(rawValue: 1 << 12)
    public static let byteOrder32Little = CGBitmapInfo(rawValue: 2 << 12)
    public static let byteOrder16Big = CGBitmapInfo(rawValue: 3 << 12)
    public static let byteOrder32Big = CGBitmapInfo(rawValue: 4 << 12)
}
public enum CGColorRenderingIntent: Int32 { case defaultIntent, absoluteColorimetric, relativeColorimetric, perceptual, saturation }
public enum CGInterpolationQuality: Int32 { case `default` = 0, none = 1, low = 2, medium = 4, high = 3 }
public enum CGBlendMode: Int32 { case normal, multiply, screen, overlay, darken, lighten, colorDodge, colorBurn, softLight,
    hardLight, difference, exclusion, hue, saturation, color, luminosity, clear, copy, sourceIn, sourceOut, sourceAtop,
    destinationOver, destinationIn, destinationOut, destinationAtop, xor, plusDarker, plusLighter }
public enum CGLineCap: Int32 { case butt, round, square }
public enum CGLineJoin: Int32 { case miter, round, bevel }
public enum CGPathFillRule: Int { case winding, evenOdd }
public enum CGTextDrawingMode: Int32 { case fill, stroke, fillStroke, invisible, fillClip, strokeClip, fillStrokeClip, clip }

public final class CGColorSpace {
    public init?(name: CFString) {}
    public static let sRGB: CFString = "kCGColorSpaceSRGB"
    public static let displayP3: CFString = "kCGColorSpaceDisplayP3"
    public static let linearSRGB: CFString = "kCGColorSpaceLinearSRGB"
    public static let extendedSRGB: CFString = "kCGColorSpaceExtendedSRGB"
    public static let genericRGBLinear: CFString = "kCGColorSpaceGenericRGBLinear"
    public static let genericGrayGamma2_2: CFString = "kCGColorSpaceGenericGrayGamma2_2"
    public static let itur_709: CFString = "kCGColorSpaceITUR_709"
    public var name: CFString? { nil }
    public var numberOfComponents: Int { 0 }
}
public func CGColorSpaceCreateDeviceGray() -> CGColorSpace { fatalError() }
public func CGColorSpaceCreateDeviceRGB() -> CGColorSpace { fatalError() }

public final class CGColor {
    public init(gray: CGFloat, alpha: CGFloat) {}
    public init(red: CGFloat, green: CGFloat, blue: CGFloat, alpha: CGFloat) {}
    public init(srgbRed red: CGFloat, green: CGFloat, blue: CGFloat, alpha: CGFloat) {}
    public init?(colorSpace space: CGColorSpace, components: UnsafePointer<CGFloat>) {}
    public static let black = CGColor(gray: 0, alpha: 1)
    public static let white = CGColor(gray: 1, alpha: 1)
    public static let clear = CGColor(gray: 0, alpha: 0)
    public var alpha: CGFloat { 1 }
    public var components: [CGFloat]? { nil }
    public var numberOfComponents: Int { 0 }
    public var colorSpace: CGColorSpace? { nil }
    public func copy(alpha: CGFloat) -> CGColor? { nil }
    public func converted(to: CGColorSpace, intent: CGColorRenderingIntent, options: CFDictionary?) -> CGColor? { nil }
}

public final class CGDataProvider {
    public init?(data: CFData) {}
    public var data: CFData? { nil }
}

public final class CGImage {
    public init?(width: Int, height: Int, bitsPerComponent: Int, bitsPerPixel: Int, bytesPerRow: Int, space: CGColorSpace,
                 bitmapInfo: CGBitmapInfo, provider: CGDataProvider, decode: UnsafePointer<CGFloat>?, shouldInterpolate: Bool,
                 intent: CGColorRenderingIntent) {}
    public var width: Int { 0 }
    public var height: Int { 0 }
    public var bitsPerComponent: Int { 0 }
    public var bitsPerPixel: Int { 0 }
    public var bytesPerRow: Int { 0 }
    public var colorSpace: CGColorSpace? { nil }
    public var alphaInfo: CGImageAlphaInfo { .none }
    public var bitmapInfo: CGBitmapInfo { [] }
    public var dataProvider: CGDataProvider? { nil }
    public var isMask: Bool { false }
    public func cropping(to rect: CGRect) -> CGImage? { nil }
    public func copy() -> CGImage? { nil }
    public func copy(colorSpace: CGColorSpace) -> CGImage? { nil }
}

public class CGPath: Equatable {
    public init(rect: CGRect, transform: UnsafePointer<CGAffineTransform>?) {}
    public init(ellipseIn rect: CGRect, transform: UnsafePointer<CGAffineTransform>?) {}
    public init(roundedRect rect: CGRect, cornerWidth: CGFloat, cornerHeight: CGFloat, transform: UnsafePointer<CGAffineTransform>?) {}
    public var boundingBox: CGRect { .zero }
    public var isEmpty: Bool { true }
    public func contains(_ point: CGPoint, using rule: CGPathFillRule = .winding, transform: CGAffineTransform = .identity) -> Bool { false }
    public func copy() -> CGPath? { nil }
    public func mutableCopy() -> CGMutablePath? { nil }
    public static func == (a: CGPath, b: CGPath) -> Bool { a === b }
}
public final class CGMutablePath: CGPath {
    public init() { super.init(rect: .zero, transform: nil) }
    public func move(to point: CGPoint, transform: CGAffineTransform = .identity) {}
    public func addLine(to point: CGPoint, transform: CGAffineTransform = .identity) {}
    public func addRect(_ rect: CGRect, transform: CGAffineTransform = .identity) {}
    public func addEllipse(in rect: CGRect, transform: CGAffineTransform = .identity) {}
    public func addRoundedRect(in rect: CGRect, cornerWidth: CGFloat, cornerHeight: CGFloat, transform: CGAffineTransform = .identity) {}
    public func addPath(_ path: CGPath, transform: CGAffineTransform = .identity) {}
    public func closeSubpath() {}
}

public final class CGContext {
    public init?(data: UnsafeMutableRawPointer?, width: Int, height: Int, bitsPerComponent: Int, bytesPerRow: Int,
                 space: CGColorSpace, bitmapInfo: UInt32) {}
    public var data: UnsafeMutableRawPointer? { nil }
    public var width: Int { 0 }
    public var height: Int { 0 }
    public var bytesPerRow: Int { 0 }
    public var bitsPerComponent: Int { 0 }
    public var colorSpace: CGColorSpace? { nil }
    public var bitmapInfo: CGBitmapInfo { [] }
    public var interpolationQuality: CGInterpolationQuality = .default
    public var ctm: CGAffineTransform { .identity }
    public func makeImage() -> CGImage? { nil }
    public func draw(_ image: CGImage, in rect: CGRect, byTiling: Bool = false) {}
    public func saveGState() {}
    public func restoreGState() {}
    public func translateBy(x tx: CGFloat, y ty: CGFloat) {}
    public func scaleBy(x sx: CGFloat, y sy: CGFloat) {}
    public func rotate(by angle: CGFloat) {}
    public func concatenate(_ transform: CGAffineTransform) {}
    public func setFillColor(_ color: CGColor) {}
    public func setStrokeColor(_ color: CGColor) {}
    public func setFillColor(gray: CGFloat, alpha: CGFloat) {}
    public func setStrokeColor(gray: CGFloat, alpha: CGFloat) {}
    public func setFillColor(red: CGFloat, green: CGFloat, blue: CGFloat, alpha: CGFloat) {}
    public func setStrokeColor(red: CGFloat, green: CGFloat, blue: CGFloat, alpha: CGFloat) {}
    public func setLineWidth(_ width: CGFloat) {}
    public func setLineCap(_ cap: CGLineCap) {}
    public func setLineJoin(_ join: CGLineJoin) {}
    public func setBlendMode(_ mode: CGBlendMode) {}
    public func setAlpha(_ alpha: CGFloat) {}
    public func setShouldAntialias(_ shouldAntialias: Bool) {}
    public func setAllowsAntialiasing(_ allowsAntialiasing: Bool) {}
    public func setShadow(offset: CGSize, blur: CGFloat, color: CGColor?) {}
    public func addPath(_ path: CGPath) {}
    public func addRect(_ rect: CGRect) {}
    public func addEllipse(in rect: CGRect) {}
    public func move(to point: CGPoint) {}
    public func addLine(to point: CGPoint) {}
    public func closePath() {}
    public func beginPath() {}
    public func fillPath(using rule: CGPathFillRule = .winding) {}
    public func strokePath() {}
    public func clip(using rule: CGPathFillRule = .winding) {}
    public func clip(to rect: CGRect) {}
    public func fill(_ rect: CGRect) {}
    public func fill(_ rects: [CGRect]) {}
    public func stroke(_ rect: CGRect) {}
    public func stroke(_ rect: CGRect, width: CGFloat) {}
    public func fillEllipse(in rect: CGRect) {}
    public func strokeEllipse(in rect: CGRect) {}
    public func clear(_ rect: CGRect) {}
    public func flush() {}
}
