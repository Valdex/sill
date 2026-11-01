import Foundation

/// Opening documents on the Mac from a device (kind 7's `documents`, Coherence M5; docs/coherence-protocol.md,
/// "Opening documents on the Mac"): which paths the host opens and with which app. A device maps a file on
/// its own side through the Mac's share it mounted to an absolute path on the Mac, and asks the host to
/// open it with an app (its bundle ID) or, with an empty bundle ID, each with its default app. The host
/// opens only absolute, plain paths (no `.` or `..` part, no empty part, no control character) to files or
/// folders that are there and readable; anything else is refused with a line. Documents.swift asks the
/// file system and opens them (NSWorkspace).
///
/// Pure (Foundation; the file system is asked through `look`), so `Tests/checks/documents` runs it on
/// Linux too.
enum DocumentPolicy {
    /// `WindowList.documents`.
    static let generation = 1
    /// The most documents one kind 7 opens; the rest are refused.
    static let maxDocuments = 64
    /// The longest path taken, in bytes of UTF-8 (macOS's PATH_MAX).
    static let maxPathBytes = 1024

    enum Refusal: String, Equatable, Sendable {
        case notAbsolute = "not an absolute path"
        case notPlain = "not a plain path (a \".\" or \"..\" part, or an empty one)"
        case tooLong = "longer than 1024 bytes"
        case control = "a control character in it"
        case missing = "not there"
        case unreadable = "not readable"
        case notFileOrFolder = "neither a file nor a folder"
        case tooMany = "more than 64 documents at once"
    }

    /// What the file system says of a path (following a symbolic link, as opening it would).
    enum Kind: Equatable, Sendable { case file, folder, other, missing }

    /// A path's shape alone: nil when it is absolute and plain.
    static func shape(_ path: String) -> Refusal? {
        guard path.utf8.count <= maxPathBytes else { return .tooLong }
        guard !path.unicodeScalars.contains(where: { $0.value < 0x20 || $0.value == 0x7F }) else { return .control }
        guard path.hasPrefix("/") else { return .notAbsolute }
        guard path != "/" else { return nil }
        let parts = path.dropFirst().split(separator: "/", omittingEmptySubsequences: false)
        // A trailing slash (a folder's "/Users/v/Documents/") is the one empty part allowed.
        let inner = parts.last == "" ? parts.dropLast() : parts[...]
        guard !inner.isEmpty, !inner.contains(where: { $0.isEmpty || $0 == "." || $0 == ".." }) else { return .notPlain }
        return nil
    }

    /// A bundle ID kind 7 may name: empty (each document with its default app), or 1 to 255 characters of
    /// letters, digits, `.`, `-` and `_`, not starting with a dot.
    static func bundleIDValid(_ id: String) -> Bool {
        guard !id.isEmpty else { return true }
        guard id.utf8.count <= 255, !id.hasPrefix(".") else { return false }
        return id.unicodeScalars.allSatisfy { $0.isASCII && (CharacterSet.alphanumerics.contains($0) || $0 == "." || $0 == "-" || $0 == "_") }
    }

    /// One path, its shape and then what the file system says.
    static func judge(_ path: String, kind: Kind, readable: Bool) -> Refusal? {
        if let bad = shape(path) { return bad }
        switch kind {
        case .missing: return .missing
        case .other: return .notFileOrFolder
        case .file, .folder: return readable ? nil : .unreadable
        }
    }

    struct Refused: Equatable, Sendable {
        var path: String
        var why: Refusal
    }

    struct Plan: Equatable, Sendable {
        /// The app's bundle ID; nil: each document with its default app.
        var app: String?
        /// The paths to open, in the order asked, each once.
        var open: [String] = []
        var refused: [Refused] = []
    }

    /// A kind 7's documents: the app (nil for an empty bundle ID; the caller refuses a bundle ID that is
    /// not `bundleIDValid` before this), and each path opened or refused. `look` is asked only for a path
    /// whose shape is good, and once per path; a path asked twice is opened once.
    static func plan(bundleID: String, documents: [String], look: (String) -> (Kind, Bool)) -> Plan {
        var plan = Plan(app: bundleID.isEmpty ? nil : bundleID)
        var seen = Set<String>()
        for (i, path) in documents.enumerated() {
            guard i < maxDocuments else { plan.refused.append(Refused(path: path, why: .tooMany)); continue }
            guard seen.insert(path).inserted else { continue }
            if let bad = shape(path) { plan.refused.append(Refused(path: path, why: bad)); continue }
            let (kind, readable) = look(path)
            if let bad = judge(path, kind: kind, readable: readable) { plan.refused.append(Refused(path: path, why: bad)) } else { plan.open.append(path) }
        }
        return plan
    }

    /// A path as the host's line names it: its last part only (a device's paths say where someone keeps
    /// their files), cleaned of anything that could forge a line.
    static func logName(_ path: String) -> String {
        let last = path.split(separator: "/").last.map(String.init) ?? path
        let clean = String(last.unicodeScalars.filter { $0.value >= 0x20 && $0.value != 0x7F }.map(Character.init))
        return clean.count > 80 ? String(clean.prefix(80)) + "…" : clean
    }
}
