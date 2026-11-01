// Stub of ApplicationServices: Accessibility (HIServices' AXUIElement.h, AXValue.h, AXObserver.h,
// AXAttributeConstants.h, AXActionConstants.h, AXNotificationConstants.h, AXRoleConstants.h), as
// Swift imports it. ApplicationServices re-exports CoreGraphics and ImageIO. The kAX… names are
// CFSTR macros, so Swift sees Strings.
@_exported import CoreGraphics
@_exported import ImageIO
import Foundation

public final class AXUIElement {}
public final class AXObserver {}
public final class AXValue {}

public enum AXError: Int32 {
    case success = 0, failure = -25200, illegalArgument = -25201, invalidUIElement = -25202, invalidUIElementObserver = -25203,
         cannotComplete = -25204, attributeUnsupported = -25205, actionUnsupported = -25206, notificationUnsupported = -25207,
         notImplemented = -25208, notificationAlreadyRegistered = -25209, notificationNotRegistered = -25210, apiDisabled = -25211,
         noValue = -25212, parameterizedAttributeUnsupported = -25213, notEnoughPrecision = -25214
}
public enum AXValueType: UInt32 { case illegal = 0, cgPoint = 1, cgSize = 2, cgRect = 3, cfRange = 4, axError = 5 }
public struct AXCopyMultipleAttributeOptions: OptionSet {
    public var rawValue: UInt32
    public init(rawValue: UInt32) { self.rawValue = rawValue }
    public static let stopOnError = AXCopyMultipleAttributeOptions(rawValue: 1)
}
// A C function pointer on a Mac; a Swift function type here, as its parameters are classes in this
// stub (so a closure capturing context, which a Mac refuses, is not caught here).
public typealias AXObserverCallback = (AXObserver, AXUIElement, CFString, UnsafeMutableRawPointer?) -> Void

public let kAXTrustedCheckOptionPrompt: Unmanaged<CFString> = .passUnretained("AXTrustedCheckOptionPrompt")
public func AXIsProcessTrusted() -> Bool { false }
public func AXIsProcessTrustedWithOptions(_ options: CFDictionary?) -> Bool { false }
public func AXAPIEnabled() -> Bool { false }

public func AXUIElementGetTypeID() -> CFTypeID { 200 }
public func AXUIElementCreateApplication(_ pid: pid_t) -> AXUIElement { AXUIElement() }
public func AXUIElementCreateSystemWide() -> AXUIElement { AXUIElement() }
public func AXUIElementCopyAttributeValue(_ element: AXUIElement, _ attribute: CFString, _ value: UnsafeMutablePointer<CFTypeRef?>) -> AXError { .success }
public func AXUIElementCopyAttributeValues(_ element: AXUIElement, _ attribute: CFString, _ index: CFIndex, _ maxValues: CFIndex, _ values: UnsafeMutablePointer<CFArray?>) -> AXError { .success }
public func AXUIElementGetAttributeValueCount(_ element: AXUIElement, _ attribute: CFString, _ count: UnsafeMutablePointer<CFIndex>) -> AXError { .success }
public func AXUIElementCopyMultipleAttributeValues(_ element: AXUIElement, _ attributes: CFArray, _ options: AXCopyMultipleAttributeOptions, _ values: UnsafeMutablePointer<CFArray?>) -> AXError { .success }
public func AXUIElementCopyAttributeNames(_ element: AXUIElement, _ names: UnsafeMutablePointer<CFArray?>) -> AXError { .success }
public func AXUIElementCopyActionNames(_ element: AXUIElement, _ names: UnsafeMutablePointer<CFArray?>) -> AXError { .success }
public func AXUIElementSetAttributeValue(_ element: AXUIElement, _ attribute: CFString, _ value: CFTypeRef) -> AXError { .success }
public func AXUIElementIsAttributeSettable(_ element: AXUIElement, _ attribute: CFString, _ settable: UnsafeMutablePointer<DarwinBoolean>) -> AXError { .success }
public func AXUIElementPerformAction(_ element: AXUIElement, _ action: CFString) -> AXError { .success }
public func AXUIElementSetMessagingTimeout(_ element: AXUIElement, _ timeoutInSeconds: Float) -> AXError { .success }
public func AXUIElementGetPid(_ element: AXUIElement, _ pid: UnsafeMutablePointer<pid_t>) -> AXError { .success }
public func AXUIElementCopyElementAtPosition(_ application: AXUIElement, _ x: Float, _ y: Float, _ element: UnsafeMutablePointer<AXUIElement?>) -> AXError { .success }

public func AXValueGetTypeID() -> CFTypeID { 201 }
public func AXValueCreate(_ theType: AXValueType, _ valuePtr: UnsafeRawPointer) -> AXValue? { nil }
public func AXValueGetType(_ value: AXValue) -> AXValueType { .illegal }
public func AXValueGetValue(_ value: AXValue, _ theType: AXValueType, _ valuePtr: UnsafeMutableRawPointer) -> Bool { false }

public func AXObserverGetTypeID() -> CFTypeID { 202 }
public func AXObserverCreate(_ application: pid_t, _ callback: AXObserverCallback, _ outObserver: UnsafeMutablePointer<AXObserver?>) -> AXError { .success }
public func AXObserverAddNotification(_ observer: AXObserver, _ element: AXUIElement, _ notification: CFString, _ refcon: UnsafeMutableRawPointer?) -> AXError { .success }
public func AXObserverRemoveNotification(_ observer: AXObserver, _ element: AXUIElement, _ notification: CFString) -> AXError { .success }
public func AXObserverGetRunLoopSource(_ observer: AXObserver) -> CFRunLoopSource { fatalError() }

// Attributes.
public var kAXRoleAttribute: String { "AXRole" }
public var kAXSubroleAttribute: String { "AXSubrole" }
public var kAXRoleDescriptionAttribute: String { "AXRoleDescription" }
public var kAXTitleAttribute: String { "AXTitle" }
public var kAXDescriptionAttribute: String { "AXDescription" }
public var kAXHelpAttribute: String { "AXHelp" }
public var kAXValueAttribute: String { "AXValue" }
public var kAXIdentifierAttribute: String { "AXIdentifier" }
public var kAXEnabledAttribute: String { "AXEnabled" }
public var kAXFocusedAttribute: String { "AXFocused" }
public var kAXParentAttribute: String { "AXParent" }
public var kAXChildrenAttribute: String { "AXChildren" }
public var kAXWindowAttribute: String { "AXWindow" }
public var kAXTopLevelUIElementAttribute: String { "AXTopLevelUIElement" }
public var kAXPositionAttribute: String { "AXPosition" }
public var kAXSizeAttribute: String { "AXSize" }
public var kAXMainAttribute: String { "AXMain" }
public var kAXMinimizedAttribute: String { "AXMinimized" }
public var kAXModalAttribute: String { "AXModal" }
public var kAXCloseButtonAttribute: String { "AXCloseButton" }
public var kAXMinimizeButtonAttribute: String { "AXMinimizeButton" }
public var kAXZoomButtonAttribute: String { "AXZoomButton" }
public var kAXFullScreenButtonAttribute: String { "AXFullScreenButton" }
public var kAXToolbarButtonAttribute: String { "AXToolbarButton" }
public var kAXProxyAttribute: String { "AXProxy" }
public var kAXGrowAreaAttribute: String { "AXGrowArea" }
public var kAXMenuBarAttribute: String { "AXMenuBar" }
public var kAXWindowsAttribute: String { "AXWindows" }
public var kAXFrontmostAttribute: String { "AXFrontmost" }
public var kAXHiddenAttribute: String { "AXHidden" }
public var kAXMainWindowAttribute: String { "AXMainWindow" }
public var kAXFocusedWindowAttribute: String { "AXFocusedWindow" }
public var kAXFocusedUIElementAttribute: String { "AXFocusedUIElement" }
public var kAXFocusedApplicationAttribute: String { "AXFocusedApplication" }
public var kAXMenuItemCmdCharAttribute: String { "AXMenuItemCmdChar" }
public var kAXMenuItemCmdVirtualKeyAttribute: String { "AXMenuItemCmdVirtualKey" }
public var kAXMenuItemCmdGlyphAttribute: String { "AXMenuItemCmdGlyph" }
public var kAXMenuItemCmdModifiersAttribute: String { "AXMenuItemCmdModifiers" }
public var kAXMenuItemMarkCharAttribute: String { "AXMenuItemMarkChar" }
public var kAXSelectedChildrenAttribute: String { "AXSelectedChildren" }
// Actions.
public var kAXPressAction: String { "AXPress" }
public var kAXRaiseAction: String { "AXRaise" }
public var kAXCancelAction: String { "AXCancel" }
public var kAXShowMenuAction: String { "AXShowMenu" }
public var kAXPickAction: String { "AXPick" }
public var kAXConfirmAction: String { "AXConfirm" }
// Roles and subroles.
public var kAXApplicationRole: String { "AXApplication" }
public var kAXWindowRole: String { "AXWindow" }
public var kAXSheetRole: String { "AXSheet" }
public var kAXDrawerRole: String { "AXDrawer" }
public var kAXMenuBarRole: String { "AXMenuBar" }
public var kAXMenuBarItemRole: String { "AXMenuBarItem" }
public var kAXMenuRole: String { "AXMenu" }
public var kAXMenuItemRole: String { "AXMenuItem" }
public var kAXListRole: String { "AXList" }
public var kAXToolbarRole: String { "AXToolbar" }
public var kAXButtonRole: String { "AXButton" }
public var kAXGroupRole: String { "AXGroup" }
public var kAXPopoverRole: String { "AXPopover" }
public var kAXStandardWindowSubrole: String { "AXStandardWindow" }
public var kAXDialogSubrole: String { "AXDialog" }
public var kAXSystemDialogSubrole: String { "AXSystemDialog" }
public var kAXFloatingWindowSubrole: String { "AXFloatingWindow" }
public var kAXSystemFloatingWindowSubrole: String { "AXSystemFloatingWindow" }
public var kAXUnknownSubrole: String { "AXUnknown" }
// Notifications.
public var kAXMainWindowChangedNotification: String { "AXMainWindowChanged" }
public var kAXFocusedWindowChangedNotification: String { "AXFocusedWindowChanged" }
public var kAXFocusedUIElementChangedNotification: String { "AXFocusedUIElementChanged" }
public var kAXApplicationActivatedNotification: String { "AXApplicationActivated" }
public var kAXApplicationDeactivatedNotification: String { "AXApplicationDeactivated" }
public var kAXApplicationHiddenNotification: String { "AXApplicationHidden" }
public var kAXApplicationShownNotification: String { "AXApplicationShown" }
public var kAXWindowCreatedNotification: String { "AXWindowCreated" }
public var kAXWindowMovedNotification: String { "AXWindowMoved" }
public var kAXWindowResizedNotification: String { "AXWindowResized" }
public var kAXWindowMiniaturizedNotification: String { "AXWindowMiniaturized" }
public var kAXWindowDeminiaturizedNotification: String { "AXWindowDeminiaturized" }
public var kAXSheetCreatedNotification: String { "AXSheetCreated" }
public var kAXDrawerCreatedNotification: String { "AXDrawerCreated" }
public var kAXMenuOpenedNotification: String { "AXMenuOpened" }
public var kAXMenuClosedNotification: String { "AXMenuClosed" }
public var kAXMenuItemSelectedNotification: String { "AXMenuItemSelected" }
public var kAXUIElementDestroyedNotification: String { "AXUIElementDestroyed" }
public var kAXTitleChangedNotification: String { "AXTitleChanged" }
public var kAXValueChangedNotification: String { "AXValueChanged" }
public var kAXCreatedNotification: String { "AXCreated" }
