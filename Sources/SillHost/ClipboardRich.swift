import AppKit
import ImageIO
import UniformTypeIdentifiers
import StreamProtocol

// The clipboard's generations 2 and 3 on the host (kind 32; docs/coherence-protocol.md, "Clipboard
// generations"; the rules are ClipboardRichPolicy's): what HostClipboard needs beyond text. Reading a
// pasteboard item's richer types (on HostClipboard's board queue, behind its `hostLooks` gate, like every
// read), a TIFF made a PNG, the Mac's copied files offered to devices (kind 37 fetches them), and a
// device's copied files kept where the pasteboard can name them.

/// Reads one pasteboard item's richer types for a look (ClipboardRichPolicy.reads), on the queue the
/// pasteboard is read on. Only types the item has and the subscribers take are read.
enum RichPasteboard {
    /// What a look read beyond the text: the item (its `files` left for the main actor to offer), and
    /// the copied files' URLs (files and folders that are there).
    struct Read {
        var item: ClipboardRichPolicy.MacItem
        var fileURLs: [URL]
    }

    /// `types`: every item's types; `text`: generation 1's text as its look read it (nil: none read).
    static func read(_ board: NSPasteboard, types: [String], generation: Int, text: String?) -> Read? {
        let reads = ClipboardRichPolicy.reads(types: types, generation: generation)
        var item = ClipboardRichPolicy.MacItem(text: text, concealed: ClipboardRichPolicy.concealed(types: types))
        var urls: [URL] = []
        if reads.contains(.html) { item.html = board.string(forType: .html) }
        if reads.contains(.rtf) { item.rtf = board.data(forType: .rtf) }
        if reads.contains(.png) {
            item.png = board.data(forType: .png)
        } else if reads.contains(.tiff) {
            item.png = board.data(forType: .tiff).flatMap(ClipboardImages.png(fromTIFF:))
        }
        if reads.contains(.files) {
            urls = (board.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL] ?? [])
                .filter { url in
                    let v = try? url.resourceValues(forKeys: [.isRegularFileKey, .isDirectoryKey])
                    return v?.isRegularFile == true || v?.isDirectory == true
                }
        }
        return Read(item: item, fileURLs: urls)
    }
}

/// Images for the clipboard.
enum ClipboardImages {
    /// A TIFF (what an app that copies an image puts on the pasteboard when it has no PNG) as a PNG, by
    /// ImageIO; nil when it does not read. Thread-safe.
    static func png(fromTIFF tiff: Data) -> Data? {
        guard let source = CGImageSourceCreateWithData(tiff as CFData, nil),
              let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else { return nil }
        let out = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(out, UTType.png.identifier as CFString, 1, nil) else { return nil }
        CGImageDestinationAddImage(dest, image, nil)
        return CGImageDestinationFinalize(dest) ? out as Data : nil
    }
}

/// The Mac's copied files offered to devices of generation 3 (`Clipboard.files`): each offer's files and
/// the devices it went to, which alone may fetch them (kind 37 with its `offer`). Offer IDs share kind
/// 37's space with drag offers (DragOut): random, never 0. The newest `ClipboardRichPolicy.keptOffers`
/// are kept. Main actor.
@MainActor
final class ClipboardOffers {
    private var offers: [UInt32: (urls: [URL], to: Set<ObjectIdentifier>)] = [:]
    private var order: [UInt32] = []

    /// An offer of `urls` to the devices `to`: its kind 32 `files`.
    func make(_ urls: [URL], to devices: Set<ObjectIdentifier>) -> ClipboardFiles {
        var id = UInt32.random(in: 1...UInt32.max)
        while offers[id] != nil { id = UInt32.random(in: 1...UInt32.max) }
        offers[id] = (urls, devices)
        order.append(id)
        while order.count > ClipboardRichPolicy.keptOffers { offers[order.removeFirst()] = nil }
        let files = urls.map { url -> DragOffer.File in
            let v = try? url.resourceValues(forKeys: [.isDirectoryKey, .fileSizeKey])
            let folder = v?.isDirectory == true
            return DragOffer.File(name: url.lastPathComponent, size: folder ? 0 : UInt64(v?.fileSize ?? 0), folder: folder ? true : nil)
        }
        return ClipboardFiles(offer: id, files: files)
    }

    /// Whether `offer` is one of these (else a drag offer's, DragOut's).
    func knows(_ offer: UInt32) -> Bool { offers[offer] != nil }

    /// The file a kind 37 asks for, when its device was offered it and the index is the offer's.
    func file(_ r: FileRequest, from device: ObjectIdentifier) -> URL? {
        guard let o = offers[r.offer], o.to.contains(device), o.urls.indices.contains(r.index) else { return nil }
        return o.urls[r.index]
    }

    /// A device gone: no offer is fetchable by it any more.
    func forget(_ device: ObjectIdentifier) {
        for (id, o) in offers where o.to.contains(device) {
            var left = o
            left.to.remove(device)
            offers[id] = left.to.isEmpty ? nil : left
        }
        order.removeAll { offers[$0] == nil }
    }

    func removeAll() {
        offers = [:]
        order = []
    }
}

/// A device's copied files kept on the Mac, so the pasteboard can name them as file URLs:
/// `~/Library/Caches/<bundle>/Clip/<copy>/`, one folder per copy, its files moved there from where
/// FileDrops received them (a folder that came zipped unpacked there), pruned by
/// ClipboardRichPolicy.prune (two copies, a day, 1 GiB). Thread-safe: everything runs on its own queue.
final class ClipStage: @unchecked Sendable {
    private let queue = DispatchQueue(label: "sill.clipboard.stage", qos: .userInitiated)
    let root: URL

    init() {
        let caches = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
        root = caches.appendingPathComponent(Bundle.main.bundleIdentifier ?? "me.saffer.sill", isDirectory: true)
            .appendingPathComponent(ClipboardRichPolicy.stagingFolder, isDirectory: true)
    }

    /// `files` (transfer, where FileDrops put it), in order, kept as one copy: a folder of `folders` is
    /// unpacked, each named by `names` (the device's name for it) or its own. `done` gets the kept URLs,
    /// none when they are over `ClipboardRichPolicy.maxSetBytes` together or nothing could be kept
    /// (what was received is removed then). On the stage's queue; `done` runs there.
    func keep(_ files: [(UInt32, URL)], folders: Set<UInt32>, names: [UInt32: String], done: @escaping @Sendable ([URL]) -> Void) {
        queue.async { [self] in
            let fm = FileManager.default
            let sizes = files.map { UInt64((try? $0.1.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0) }
            guard ClipboardRichPolicy.takesSet(bytes: sizes) else {
                for (_, url) in files { try? fm.removeItem(at: url) }
                print("Clipboard: a device's copied files are over \(ClipboardRichPolicy.maxSetBytes >> 20) MB together; not put on this Mac's clipboard.")
                done([])
                return
            }
            let set = root.appendingPathComponent(UUID().uuidString, isDirectory: true)
            guard (try? fm.createDirectory(at: set, withIntermediateDirectories: true)) != nil else {
                for (_, url) in files { try? fm.removeItem(at: url) }
                print("Clipboard: could not make \(set.path) for a device's copied files.")
                done([])
                return
            }
            var kept: [URL] = []
            for (id, url) in files {
                let name = DropFileName.safe(names[id] ?? (folders.contains(id) ? url.deletingPathExtension().lastPathComponent : url.lastPathComponent))
                if folders.contains(id) {
                    if let folder = Self.unpack(url, into: set, as: name) { kept.append(folder) }
                    try? fm.removeItem(at: url)
                } else {
                    let dest = Self.unique(set.appendingPathComponent(name))
                    if (try? fm.moveItem(at: url, to: dest)) != nil { kept.append(dest) } else { try? fm.removeItem(at: url) }
                }
            }
            if kept.isEmpty { try? fm.removeItem(at: set) }
            prune(keeping: set)
            done(kept)
        }
    }

    /// Copies past ClipboardRichPolicy.prune's rules removed (`keeping` never). At launch too.
    func prune(keeping: URL? = nil) {
        let fm = FileManager.default
        guard let entries = try? fm.contentsOfDirectory(at: root, includingPropertiesForKeys: [.creationDateKey, .isDirectoryKey]) else { return }
        let now = Date().timeIntervalSince1970
        var sets: [ClipboardRichPolicy.StagedSet] = []
        for url in entries where url.standardizedFileURL != keeping?.standardizedFileURL {
            let created = (try? url.resourceValues(forKeys: [.creationDateKey]).creationDate)?.timeIntervalSince1970 ?? 0
            sets.append(.init(name: url.lastPathComponent, bytes: Self.bytes(url), createdAt: created))
        }
        if let keeping { sets.append(.init(name: keeping.lastPathComponent, bytes: Self.bytes(keeping), createdAt: now + 1)) }
        for name in ClipboardRichPolicy.prune(sets, now: now) where name != keeping?.lastPathComponent {
            try? fm.removeItem(at: root.appendingPathComponent(name))
        }
    }

    /// Launch: what an earlier run kept past the rules goes. Off the caller's thread.
    func pruneSoon() { queue.async { [self] in prune() } }

    private static func bytes(_ url: URL) -> UInt64 {
        guard let e = FileManager.default.enumerator(at: url, includingPropertiesForKeys: [.fileSizeKey]) else { return 0 }
        var total: UInt64 = 0
        for case let f as URL in e { total &+= UInt64((try? f.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0) }
        return total
    }

    /// A zipped folder (FolderZip) unpacked into a scratch folder of `set`, its one top entry (beside
    /// ditto's __MACOSX) moved into `set` as `name`.
    private static func unpack(_ zip: URL, into set: URL, as name: String) -> URL? {
        let fm = FileManager.default
        let scratch = set.appendingPathComponent(".unpack-\(UUID().uuidString)", isDirectory: true)
        defer { try? fm.removeItem(at: scratch) }
        guard (try? fm.createDirectory(at: scratch, withIntermediateDirectories: true)) != nil,
              FolderZip.unzip(zip, into: scratch, name: "") != nil,
              let top = try? fm.contentsOfDirectory(at: scratch, includingPropertiesForKeys: nil).filter({ $0.lastPathComponent != "__MACOSX" }),
              top.count == 1 else {
            print("Clipboard: could not unpack a device's copied folder \(name).")
            return nil
        }
        let dest = unique(set.appendingPathComponent(name))
        return (try? fm.moveItem(at: top[0], to: dest)) != nil ? dest : nil
    }

    private static func unique(_ url: URL) -> URL {
        var candidate = url, n = 2
        let base = url.deletingPathExtension().lastPathComponent, ext = url.pathExtension
        while FileManager.default.fileExists(atPath: candidate.path) {
            candidate = url.deletingLastPathComponent().appendingPathComponent(ext.isEmpty ? "\(base) \(n)" : "\(base) \(n).\(ext)")
            n += 1
        }
        return candidate
    }
}
