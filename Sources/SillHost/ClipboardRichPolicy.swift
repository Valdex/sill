import Foundation
import StreamProtocol

/// The clipboard's later generations, the host's rules (kind 32; docs/coherence-protocol.md, "Clipboard
/// generations"): what a subscription takes (`accepts`), which of the Mac's pasteboard types a look reads
/// for it, the kind 32 each generation gets (an image, HTML and RTF beside the text in generation 2, an
/// offer of the Mac's copied files in 3, `concealed` for a password manager's copy), what of a device's
/// kind 32 the Mac's pasteboard takes, and how the device's copied files are kept on the Mac. Generation
/// 1 (text) stays ClipboardPolicy's, byte for byte: a device that subscribed with `{}` (every iPhone and
/// iPad) gets exactly what it got before. HostClipboard and ClipboardRich.swift read and write the
/// pasteboard; this decides.
///
/// Pure (Foundation and StreamProtocol), so `Tests/checks/clipboard-rich` runs it on Linux too.
enum ClipboardRichPolicy {
    /// The generation the host offers while it shares the clipboard: 3 on a host that captures this Mac,
    /// 2 on a synthetic host, which writes no file (FileDrops) and so takes and offers none.
    static func offered(synthetic: Bool) -> Int { synthetic ? ClipboardGeneration.rich : ClipboardGeneration.files }

    // MARK: Pasteboard types

    static let pngType = "public.png"
    static let jpegType = "public.jpeg"
    static let tiffType = "public.tiff"
    static let htmlType = "public.html"
    static let rtfType = "public.rtf"
    static let fileURLType = "public.file-url"
    /// The markers a password manager puts on its copy (nspasteboard.org's concealed and transient types,
    /// the older transient type, 1Password's own): the item goes like any other since 2026-10-29
    /// (ClipboardPolicy), marked `concealed` for a device that takes generation 2 or later.
    static let concealedTypes = ["org.nspasteboard.ConcealedType", "org.nspasteboard.TransientType",
                                 "de.petermaurer.TransientPasteboardType", "com.agilebits.onepassword"]

    /// Whether an item with these types (every pasteboard item's) is a concealed copy.
    static func concealed(types: [String]) -> Bool { types.contains { concealedTypes.contains($0) } }

    // MARK: Subscriptions

    /// The generation a connection's subscription takes after its kind 32 (`current`: before it, nil for
    /// none): a bare one (no text, image, HTML, RTF or files) says it by `accepts`, 1 without it (`{}`);
    /// a copy that carries `accepts` says it again, one without keeps what it was (1 for a connection that
    /// never said). Never more than the host offers, never less than 1. Nil for a stop or a payload that
    /// does not decode (ClipboardPolicy's rules decide those).
    static func accepts(after payload: Data, current: Int?, offered: Int) -> Int? {
        guard let clip = Wire.decode(Clipboard.self, from: payload), clip.stop != true else { return nil }
        let bare = clip.text == nil && clip.image == nil && clip.html == nil && clip.rtf == nil && clip.files == nil
        let said = bare ? (clip.accepts ?? ClipboardGeneration.text) : (clip.accepts ?? current ?? ClipboardGeneration.text)
        return min(max(said, ClipboardGeneration.text), max(offered, ClipboardGeneration.text))
    }

    // MARK: The Mac → devices

    enum Read: Hashable, Sendable { case html, rtf, png, tiff, files }

    /// What a look reads, beyond generation 1's text, of an item with `types` for subscribers that take up
    /// to `generation`. Nothing below 2. File URLs (generation 3) leave the image out: with files, Finder's
    /// item carries their icon, not a picture someone copied. A PNG is read before a TIFF, which only
    /// stands in when there is none (and is made a PNG).
    static func reads(types: [String], generation: Int) -> Set<Read> {
        guard generation >= ClipboardGeneration.rich else { return [] }
        var out: Set<Read> = []
        let files = types.contains(fileURLType)
        if files, generation >= ClipboardGeneration.files { out.insert(.files) }
        if types.contains(htmlType) { out.insert(.html) }
        if types.contains(rtfType) { out.insert(.rtf) }
        if !files {
            if types.contains(pngType) { out.insert(.png) } else if types.contains(tiffType) { out.insert(.tiff) }
        }
        return out
    }

    /// One item of the Mac's pasteboard as a look read it.
    struct MacItem: Equatable, Sendable {
        var text: String?
        var html: String?
        var rtf: Data?
        var png: Data?
        /// Generation 3's offer of the copied files (HostClipboard makes it).
        var files: ClipboardFiles?
        var concealed = false
        init(text: String? = nil, html: String? = nil, rtf: Data? = nil, png: Data? = nil, files: ClipboardFiles? = nil, concealed: Bool = false) {
            self.text = text; self.html = html; self.rtf = rtf; self.png = png; self.files = files; self.concealed = concealed
        }
    }

    /// What of an item a kind 32 left out, for the host's line (never what was copied).
    enum Left: String, Equatable, Sendable {
        case textTooLong = "text over 256 KB"
        case image = "an image (too large with the rest)"
        case rtf = "RTF (too large with the rest)"
        case html = "HTML (too large with the rest)"
        case files = "the files' offer (too large)"
    }

    /// The kind 32 a subscription of `generation` (2 or 3) gets for `item`: the text (at most
    /// `Clipboard.maxTextBytes`, as generation 1's), then what the generation adds (generation 2: HTML,
    /// RTF and the PNG; 3: the files' offer too), and `concealed` true for a concealed item. The JSON stays
    /// within `cap` (the device's cap on a host's message, 4 MiB): the image goes first, then the RTF, the
    /// HTML, the files' offer, the text. Nil when nothing is left, with what was left out.
    static func payload(_ item: MacItem, generation: Int, cap: Int = StreamMessage.maxOtherHostPayload) -> (payload: Data?, left: [Left]) {
        var left: [Left] = []
        guard generation >= ClipboardGeneration.rich else { return (nil, left) }
        var text = item.text.flatMap { $0.isEmpty ? nil : $0 }
        if let t = text, t.utf8.count > Clipboard.maxTextBytes { text = nil; left.append(.textTooLong) }
        var html = item.html.flatMap { $0.isEmpty ? nil : $0 }
        var rtf = item.rtf.flatMap { $0.isEmpty ? nil : $0 }
        var png = item.png.flatMap { $0.isEmpty ? nil : $0 }
        var files = generation >= ClipboardGeneration.files ? item.files.flatMap { ($0.files ?? []).isEmpty ? nil : $0 } : nil
        while true {
            if text == nil && html == nil && rtf == nil && png == nil && files == nil { return (nil, left) }
            let clip = Clipboard(text: text, image: png, html: html, rtf: rtf, files: files, concealed: item.concealed ? true : nil)
            let data = Wire.encode(clip)
            if !data.isEmpty, data.count <= cap { return (data, left) }
            if png != nil { png = nil; left.append(.image) }
            else if rtf != nil { rtf = nil; left.append(.rtf) }
            else if html != nil { html = nil; left.append(.html) }
            else if files != nil { files = nil; left.append(.files) }
            else { text = nil; left.append(.textTooLong) }
        }
    }

    /// Each subscriber's kind 32 for one change: generation 1's (ClipboardPolicy.hostDecides, unchanged)
    /// for a subscription of 1, `payload` for 2 and 3. `subscribers`: each one's generation.
    static func sends<ID: Hashable>(to subscribers: [(ID, Int)], textPayload: Data?, item: MacItem?) -> [(ID, Data)] {
        var byGeneration: [Int: Data?] = [:]
        var out: [(ID, Data)] = []
        for (id, generation) in subscribers {
            let g = min(max(generation, ClipboardGeneration.text), ClipboardGeneration.files)
            if byGeneration[g] == nil {
                byGeneration[g] = g == ClipboardGeneration.text ? textPayload : item.flatMap { payload($0, generation: g).payload }
            }
            if let p = byGeneration[g] ?? nil { out.append((id, p)) }
        }
        return out
    }

    // MARK: A device → the Mac

    /// What of a device's kind 32 the Mac's pasteboard takes beyond its text: HTML, RTF and an image
    /// (PNG or JPEG, by its first bytes) from generation 2, the transfers of its copied files from 3.
    struct DeviceItem: Equatable, Sendable {
        var text: String?
        var html: String?
        var rtf: Data?
        var image: Data?
        /// The image's pasteboard type (`pngType` or `jpegType`).
        var imageType: String?
        /// The transfers (kind 34) of the device's copied files, in order.
        var transfers: [UInt32] = []
        /// Those of `transfers` that are folders sent zipped (FolderZip): unpacked as they are kept.
        var folders: Set<UInt32> = []
        /// The names the device gave its files, by transfer (a folder's without ".zip").
        var names: [UInt32: String] = [:]
    }

    /// The most files one copy names.
    static let maxFiles = 256

    /// A device's kind 32 as the Mac's pasteboard takes it, for a host offering `offered`: nil when it
    /// carries nothing of a later generation (generation 1's rules take it, as before), or when it is a
    /// stop or does not decode. An image that is neither a PNG nor a JPEG, HTML that is empty, and more
    /// than `maxFiles` transfers are left out; the text rides along when it is within
    /// `Clipboard.maxTextBytes`.
    static func fromDevice(_ payload: Data, offered: Int) -> DeviceItem? {
        guard offered >= ClipboardGeneration.rich, let clip = Wire.decode(Clipboard.self, from: payload), clip.stop != true else { return nil }
        var item = DeviceItem()
        if let html = clip.html, !html.isEmpty { item.html = html }
        if let rtf = clip.rtf, !rtf.isEmpty { item.rtf = rtf }
        if let image = clip.image {
            if isPNG(image) { item.image = image; item.imageType = pngType } else if isJPEG(image) { item.image = image; item.imageType = jpegType }
        }
        if offered >= ClipboardGeneration.files, let ids = clip.files?.transfers, !ids.isEmpty, ids.count <= maxFiles {
            var seen = Set<UInt32>()
            let files = clip.files?.files ?? []
            for (i, id) in ids.enumerated() where seen.insert(id).inserted {
                item.transfers.append(id)
                if files.count == ids.count {
                    if files[i].folder == true { item.folders.insert(id) }
                    if !files[i].name.isEmpty { item.names[id] = files[i].name }
                }
            }
        }
        guard item.html != nil || item.rtf != nil || item.image != nil || !item.transfers.isEmpty else { return nil }
        if let text = clip.text, !text.isEmpty, text.utf8.count <= Clipboard.maxTextBytes { item.text = text }
        return item
    }

    static func isPNG(_ d: Data) -> Bool { d.count >= 8 && Array(d.prefix(8)) == [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A] }
    static func isJPEG(_ d: Data) -> Bool { d.count >= 3 && Array(d.prefix(3)) == [0xFF, 0xD8, 0xFF] }

    // MARK: The device's copied files on the Mac

    /// Where they are kept: `~/Library/Caches/<bundle>/Clip/<set>/`, one folder per copy.
    static let stagingFolder = "Clip"
    /// The most one copy's files may be (FileChunk.maxTotal, what a drop may be; the Windows client sends
    /// up to 128 MB).
    static let maxSetBytes = FileChunk.maxTotal
    /// The most every kept copy may be together.
    static let maxStagedBytes: UInt64 = 1 << 30
    /// Copies kept: the one on the pasteboard and the one before it (an app may still be reading it).
    static let keptSets = 2
    /// A kept copy older than this goes, whatever else holds.
    static let maxAge: Double = 86_400

    struct StagedSet: Equatable, Sendable {
        var name: String
        var bytes: UInt64
        var createdAt: Double
    }

    /// The copies to remove from `sets` at `now`: older than `maxAge` (the newest too: a day-old copy is
    /// nobody's paste), beyond the newest `keptSets`, then the oldest until the rest are within `cap`
    /// (never the newest, which is on the pasteboard).
    static func prune(_ sets: [StagedSet], now: Double, cap: UInt64 = maxStagedBytes) -> [String] {
        let newest = sets.sorted { ($0.createdAt, $0.name) > ($1.createdAt, $1.name) }
        var kept: [StagedSet] = []
        var gone: [String] = []
        for (i, s) in newest.enumerated() {
            if now - s.createdAt > maxAge || i >= keptSets { gone.append(s.name) } else { kept.append(s) }
        }
        var total = kept.reduce(UInt64(0)) { $0 &+ $1.bytes }
        while total > cap, kept.count > 1 {
            let oldest = kept.removeLast()
            gone.append(oldest.name)
            total -= oldest.bytes
        }
        return gone
    }

    /// A copy's files go on the pasteboard only when they are within `maxSetBytes` together.
    static func takesSet(bytes: [UInt64]) -> Bool {
        var total: UInt64 = 0
        for b in bytes {
            let (sum, over) = total.addingReportingOverflow(b)
            if over { return false }
            total = sum
        }
        return total <= maxSetBytes
    }

    // MARK: The Mac's copied files offered

    /// Offers kept, newest last: a device fetches the files of the copy it holds, which a later copy
    /// replaces on it; an older one stays fetchable a little while (a paste under way).
    static let keptOffers = 4
}
