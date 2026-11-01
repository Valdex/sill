import Foundation

// Coherence (this fork): several Mac windows on a device at once, each a window of the device's own,
// with their state, a richer clipboard, a second connection for bulk data and sound for Windows. The
// wire contract is docs/coherence-protocol.md, which a C# client is written against: this file and the
// fields below follow it word for word. Everything is additive under Compatibility.swift's rules (JSON,
// every field optional and left out when nil unless named otherwise, strings not enums, nothing renamed
// or retyped, kinds an older peer skips, a kind or field sent only to a peer that said it takes it).
//
// Here: the new payloads (kinds 44's, 45, 46, 48 and 49) and the generations. Fields added to existing
// payloads live with their types, since Swift adds no stored property in an extension:
// - SideStreamRequest (kind 29; SideStreams.swift): `coherence`, `focus`, `focusFPS`, `apps`, `all`,
//   `hidden`, `scale`.
// - WindowList (kind 2; Switcher.swift): `coherence`, `windowInput`, `fileDrop`, `liveDrag`, `data`,
//   `documents`, `iconSize`, beside #66's `sideStreams`.
// - WindowCommand.Action (kind 15; Switcher.swift): `activate`, `restore`.
// - LaunchApp (kind 7; Switcher.swift): `documents`.
// - Hello (kind 23; Compatibility.swift): `coherence`, `iconSize`.
// - Clipboard (kind 32; Clipboard.swift): `accepts`, `html`, `rtf`, `files` (`image` was there).
// - AudioCodec (kind 43; Audio.swift): `pcmS16LE`, "pcm-s16le".
//
// Pure: Foundation only, so it is type-checked on its own with swiftc (Tests/checks/coherence-wire).

/// The generations this fork adds, as `WindowList` and `Hello` carry them.
public enum CoherenceGeneration {
    /// `WindowList.coherence` and `Hello.coherence`.
    public static let current = 1
}

/// `WindowList.windowInput`'s values.
public enum WindowInputGeneration {
    /// Pull request #66's: a move, a scroll and a scroll's phases over a window shown beside the streamed
    /// one (`WindowInput.allowed`).
    public static let movesAndScrolls = 1
    /// Coherence's: every kind 8 event may carry `window`; the host activates the window first for a
    /// click or a key.
    public static let every = 2
}

/// `WindowList.clipboard`'s generations past main's 1 (text).
public enum ClipboardGeneration {
    public static let text = 1
    /// `image` (PNG), `html`, `rtf`.
    public static let rich = 2
    /// `files` too.
    public static let files = 3
}

/// Kind 44's optional payload, `{"window":4711}`, to a host whose list says `coherence` 1: only that
/// window's encoder makes a keyframe. An empty payload (every device before) is the picked stream's.
public struct KeyframeRequest: Codable, Hashable, Sendable {
    public var window: UInt32?
    public init(window: UInt32? = nil) { self.window = window }

    /// The window a kind 44 payload names; nil for an empty payload, one that does not decode, or one
    /// that names none: the picked stream's, as before.
    public static func window(in payload: Data) -> UInt32? {
        guard !payload.isEmpty else { return nil }
        return Wire.decode(KeyframeRequest.self, from: payload)?.window
    }
}

/// Kind 45, host → device: the shared windows' state, modelled on RDP RemoteApp's window orders. `gen`
/// rises with every message and a device drops one older than the last it applied; `full` true lists
/// every window the device should have (sent when coherence starts and after a gap), otherwise only
/// windows that changed, and `removed`.
public struct WindowStateMessage: Codable, Hashable, Sendable {
    /// Rises with every message (always sent).
    public var gen: Int
    public var full: Bool?
    /// The `token` of the kind 46 this answers.
    public var answering: Int?
    public var windows: [WindowStateEntry]?
    public var removed: [UInt32]?
    /// The Mac's own stacking, front to back, for information.
    public var order: [UInt32]?
    /// The Mac's key window, for information.
    public var focus: UInt32?
    /// macOS's Secure Event Input is on (a password field): typed keys may not land.
    public var secureInput: Bool?

    public init(gen: Int, full: Bool? = nil, answering: Int? = nil, windows: [WindowStateEntry]? = nil, removed: [UInt32]? = nil,
                order: [UInt32]? = nil, focus: UInt32? = nil, secureInput: Bool? = nil) {
        self.gen = gen; self.full = full; self.answering = answering; self.windows = windows; self.removed = removed
        self.order = order; self.focus = focus; self.secureInput = secureInput
    }
}

/// One window of a kind 45. Sizes, rects and lengths are in points; a rect is [x, y, width, height].
public struct WindowStateEntry: Codable, Hashable, Sendable {
    /// The host's window ID (`WindowInfo.id`; always sent).
    public var id: UInt32
    /// The app's bundle ID.
    public var app: String?
    public var appName: String?
    public var title: String?
    /// One of `WindowStateEntry.roles`; a role a device does not know is a `window`.
    public var role: String?
    /// A top-level window's size, [width, height] (its place on Sill's desk means nothing to the device).
    public var size: [Double]?
    /// The least and the most it takes, when known (learned from sizes it refused).
    public var min: [Double]?
    public var max: [Double]?
    public var resizable: Bool?
    public var focused: Bool?
    public var minimized: Bool?
    public var fullScreen: Bool?
    public var modal: Bool?
    /// The height of a plain title bar the device may crop and replace with its own; 0 or nil: a unified
    /// toolbar, keep it.
    public var titleBar: Double?
    /// The window came by itself (kind 29's `apps` or `all`).
    public var joined: Bool?
    /// The traffic lights' rects from the window's top-left, for a device that draws the Mac's frame.
    public var controls: WindowControls?
    /// A child's owner (a sheet, a menu, a popover…).
    public var owner: UInt32?
    /// A child's frame relative to its owner's top-left.
    public var rect: [Double]?
    /// True: the child shows inside its owner's picture (`wholeApp`); false: it spills out, and the device
    /// asks for it as a side stream of its own.
    public var inCrop: Bool?

    public init(id: UInt32, app: String? = nil, appName: String? = nil, title: String? = nil, role: String? = nil,
                size: [Double]? = nil, min: [Double]? = nil, max: [Double]? = nil, resizable: Bool? = nil, focused: Bool? = nil,
                minimized: Bool? = nil, fullScreen: Bool? = nil, modal: Bool? = nil, titleBar: Double? = nil, joined: Bool? = nil,
                controls: WindowControls? = nil, owner: UInt32? = nil, rect: [Double]? = nil, inCrop: Bool? = nil) {
        self.id = id; self.app = app; self.appName = appName; self.title = title; self.role = role
        self.size = size; self.min = min; self.max = max; self.resizable = resizable; self.focused = focused
        self.minimized = minimized; self.fullScreen = fullScreen; self.modal = modal; self.titleBar = titleBar; self.joined = joined
        self.controls = controls; self.owner = owner; self.rect = rect; self.inCrop = inCrop
    }

    /// `role`'s values. Strings, never an enum: a later role reads as `window` on an older device.
    public static let window = "window", sheet = "sheet", dialog = "dialog", panel = "panel", menu = "menu",
                      popover = "popover", tooltip = "tooltip"
    public static let roles = [window, sheet, dialog, panel, menu, popover, tooltip]

    /// The role as a device reads it: a role it does not know (or none) is a `window`.
    public static func knownRole(_ role: String?) -> String {
        guard let role, roles.contains(role) else { return window }
        return role
    }
}

/// The traffic lights' rects, [x, y, width, height] in points from the window's top-left.
public struct WindowControls: Codable, Hashable, Sendable {
    public var close: [Double]?
    public var minimize: [Double]?
    public var zoom: [Double]?
    public init(close: [Double]? = nil, minimize: [Double]? = nil, zoom: [Double]? = nil) {
        self.close = close; self.minimize = minimize; self.zoom = zoom
    }
}

/// Kind 46, device → host: size a window, in points, through Accessibility. Moves are the device's alone
/// and never sent. Answered by a kind 45 carrying the size the window took and `answering`. A device
/// sends it at the end of a resize and at most `maxPerSecond` times a second during one.
public struct PlaceWindow: Codable, Hashable, Sendable {
    public var id: UInt32
    /// [width, height] in points.
    public var size: [Double]
    public var token: Int?
    public init(id: UInt32, size: [Double], token: Int? = nil) { self.id = id; self.size = size; self.token = token }

    /// About the most a device sends during one resize.
    public static let maxPerSecond = 20
}

/// Kind 48, host → device on the session: the token a second connection to the same door and port
/// (ALPN `sill-data/1`, the same TLS and pinned keys; a plain door, plain) presents in its kind 49, sent
/// once after the window list to a device whose hello says `coherence` 1, from a host whose list says
/// `data` 1.
public struct DataTicket: Codable, Hashable, Sendable {
    /// Base64 of 32 random bytes.
    public var token: String
    public init(token: String) { self.token = token }

    /// The token's random bytes.
    public static let tokenBytes = 32
}

/// Kind 49, the first message on a `sill-data/1` connection, within `DataHello.deadline`: the session's
/// token. The host admits it only for the session holding that token and the same key, one per
/// session, closed with the session.
public struct DataHello: Codable, Hashable, Sendable {
    public var token: String
    public init(token: String) { self.token = token }

    /// The ALPN of the bulk connection.
    public static let alpn = "sill-data/1"
    /// Seconds the host waits for it.
    public static let deadline: Double = 4
    /// The cap on any message on the data connection: a 512 KB part and its header with room to spare.
    public static let maxPayload = (1 << 20) + 64
}

/// Generation 3's clipboard files (`Clipboard.files`). From the host: `offer` and `files`, fetched only
/// when pasted (the receiver sends kind 37 with that `offer`; clipboard offers share drag offers' IDs).
/// From a device: the files sent first as kind 34 parts, then `transfers`, their transfer IDs, which the
/// host writes to the Mac's pasteboard as file URLs of the received copies.
public struct ClipboardFiles: Codable, Hashable, Sendable {
    public var offer: UInt32?
    public var files: [DragOffer.File]?
    public var transfers: [UInt32]?
    public init(offer: UInt32? = nil, files: [DragOffer.File]? = nil, transfers: [UInt32]? = nil) {
        self.offer = offer; self.files = files; self.transfers = transfers
    }
}
