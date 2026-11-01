import Foundation

// More of the host's windows live at once, and files dropped and dragged (kinds 29–31 and 34–42), as
// upstream pull request #66 (Sill Viewer, a Mac client) gives them, ported onto main for this fork's
// Windows client (docs/coherence-protocol.md). Two parts of #66's file are left out here: its kind 33
// sound (`AudioPacket`, and SideStreamRequest's `audio` and `audioCodec`: main's sound is kind 43,
// Audio.swift) and its clipboard images on the Mac (`ClipboardImage`, `ClipboardPasteboard`: the
// clipboard's later generations, Clipboard.swift and Coherence.swift). Kind 39's payload (`DockMenu`)
// is kept for the wire's sake; this host serves no kind 39.
//
// Pure apart from `FolderZip` (macOS only, behind `#if os(macOS)` as in #66): Foundation only, so it
// is type-checked on its own with swiftc.

/// More of the host's windows live at once (kinds 29–31), for a client whose windows are the host's
/// windows: the one it streams (kind 6) at full rate, and these at a lower one, each with its own
/// capture and encoder on the host. A device sends kind 29 only to a host whose window list says
/// `sideStreams` (the most it streams this way), and reads 30 and 31 by the window ID in front.
public struct SideStreamRequest: Codable, Hashable, Sendable {
    /// The host's window IDs; the one the device streams is left out by the host if listed.
    public var windows: [UInt32]
    /// Frames a second for each (the host caps it, `SideStreamRequest.maxFPS`).
    public var fps: Int?
    /// True: every window this device shows (the picked one too) is captured with what belongs to it,
    /// its menus, popovers, sheets and the system's panels over it, and no other app's window. A host
    /// before it captures the window alone.
    public var wholeApp: Bool?
    /// The device's largest screen in points, [width, height]: the host's room for each of its
    /// windows (Sill's desk) is made at least this big, so growing a window up to full screen there
    /// never makes it again.
    public var screen: [Double]?
    // Coherence (docs/coherence-protocol.md, kind 29), each only to a host whose list says
    // `coherence` 1; a host before ignores them.
    /// True: every listed window is a peer, none picked with kind 6; the host streams them all.
    public var coherence: Bool?
    /// The window the device has in front, streamed at `focusFPS`.
    public var focus: UInt32?
    /// Frames a second for `focus` (default `defaultFocusFPS`, at most `maxFocusFPS`).
    public var focusFPS: Int?
    /// Bundle IDs whose windows join the set by themselves as they open (kind 45's `joined`).
    public var apps: [String]?
    /// True: every app's windows join by themselves.
    public var all: Bool?
    /// Windows minimized or fully covered on the device: their frames are dropped, their capture kept.
    public var hidden: [UInt32]?
    /// The device's pixels per point (1.0, 1.25, 1.5, 2…): the host captures at that size, at most
    /// its display's scale.
    public var scale: Double?

    public init(windows: [UInt32], fps: Int? = nil, wholeApp: Bool? = nil, screen: [Double]? = nil,
                coherence: Bool? = nil, focus: UInt32? = nil, focusFPS: Int? = nil, apps: [String]? = nil, all: Bool? = nil,
                hidden: [UInt32]? = nil, scale: Double? = nil) {
        self.windows = windows; self.fps = fps; self.wholeApp = wholeApp; self.screen = screen
        self.coherence = coherence; self.focus = focus; self.focusFPS = focusFPS; self.apps = apps; self.all = all
        self.hidden = hidden; self.scale = scale
    }

    public static let defaultFPS = 15
    public static let maxFPS = 30
    /// The focused window's rate under coherence: 60 unless asked, 120 at most.
    public static let defaultFocusFPS = 60
    public static let maxFocusFPS = 120

    /// A side stream's payload (kinds 30 and 31): the window ID, then the rest.
    public static func pack(window: UInt32, _ payload: Data) -> Data {
        var d = Data(capacity: 4 + payload.count)
        d.appendBigEndian(window)
        d.append(payload)
        return d
    }

    /// The window ID and the rest of a kind 30 or 31; nil when too short.
    public static func unpack(_ d: Data) -> (window: UInt32, payload: Data)? {
        guard d.count >= 4 else { return nil }
        return (d.readBigEndian(UInt32.self, at: 0), d.subdata(in: (d.startIndex + 4)..<d.endIndex))
    }
}

/// A part of a dropped file (kind 34): the transfer's ID (UInt32 BE), the file's name (UInt16 BE
/// length, then UTF-8), its whole size and this part's offset (UInt64 BE each), then the bytes.
/// Parts come in order, each under the host's 1 MB limit for a device's message.
public enum FileChunk {
    public static let maxBytes = 512 * 1024
    /// The most a drop sends in all.
    public static let maxTotal: UInt64 = 512 * 1024 * 1024   // what a drop may queue on the connection

    public static func pack(id: UInt32, name: String, total: UInt64, offset: UInt64, bytes: Data) -> Data {
        let n = Data(name.utf8.prefix(1024))
        var d = Data(capacity: 22 + n.count + bytes.count)
        d.appendBigEndian(id)
        d.appendBigEndian(UInt16(n.count))
        d.append(n)
        d.appendBigEndian(total)
        d.appendBigEndian(offset)
        d.append(bytes)
        return d
    }

    public static func unpack(_ d: Data) -> (id: UInt32, name: String, total: UInt64, offset: UInt64, bytes: Data)? {
        guard d.count >= 6 else { return nil }
        let id = d.readBigEndian(UInt32.self, at: 0)
        let n = Int(d.readBigEndian(UInt16.self, at: 4))
        guard d.count >= 6 + n + 16, let name = String(data: d.subdata(in: (d.startIndex + 6)..<(d.startIndex + 6 + n)), encoding: .utf8)
        else { return nil }
        let total = d.readBigEndian(UInt64.self, at: 6 + n)
        let offset = d.readBigEndian(UInt64.self, at: 14 + n)
        return (id, name, total, offset, d.subdata(in: (d.startIndex + 22 + n)..<d.endIndex))
    }
}

/// Files the device sent (kind 34, by ID) dropped on the picture at (x, y), fractions of the
/// streamed frame as a pointer's are: the host's app under that point gets them as a drop.
/// With `offer`, `files` are indices into that drag offer (kind 36): the host's own files, dragged
/// out of one of its windows and dropped on another, dropped from where they are with nothing copied.
/// With `window`, (x, y) are in that window's picture, not the streamed one's (a window shown beside it).
public struct FileDrop: Codable, Hashable, Sendable {
    public var files: [UInt32]
    public var x: Double
    public var y: Double
    public var offer: UInt32?
    public var window: UInt32?
    /// The files (of `files`) that are folders sent zipped (`FolderZip`): unpacked before the drop.
    public var folders: [UInt32]?
    public init(files: [UInt32], x: Double, y: Double, offer: UInt32? = nil, window: UInt32? = nil, folders: [UInt32]? = nil) {
        self.files = files; self.x = x; self.y = y; self.offer = offer; self.window = window; self.folders = folders
    }
}

/// A drag of files over the host's windows, followed as it goes (kind 38), so the app under the pointer
/// takes it as a drag of its own: it shows where the files would land, and the modifier keys choose
/// what a drop does (Finder: move, ⌥ copy, ⌥⌘ alias). One drag is one `id`, in phases:
/// - `start`: the drag came over a window of the host. With `offer`, the files are the host's own (a
///   drag out of one of its windows, whose button the host still holds); without, the device's own,
///   sent as kind 34 parts.
/// - `files`: those parts have all been sent (`files`, `folders` as kind 35's): the host starts its
///   drag of them under the pointer.
/// - `move`: the pointer over a window (`window`, or the streamed one), `modifiers` as InputEvent's.
/// - `drop`: let go there. `cancel`: the drag ended elsewhere, or was given up (the host lets go of its
///   own with Esc, and removes the copies it received).
/// Phases a host does not know are ignored; the device keeps sending moves only while the drag is over
/// one of the host's windows.
public struct LiveDrag: Codable, Hashable, Sendable {
    public var id: UInt32
    public var phase: String
    public var window: UInt32?
    public var x: Double
    public var y: Double
    public var modifiers: UInt64?
    public var offer: UInt32?
    public var files: [UInt32]?
    public var folders: [UInt32]?
    public init(id: UInt32, phase: String, window: UInt32? = nil, x: Double = 0, y: Double = 0, modifiers: UInt64? = nil,
                offer: UInt32? = nil, files: [UInt32]? = nil, folders: [UInt32]? = nil) {
        self.id = id; self.phase = phase; self.window = window; self.x = x; self.y = y; self.modifiers = modifiers
        self.offer = offer; self.files = files; self.folders = folders
    }
    public static let start = "start", filesSent = "files", move = "move", drop = "drop", cancel = "cancel"
}

/// An app's own Dock menu on the host (kind 39), for the Dock icon a Mac client shows for it: the
/// items the app adds (Finder's New Finder Window…, a browser's profiles), without the app's windows
/// (the client lists its own) or the Dock's own items (Options, Show All Windows, Hide, Quit, which
/// the client's Dock adds itself). Pull request #66's; this fork's host serves no kind 39 (its
/// window list never says `dockMenu`), so the type is here for the wire alone.
/// - Device → host: `app` and `token`; with `press`, that item (an `Item.id`, its `title` as shown).
/// - Host → device: `app`, the `token` answered, and `items` (empty: none, or the app is not running
///   there), or `pressed`.
public struct DockMenu: Codable, Hashable, Sendable {
    public struct Item: Codable, Hashable, Sendable {
        /// Its place in the Dock's menu as read, which a press names.
        public var id: Int
        public var title: String
        public var enabled: Bool
        public var separator: Bool?
        public init(id: Int, title: String, enabled: Bool, separator: Bool? = nil) {
            self.id = id; self.title = title; self.enabled = enabled; self.separator = separator
        }
    }
    public var app: String
    public var token: UInt32
    public var press: Int?
    public var title: String?
    public var items: [Item]?
    public var pressed: Bool?
    public init(app: String, token: UInt32, press: Int? = nil, title: String? = nil, items: [Item]? = nil, pressed: Bool? = nil) {
        self.app = app; self.token = token; self.press = press; self.title = title; self.items = items; self.pressed = pressed
    }
}

/// What an app on the host is dragging under a device's held button (kind 36): files, by name and
/// size, the device may carry on dragging on its own side and ask for when they are dropped there.
public struct DragOffer: Codable, Hashable, Sendable {
    public struct File: Codable, Hashable, Sendable {
        public var name: String
        public var size: UInt64
        /// A folder: sent zipped (`FolderZip`) when asked for, its size then the zip's.
        public var folder: Bool?
        public init(name: String, size: UInt64, folder: Bool? = nil) { self.name = name; self.size = size; self.folder = folder }
    }
    public var id: UInt32
    public var files: [File]
    public init(id: UInt32, files: [File]) { self.id = id; self.files = files }
}

/// One file of a drag offer (kind 37), sent back as kind 34 parts with `transfer` as their ID.
public struct FileRequest: Codable, Hashable, Sendable {
    public var offer: UInt32
    public var index: Int
    public var transfer: UInt32
    /// True: the file was dragged to the device's Trash; the host moves it to its own instead of sending it.
    public var trash: Bool?
    public init(offer: UInt32, index: Int, transfer: UInt32, trash: Bool? = nil) {
        self.offer = offer; self.index = index; self.transfer = transfer; self.trash = trash
    }
}

#if os(macOS)
/// A folder as one file for kinds 34–37: zipped with `ditto` (Finder's own archive: resource forks,
/// extended attributes, symbolic links kept), unpacked where it lands.
public enum FolderZip {
    /// `folder` zipped into a new temporary file, its top folder kept; nil when ditto fails.
    public static func zip(_ folder: URL) -> URL? {
        let out = FileManager.default.temporaryDirectory.appendingPathComponent("sill-\(UUID().uuidString).zip")
        return run(["-c", "-k", "--sequesterRsrc", "--keepParent", folder.path, out.path]) ? out : nil
    }

    /// `zip` unpacked into `directory`; the folder it held (named `name`), or nil.
    public static func unzip(_ zip: URL, into directory: URL, name: String) -> URL? {
        guard run(["-x", "-k", zip.path, directory.path]) else { return nil }
        let folder = directory.appendingPathComponent(name)
        return FileManager.default.fileExists(atPath: folder.path) ? folder : nil
    }

    private static func run(_ args: [String]) -> Bool {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
        p.arguments = args
        guard (try? p.run()) != nil else { return false }
        p.waitUntilExit()
        return p.terminationStatus == 0
    }
}
#endif

/// Input over a window shown beside the streamed one: kind 8, its InputEvent with a `window` key beside
/// the event's own (a fraction of that window's picture, as kind 8's is of the streamed one; window IDs
/// as side streams name them), sent only to a host whose list says `windowInput`: a host from before
/// ignores the key, and would take the input as the streamed window's. Generation 1 (#66's): the host
/// takes only a pointer move, a scroll and a scroll's phases this way, what a Mac lets a person do to a
/// window that is visible but not in front without bringing it forward (a click focuses the window,
/// which makes it the streamed one). Generation 2 (coherence, `WindowInputGeneration.every`): every
/// event, the host activating the window first for a click or a key.
public struct WindowInput: Hashable {
    public var window: UInt32?
    public var event: InputEvent
    public init(window: UInt32?, event: InputEvent) { self.window = window; self.event = event }

    /// Whether a host of generation 1 takes `event` for a window not in front.
    public static func allowed(_ event: InputEvent) -> Bool {
        switch event {
        case .pointer(.move, _, _), .scroll, .scrollGesture: return true
        default: return false
        }
    }

    /// Kind 8's payload: the event alone without a window, as every device has sent it.
    public func encoded() -> Data {
        let plain = Wire.encode(event)
        guard let window, var object = (try? JSONSerialization.jsonObject(with: plain)) as? [String: Any] else { return plain }
        object["window"] = window
        return (try? JSONSerialization.data(withJSONObject: object, options: [.sortedKeys])) ?? plain
    }

    /// A kind 8 payload, with or without a window.
    public static func decode(_ data: Data) -> WindowInput? {
        guard var object = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return nil }
        guard object["window"] != nil else { return Wire.decode(InputEvent.self, from: data).map { WindowInput(window: nil, event: $0) } }
        guard object.count == 2,
              let window = (object.removeValue(forKey: "window") as? NSNumber)?.uint32Value,
              let rest = try? JSONSerialization.data(withJSONObject: object),
              let event = Wire.decode(InputEvent.self, from: rest) else { return nil }
        return WindowInput(window: window, event: event)
    }
}

/// Windows the person at the host stopped sharing (kind 41): each went back to where it was on the
/// Mac and stopped streaming; a device closes its windows of them and may open them again. Window IDs
/// as side streams name them (a Desktop window's display tagged, DesktopWindow).
public struct TakenBack: Codable, Hashable, Sendable {
    public var windows: [UInt32]
    public init(windows: [UInt32]) { self.windows = windows }
}
