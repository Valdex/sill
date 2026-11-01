import Foundation

/// What the host streams. The client decides; the host only ever streams one source.
public enum StreamSource: Codable, Hashable {
    case none
    case window(UInt32)   // CGWindowID, stable for the life of the window
    case desktop          // the Mac's main display
}

/// One on-screen Mac window, as the client sees it. WindowList order is stable: first seen first.
public struct WindowInfo: Codable, Hashable, Identifiable {
    public var id: UInt32
    public var title: String
    public var appName: String
    public var bundleID: String
    public var width: Int      // points
    public var height: Int

    public init(id: UInt32, title: String, appName: String, bundleID: String, width: Int, height: Int) {
        self.id = id; self.title = title; self.appName = appName; self.bundleID = bundleID
        self.width = width; self.height = height
    }
}

/// An installed app, for the drawer's "All apps" section.
public struct AppInfo: Codable, Hashable, Identifiable {
    public var id: String { bundleID }
    public var name: String
    public var bundleID: String

    public init(name: String, bundleID: String) { self.name = name; self.bundleID = bundleID }
}

public struct WindowList: Codable {
    public var macName: String
    public var windows: [WindowInfo]
    public var active: StreamSource
    /// Random, picked once per launch of the host and the same in every list to every device: two
    /// connections whose lists carry the same one reach the same running host. A name cannot tell
    /// that (two Macs can share one, and mDNS renames neither when they share no link), and a
    /// device moving its session from AWDL to the network must not land on another Mac. Optional:
    /// nil from a host older than 2026-09-24, and an older device ignores the key.
    public var launchID: String?
    /// The host's version: Sill.app's CFBundleShortVersionString ("0.4.0"); nil from SillHost (no
    /// bundle) and from hosts before 2026-09-25. For later devices: which Mac to update, and to what
    /// (docs/update-notice-plan.md §14).
    public var hostVersion: String?
    /// The host's SillProtocol.current; nil from hosts before 2026-09-25, which speak 1. The JSON key
    /// is "protocol".
    public var `protocol`: Int?
    /// Which trackpad gestures this host takes (kind 28, Gesture.swift): 1 is the six of
    /// `TrackpadGesture.names`, each turned into the Mac's own shortcut. Nil from every host before
    /// 2026-09-27, to which a device sends no kind 28. A later generation of gestures says 2 and
    /// goes only to a host that says so.
    public var gestures: Int?
    /// What this host takes and sends of the clipboard (kind 32, Clipboard.swift): 1 is text
    /// (`ClipboardPolicy.generation`). Nil from every host before 2026-10-14, and from one whose Share
    /// clipboard with devices is off: a device sends no kind 32 to either. A later generation
    /// (images) says 2, and a device sends images only to a host that says so. An Int, as `gestures`:
    /// pull request #66's `Bool` (never released) does not decode here and becomes an Int as it
    /// rebases.
    public var clipboard: Int?
    /// Whether this host repeats a device's key held down (KeyRepeat, #162): 1 is a key repeated between
    /// its down and its up, after the Mac's own Delay Until Repeat and at its Key Repeat Rate, each repeat
    /// the key's down again with the autorepeat flag. Nil from every host before 2026-10-29: a device then
    /// types a hardware keyboard's characters through iPadOS's text system, as it always did (iPadOS
    /// repeats Delete and Space itself; sent as keys, a Mac that repeats none would type each once), and
    /// sends them as keys only to a host that says so (the device's KeyTyping). Set after the initializer,
    /// whose parameters stay as they were: `list.keyRepeat = KeyRepeat.generation`.
    public var keyRepeat: Int?

    // Pull request #66's additions (2026-10-12, the Mac client; SideStreams.swift), ported for this fork's
    // coherence (docs/coherence-protocol.md). #66's `Bool`s are `Int` generations here, as `clipboard` above
    // says they become: 1 is #66's meaning, a later generation says 2 and goes only to a host that says so.
    // Each nil from every host before; set after the initializer, whose parameters stay as they were, as
    // `keyRepeat` is. #66's `audio` (its kind 33 sound) is not carried: this build's sound is kind 43.
    /// 1: this host takes the middle button (PointerAction.middleDown and .middleUp); a device sends
    /// neither to a host that does not say so (its kind 8 would not decode there).
    public var middleButton: Int?
    /// How many windows this host streams besides the one picked (kinds 29–31, SideStreamRequest); a
    /// device sends no kind 29 to a host without it.
    public var sideStreams: Int?
    /// 1: this host takes files dropped on its windows (kinds 34, 35, 36, 37).
    public var fileDrop: Int?
    /// 1: this host follows a drag of files over its windows as it goes (kind 38, LiveDrag); to a host
    /// without it a device drops only where it lets go (kind 35).
    public var liveDrag: Int?
    /// 1: this host reads and presses its apps' own Dock menus (kind 39, DockMenu). This fork's host never
    /// says it.
    public var dockMenu: Int?
    /// Input over a window shown beside the streamed one (kind 8 with a `window`, WindowInput): 1, a move,
    /// a scroll and a scroll's phases (#66's); 2, every event (coherence).
    public var windowInput: Int?
    /// Each app's Dock badge on the host ("3", "•"), by bundle ID, for a Mac client's Dock icons; an app
    /// without a badge is left out. This fork's host reads none.
    public var badges: [String: String]?
    /// The host's own displays (never Sill's hidden ones), for a device that picks which the Desktop is
    /// (kind 40); nil from a host whose Desktop is its main display.
    public var displays: [DisplayInfo]?
    /// The one the Desktop shows now.
    public var desktopDisplay: UInt32?
    /// 1: this host starts its hidden displays' captures again on a device's word (kind 42).
    public var rebuildDesk: Int?
    // Coherence (this fork; docs/coherence-protocol.md, "Capabilities"), each an Int, nil from hosts before.
    /// 1: kinds 45 and 46, kind 29's coherence fields, kind 15's `activate` and `restore`, kind 44's `window`.
    public var coherence: Int?
    /// 1: kind 48 and the `sill-data/1` connection.
    public var data: Int?
    /// 1: `LaunchApp.documents`.
    public var documents: Int?
    /// The largest icon (pixels) this host draws for kind 4 when the hello asks (`Hello.iconSize`).
    public var iconSize: Int?

    public init(macName: String, windows: [WindowInfo], active: StreamSource, launchID: String? = nil,
                hostVersion: String? = nil, protocol: Int? = nil, gestures: Int? = nil, clipboard: Int? = nil) {
        self.macName = macName; self.windows = windows; self.active = active; self.launchID = launchID
        self.hostVersion = hostVersion; self.protocol = `protocol`; self.gestures = gestures
        self.clipboard = clipboard
    }
}

public struct LaunchApp: Codable {
    public var bundleID: String
    /// Absolute paths on the Mac to open with that app (an empty `bundleID`: each with its default app).
    /// Only to a host whose window list says `documents` 1 (docs/coherence-protocol.md); nil from every
    /// device before, and left out of the JSON when nil.
    public var documents: [String]?
    public init(bundleID: String, documents: [String]? = nil) { self.bundleID = bundleID; self.documents = documents }
}

/// Client → host: what the bar's long-press menu asks of a window, the three traffic lights.
///
/// `activate` (bring the window's app forward and make the window key, as a click would) and `restore`
/// (un-minimize it) are coherence's (docs/coherence-protocol.md), sent only to a host whose window list
/// says `coherence` 1: a host before decodes no kind 15 that carries them and drops it whole, so a device
/// never sends them to one. A later action follows the same rule (a host of this build drops it too).
public struct WindowCommand: Codable {
    public enum Action: String, Codable { case close, minimize, fullScreen, activate, restore }
    public var id: UInt32
    public var action: Action
    public init(id: UInt32, action: Action) { self.id = id; self.action = action }
}

/// JSON payloads. Everything here is small and infrequent; frames never go through JSON.
public enum Wire {
    public static func encode<T: Encodable>(_ value: T) -> Data {
        (try? JSONEncoder().encode(value)) ?? Data()
    }
    public static func decode<T: Decodable>(_ type: T.Type, from data: Data) -> T? {
        try? JSONDecoder().decode(type, from: data)
    }
}

/// Binary image payloads: a key, then the image bytes.
public enum ImageBlob {
    /// windowID(4, BE) + JPEG
    public static func encodeThumbnail(windowID: UInt32, jpeg: Data) -> Data {
        var d = Data(capacity: 4 + jpeg.count)
        d.appendBigEndian(windowID)
        d.append(jpeg)
        return d
    }
    public static func decodeThumbnail(_ d: Data) -> (windowID: UInt32, jpeg: Data)? {
        guard d.count > 4 else { return nil }
        return (d.readBigEndian(UInt32.self, at: 0), d.subdata(in: (d.startIndex + 4)..<d.endIndex))
    }

    /// bundleIDLength(2, BE) + bundleID UTF-8 + PNG
    public static func encodeIcon(bundleID: String, png: Data) -> Data {
        let id = Data(bundleID.utf8)
        var d = Data(capacity: 2 + id.count + png.count)
        d.appendBigEndian(UInt16(id.count))
        d.append(id)
        d.append(png)
        return d
    }
    public static func decodeIcon(_ d: Data) -> (bundleID: String, png: Data)? {
        guard d.count > 2 else { return nil }
        let len = Int(d.readBigEndian(UInt16.self, at: 0))
        guard d.count > 2 + len else { return nil }
        let idStart = d.startIndex + 2
        guard let id = String(data: d.subdata(in: idStart..<(idStart + len)), encoding: .utf8) else { return nil }
        return (id, d.subdata(in: (idStart + len)..<d.endIndex))
    }
}

/// One of the host's own displays, as a device lists it to pick the Desktop (kind 40).
public struct DisplayInfo: Codable, Hashable, Sendable {
    public var id: UInt32
    public var name: String
    public var width: Double
    public var height: Double
    public var main: Bool?
    public init(id: UInt32, name: String, width: Double, height: Double, main: Bool? = nil) {
        self.id = id; self.name = name; self.width = width; self.height = height; self.main = main
    }
}

/// Which of the host's displays the Desktop is (kind 40).
public struct PickDisplay: Codable, Hashable, Sendable {
    public var display: UInt32
    public init(display: UInt32) { self.display = display }
}

/// One of the host's displays where a window ID goes (a side stream, kinds 29–31; a drop's `window`):
/// its display ID with the top bit set, which no window ID has. A client may show each of the host's
/// displays as a Desktop window of its own, the focused one the Desktop (`PickDisplay`) and the others
/// side streams; only to a host that lists its `displays`.
public enum DesktopWindow {
    public static let bit: UInt32 = 0x8000_0000
    public static func id(display: UInt32) -> UInt32 { display | bit }
    /// The display a tagged ID names; nil for a window's.
    public static func display(of id: UInt32) -> UInt32? { id & bit != 0 ? id & ~bit : nil }
}
