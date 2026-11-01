import AppKit
import Network
import StreamProtocol

/// The clipboard, the host's side (kind 32; docs/clipboard-plan.md). The rules are ClipboardPolicy's;
/// this reads and writes the pasteboard and keeps who subscribed.
///
/// - A device subscribes with any kind 32 (an empty one at connect, while its Share Clipboard is on)
///   and stops with `stop`; one line each ("Clipboard: iPad (iPad14,1) shares the clipboard."). A
///   device that leaves is forgotten without a line.
/// - While at least one subscribes, the pasteboard's change count is looked at four times a second.
///   The count as it stands when the first subscribes is taken as seen: what was on the clipboard then
///   is never sent, only what is copied while a device shares it. A change ClipboardPolicy.hostDecides
///   takes goes to every subscriber; one whose pasteboard has no items yet is looked at again for up
///   to a second first (`hostWaits`: an app clears, then writes). One from another device through
///   Universal Clipboard is never read; a password manager's copy goes like any other (2026-10-29).
/// - A device's text becomes the pasteboard's, written for this Mac only (`.currentHostOnly`: Universal
///   Clipboard does not carry it to the person's other devices, the sender among them) with the
///   marker `ClipboardPolicy.ownType` naming this launch; it goes to every other subscriber at once,
///   and the look never sends it again.
/// - Off (`HostConfig.shareClipboard`, `setEnabled`): every subscription dropped, nothing looked at,
///   and a kind 32 ignored without a word or an answer. The coordinator's window lists say
///   `clipboard` only while it is on.
/// - macOS's paste privacy (`MacClipboard`, ClipboardPolicy.MacAccess; macOS 15.4 and later): the look
///   reads the pasteboard only while macOS allows every read Sill makes on its own; while it would ask
///   (its alert at every copy) or denies, the look reads nothing, not even the change count, and what
///   is copied meanwhile is never sent (`accessStep`). One line when that changes (`accessLine`). A
///   device's text is written whatever macOS says: writing needs no leave.
///
/// Never logs what was copied. Stats: `clip.sent` (messages sent), `clip.recv` (texts taken).
///
/// Threading: main actor for the subscribers and the lines; the pasteboard itself is read and written
/// on a queue of its own (`Board`), one call at a time, so a slow read (an app asked for text it only
/// promised, which can take a while) never holds up the main actor, where devices' input is handled,
/// but for a device's text, which the main actor waits to see written (`Board.write`): the ⌘V a device
/// sends right after it (paste right away) must find it there.
/// The look is a Task.sleep loop, not a RunLoop timer, which the CLI's dispatchMain never runs.
@MainActor
final class HostClipboard {
    /// Share clipboard with devices.
    private(set) var enabled: Bool
    /// The generation this host offers while on (ClipboardRichPolicy.offered): 3, or 2 on a synthetic
    /// host, which writes no file.
    let offered: Int
    /// The window lists' `clipboard`: the generation while on, nil while off.
    var advertised: Int? { enabled ? offered : nil }

    // Generations 2 and 3 (ClipboardRich.swift; docs/coherence-protocol.md, "Clipboard generations").
    /// Each subscriber's generation (ClipboardRichPolicy.accepts): 1 for `{}`, as every iPhone and iPad.
    private var accepts: [ObjectIdentifier: Int] = [:]
    /// The Mac's copied files offered to generation 3 subscribers.
    private let offers = ClipboardOffers()
    /// Where a device's copied files are kept for the pasteboard (nil on a synthetic host).
    private let stage: ClipStage?
    /// A device's files by their transfers, whole here and taken (FileDrops.take), set by the coordinator.
    var takeFiles: (([UInt32], NWConnection) -> [UInt32: URL])?
    /// Runs the closure once those transfers are whole, or their parts stopped coming (FileDrops.whenWhole).
    var whenWhole: (([UInt32], NWConnection, @escaping () -> Void) -> Void)?

    private let server: StreamServer
    /// The pasteboard, on its own queue: `NSPasteboard.general`, or a synthetic host's own.
    private let board: Board
    /// A synthetic host's own pasteboard: its name, said at its first subscription. Nil for the Mac's.
    private let testBoardName: String?
    private var saidTestBoard = false
    /// TEST ONLY (SILL_TEST_CLIPBOARD_LOG=1, a synthetic host): each decision, with sizes, never text.
    private let testLog: Bool
    /// The test hooks' line for `start` (one, when a variable is set), nil without them.
    let testHookLine: String?

    /// The subscribers, by connection, and each one's name for the lines.
    private var subscribers: [ObjectIdentifier: (connection: NWConnection, who: String)] = [:]
    /// In the order they subscribed, for a stable order of sends.
    private var order: [ObjectIdentifier] = []
    private var look: Task<Void, Never>?
    /// Each connection's subscriptions and texts of the last second (ClipboardPolicy.hostReceives).
    private var arrivals: [ObjectIdentifier: [Double]] = [:]
    /// macOS's answer at the last look, for its line (ClipboardPolicy.accessLine); kept for the launch.
    private var reportedAccess: ClipboardPolicy.MacAccess?
    private lazy var ignored = RefusalSummary(queue: .main, categories: ["rate", "tooLong", "undecodable"]) { counts in
        ClipboardPolicy.Copy.ignoredLine(rate: counts["rate"] ?? 0, tooLong: counts["tooLong"] ?? 0,
                                         undecodable: counts["undecodable"] ?? 0)
    }

    /// `synthetic`: a host that never touches this Mac's clipboard (as it never reads the real
    /// pointer): a pasteboard of its own, `me.saffer.sill.test.<pid>`, or the one TEST ONLY
    /// `SILL_TEST_PASTEBOARD` names, so a test can write and read it. macOS asks about no pasteboard
    /// but the general one, so a synthetic host's reads are always allowed, unless TEST ONLY
    /// `SILL_TEST_CLIPBOARD_ACCESS` says otherwise: `allowed`, `notAsked`, `asks` or `denied`, or the
    /// path of a file holding one of them, read at each look (so a test can change it while it runs).
    init(server: StreamServer, enabled: Bool, launchID: String, synthetic: Bool,
         environment env: [String: String] = ProcessInfo.processInfo.environment) {
        self.server = server
        self.enabled = enabled
        offered = ClipboardRichPolicy.offered(synthetic: synthetic)
        stage = synthetic ? nil : ClipStage()
        stage?.pruneSoon()
        var lines: [String] = []
        if synthetic {
            let named = env["SILL_TEST_PASTEBOARD"].flatMap { $0.isEmpty ? nil : $0 }
            let name = named ?? "me.saffer.sill.test.\(getpid())"
            var access: @Sendable () -> ClipboardPolicy.MacAccess = { .allowed }
            switch env["SILL_TEST_CLIPBOARD_ACCESS"] {
            case nil, "":
                break
            case let spec? where ClipboardPolicy.MacAccess(rawValue: spec) != nil:
                let fixed = ClipboardPolicy.MacAccess(rawValue: spec)!
                access = { fixed }
                lines.append("Test clipboard access: macOS's answer for this host's pasteboard is \(spec).")
            case let spec? where spec.hasPrefix("/"):
                access = {
                    let word = (try? String(contentsOfFile: spec, encoding: .utf8))?.trimmingCharacters(in: .whitespacesAndNewlines)
                    return word.flatMap(ClipboardPolicy.MacAccess.init(rawValue:)) ?? .allowed
                }
                lines.append("Test clipboard access: macOS's answer for this host's pasteboard is read from \(spec) at each look.")
            case let spec?:
                lines.append("SILL_TEST_CLIPBOARD_ACCESS=\(spec) ignored: allowed, notAsked, asks, denied or a file's path.")
            }
            board = Board(name: name, launchID: launchID, access: access)
            testBoardName = name
        } else {
            board = Board(name: nil, launchID: launchID, access: { MacClipboard.access() })
            testBoardName = nil
            if let named = env["SILL_TEST_PASTEBOARD"], !named.isEmpty {
                lines.append("SILL_TEST_PASTEBOARD ignored: only a --synthetic host takes it.")
            }
            if let spec = env["SILL_TEST_CLIPBOARD_ACCESS"], !spec.isEmpty {
                lines.append("SILL_TEST_CLIPBOARD_ACCESS ignored: only a --synthetic host takes it.")
            }
        }
        switch (env["SILL_TEST_CLIPBOARD_LOG"], synthetic) {
        case (nil, _), ("", _):
            testLog = false
        case ("1", true):
            testLog = true
            lines.append("Test clipboard log: each change and each device's text is printed with its size, never its text.")
        case ("1", false):
            testLog = false
            lines.append("SILL_TEST_CLIPBOARD_LOG ignored: only a --synthetic host takes it.")
        case (let value?, _):
            testLog = false
            lines.append("SILL_TEST_CLIPBOARD_LOG=\(value) ignored: 1 turns it on.")
        }
        testHookLine = lines.isEmpty ? nil : lines.joined(separator: "\n")
    }

    // MARK: The setting

    /// Turns sharing on or off at once; true when it changed (the caller sends the lists again). Off
    /// drops every subscription without a line: the devices see the list without `clipboard`.
    @discardableResult
    func setEnabled(_ on: Bool) -> Bool {
        guard on != enabled else { return false }
        enabled = on
        if !on {
            subscribers = [:]
            order = []
            arrivals = [:]
            accepts = [:]
            offers.removeAll()
            stopLooking()
        }
        return true
    }

    // MARK: A device's message

    /// A kind 32 from `connection`, named `who` for the lines.
    func received(_ payload: Data, from connection: NWConnection, who: String) {
        guard enabled else { return }
        let id = ObjectIdentifier(connection)
        // What it asks first, then the rate, which a stop never meets (ClipboardPolicy.hostReceives).
        let got = ClipboardPolicy.hostReceives(payload, arrivals: arrivals[id] ?? [], now: ProcessInfo.processInfo.systemUptime)
        arrivals[id] = got.kept
        // A subscription or copy ClipboardPolicy took says the generation it takes; one carrying a later
        // generation's fields (an image, HTML, RTF, files) goes to the pasteboard whole.
        switch got.ask {
        case .subscribe, .set:
            if let g = ClipboardRichPolicy.accepts(after: payload, current: accepts[id], offered: offered) { accepts[id] = g }
            if let item = ClipboardRichPolicy.fromDevice(payload, offered: offered) {
                subscribe(connection, who: who)
                takeRich(item, from: connection, who: who)
                return
            }
        case .stop, .refuse:
            break
        }
        switch got.ask {
        case .subscribe:
            subscribe(connection, who: who)
        case .set(let text):
            subscribe(connection, who: who)
            board.write(text)
            Stats.shared.bump("clip.recv")
            let others = ClipboardPolicy.forward(to: order, from: id).compactMap { subscribers[$0]?.connection }
            if testLog { print("Test clipboard: \(text.utf8.count) bytes from \(who) set on the pasteboard, sent to \(others.count) other device\(others.count == 1 ? "" : "s").") }
            send(payload: ClipboardPolicy.payload(text: text), to: others)
        case .stop:
            accepts[id] = nil
            guard let gone = subscribers.removeValue(forKey: id) else { return }
            order.removeAll { $0 == id }
            print(ClipboardPolicy.Copy.stoppedLine(device: gone.who))
            if subscribers.isEmpty { stopLooking() }
        case .refuse(let why):
            ignored.count(why.rawValue)     // "rate", "tooLong", "undecodable"
        }
    }

    /// The device left: forgotten without a line.
    func clientLeft(_ connection: NWConnection) {
        let id = ObjectIdentifier(connection)
        arrivals[id] = nil
        accepts[id] = nil
        offers.forget(id)
        guard subscribers.removeValue(forKey: id) != nil else { return }
        order.removeAll { $0 == id }
        if subscribers.isEmpty { stopLooking() }
    }

    private func subscribe(_ connection: NWConnection, who: String) {
        let id = ObjectIdentifier(connection)
        guard subscribers[id] == nil else { return }
        if let testBoardName, !saidTestBoard {
            saidTestBoard = true
            print("Test clipboard: a pasteboard of this host's own, \(testBoardName), never this Mac's clipboard.")
        }
        subscribers[id] = (connection, who)
        order.append(id)
        print(ClipboardPolicy.Copy.sharesLine(device: who))
        startLooking()
    }

    // MARK: The pasteboard

    private func startLooking() {
        guard look == nil else { return }
        board.takeAsSeen()             // what is on the clipboard now is never sent
        look = Task { @MainActor [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(ClipboardPolicy.lookInterval))
                guard !Task.isCancelled, let board = self?.board else { return }
                // On the board's queue; the main actor waits for nothing meanwhile.
                let looked = await board.look(generation: self?.highestAccepted ?? ClipboardGeneration.text)
                guard !Task.isCancelled, let self else { return }
                self.lookedAt(looked)
            }
        }
    }

    private func stopLooking() {
        look?.cancel()
        look = nil
    }

    /// A look: macOS's answer's line when it changed (ClipboardPolicy.accessLine), and a change the
    /// board looked at to every subscriber, when ClipboardPolicy took it.
    private func lookedAt(_ looked: Board.Look) {
        if let line = ClipboardPolicy.accessLine(was: reportedAccess, now: looked.access) { print(line) }
        reportedAccess = looked.access
        guard let (count, decision) = looked.change else { return }
        if looked.rich != nil, highestAccepted >= ClipboardGeneration.rich {
            sendRich(count: count, decision: decision, read: looked.rich)
            return
        }
        switch decision {
        case .send(let payload):
            let to = order.compactMap { subscribers[$0]?.connection }
            if testLog { print("Test clipboard: change \(count) sent to \(to.count) device\(to.count == 1 ? "" : "s") (\(payload.count) bytes of JSON).") }
            send(payload: payload, to: to)
        case .skip(let why):
            if testLog { print("Test clipboard: change \(count) not sent (\(why.rawValue)).") }
        }
    }

    /// The pasteboard, read and written on `queue` alone, in order: a write a device asked for is
    /// done before any later look, so the look always finds it as this host's own (its change count
    /// taken as seen, and its marker naming this launch besides).
    private final class Board: @unchecked Sendable {
        private let queue = DispatchQueue(label: "sill.clipboard", qos: .userInitiated)
        private let board: NSPasteboard
        private let launchID: String
        /// macOS's answer for this pasteboard (MacClipboard.access, or a synthetic host's own).
        private let access: @Sendable () -> ClipboardPolicy.MacAccess
        // On `queue`: the change count the looks have dealt with, the one after this host's own last
        // write, the first look at a change whose pasteboard was still empty (ClipboardPolicy.hostWaits),
        // and macOS's answer at the last look (or when the count was last taken as seen).
        private var seen = 0
        private var ownChangeCount: Int?
        private var firstLook: (count: Int, at: Double)?
        private var lastAccess: ClipboardPolicy.MacAccess?

        /// What a look found: macOS's answer, and a change of the pasteboard with ClipboardPolicy's
        /// decision (nil when the count did not move, the pasteboard was still empty, or nothing was read).
        struct Look {
            let access: ClipboardPolicy.MacAccess
            let change: (count: Int, decision: ClipboardPolicy.HostDecision)?
            /// Generations 2 and 3: the item's richer types, read when a subscriber takes them and the
            /// change is not this host's own or Universal Clipboard's (RichPasteboard).
            var rich: RichPasteboard.Read? = nil
        }

        /// `name`: a pasteboard of a synthetic host's own; nil, the Mac's.
        init(name: String?, launchID: String, access: @escaping @Sendable () -> ClipboardPolicy.MacAccess) {
            board = name.map { NSPasteboard(name: NSPasteboard.Name($0)) } ?? .general
            self.launchID = launchID
            self.access = access
        }

        /// The change count as it stands is seen: what is on the clipboard now is never sent. While
        /// macOS does not allow every read, nothing is read: the first look it allows takes it then.
        func takeAsSeen() {
            queue.async {
                let now = self.access()
                self.lastAccess = now
                if ClipboardPolicy.hostLooks(now) { self.seen = self.board.changeCount }
            }
        }

        /// One look: macOS's answer, then, while it allows every read and the change count moved,
        /// ClipboardPolicy's decision from the items' types, the marker and the text read only when it
        /// needs them.
        func look(generation: Int = ClipboardGeneration.text) async -> Look {
            await withCheckedContinuation { done in
                queue.async { done.resume(returning: self.lookNow(generation: generation)) }
            }
        }

        private func lookNow(generation: Int) -> Look {
            let access = self.access()
            let step = ClipboardPolicy.accessStep(was: lastAccess, now: access)
            lastAccess = access
            switch step {
            case .wait:
                return Look(access: access, change: nil)
            case .takeAsSeen:
                // Allowed again: what was copied while macOS asked or denied is never sent.
                seen = board.changeCount
                firstLook = nil
                return Look(access: access, change: nil)
            case .look:
                break
            }
            let count = board.changeCount
            guard count != seen else { return Look(access: access, change: nil) }
            let types = (board.pasteboardItems ?? []).flatMap { $0.types.map(\.rawValue) }
            // Cleared and not yet written: look again (the write does not move the count).
            let now = ProcessInfo.processInfo.systemUptime
            if firstLook?.count != count { firstLook = (count, now) }
            if ClipboardPolicy.hostWaits(types: types, firstLook: firstLook?.at ?? now, now: now) {
                return Look(access: access, change: nil)
            }
            seen = count
            let own = NSPasteboard.PasteboardType(ClipboardPolicy.ownType)
            var text: String?, textRead = false
            let decision = ClipboardPolicy.hostDecides(types: types, changeCount: count, ownChangeCount: ownChangeCount,
                                                       launchID: launchID,
                                                       ownMarker: { self.board.string(forType: own) },
                                                       text: { textRead = true; text = self.board.string(forType: .string); return text })
            // Generation 1's text was read only past this host's own write and Universal Clipboard's: the
            // richer types are read on the same terms, and only for a subscriber that takes them.
            var look = Look(access: access, change: (count, decision))
            if generation >= ClipboardGeneration.rich, textRead {
                look.rich = RichPasteboard.read(board, types: types, generation: generation, text: text)
            }
            return look
        }

        /// A device's text, for this Mac only, marked as this launch's, its change count kept. Written
        /// whatever macOS says of reads: writing needs no leave. Synchronous (the review of #242): the
        /// main actor, which handles each device's messages in order, returns from `received` only once
        /// the pasteboard holds the text, so the ⌘V a device sends after it (paste right away, 50 ms
        /// after the receipt the network queue echoed at once) always pastes it. Written asynchronously,
        /// as before, a main actor busy as the text arrived (a host's first stream start held it 70 to
        /// 150 ms) took the text and the keys back to back: V's key came within half a millisecond of the
        /// write's end and before it in 4 of 25 pastes on loopback, where with the main actor free it
        /// came 50 to 66 ms after. The cost: the write (about a millisecond) and a look the queue is in
        /// the middle of. Nothing on this queue ever waits for the main actor, so it cannot deadlock.
        func write(_ text: String) {
            queue.sync {
                let prepared = self.board.prepareForNewContents(with: .currentHostOnly)
                let item = NSPasteboardItem()
                item.setString(text, forType: .string)
                item.setString(self.launchID, forType: NSPasteboard.PasteboardType(ClipboardPolicy.ownType))
                self.board.writeObjects([item])
                // The count after it: read back while macOS allows reads, as the looks do; else the one
                // the write began with, which the write itself does not move.
                let count = self.lastAccess.map(ClipboardPolicy.hostLooks) == false ? prepared : self.board.changeCount
                self.ownChangeCount = count
                self.seen = count
            }
        }

        /// A device's copy of generation 2 (its text, HTML, RTF and image, ClipboardRichPolicy.DeviceItem),
        /// as one item, for this Mac only and marked as this launch's, as `write`; synchronous for the same
        /// reason (a ⌘V right after it must find it).
        func write(rich item: ClipboardRichPolicy.DeviceItem) {
            queue.sync {
                let prepared = self.board.prepareForNewContents(with: .currentHostOnly)
                let entry = NSPasteboardItem()
                if let text = item.text { entry.setString(text, forType: .string) }
                if let html = item.html { entry.setString(html, forType: .html) }
                if let rtf = item.rtf { entry.setData(rtf, forType: .rtf) }
                if let image = item.image, let type = item.imageType { entry.setData(image, forType: NSPasteboard.PasteboardType(type)) }
                entry.setString(self.launchID, forType: NSPasteboard.PasteboardType(ClipboardPolicy.ownType))
                self.board.writeObjects([entry])
                self.wrote(prepared)
            }
        }

        /// A device's copied files, kept on this Mac (ClipStage), as file URLs: one item a file, its name as
        /// its text (as Finder's copy), each marked as this launch's; for this Mac only.
        func write(files urls: [URL]) {
            queue.sync {
                let prepared = self.board.prepareForNewContents(with: .currentHostOnly)
                let items = urls.map { url -> NSPasteboardItem in
                    let entry = NSPasteboardItem()
                    entry.setString(url.absoluteString, forType: .fileURL)
                    entry.setString(url.lastPathComponent, forType: .string)
                    entry.setString(self.launchID, forType: NSPasteboard.PasteboardType(ClipboardPolicy.ownType))
                    return entry
                }
                self.board.writeObjects(items)
                self.wrote(prepared)
            }
        }

        /// After this host's own write: its change count kept and taken as seen, as `write` does. On `queue`.
        private func wrote(_ prepared: Int) {
            let count = lastAccess.map(ClipboardPolicy.hostLooks) == false ? prepared : board.changeCount
            ownChangeCount = count
            seen = count
        }
    }

    // MARK: Generations 2 and 3

    /// The highest generation any subscriber takes.
    private var highestAccepted: Int {
        order.compactMap { subscribers[$0] == nil ? nil : (accepts[$0] ?? ClipboardGeneration.text) }.max() ?? ClipboardGeneration.text
    }

    /// A change read with its richer types: each subscriber its generation's kind 32
    /// (ClipboardRichPolicy.sends; generation 1's exactly as before), the copied files offered to those of
    /// generation 3 (ClipboardOffers), and one line when a part was left out for the cap.
    private func sendRich(count: Int, decision: ClipboardPolicy.HostDecision, read: RichPasteboard.Read?) {
        let textPayload: Data?
        switch decision {
        case .send(let payload): textPayload = payload
        case .skip(.own), .skip(.remote):
            if testLog { print("Test clipboard: change \(count) not sent (\(decision)).") }
            return
        case .skip: textPayload = nil
        }
        var item = read?.item
        let ids = order.filter { subscribers[$0] != nil }
        if let urls = read?.fileURLs, !urls.isEmpty {
            let takers = Set(ids.filter { (accepts[$0] ?? ClipboardGeneration.text) >= ClipboardGeneration.files })
            if !takers.isEmpty { item?.files = offers.make(urls, to: takers) }
        }
        let sends = ClipboardRichPolicy.sends(to: ids.map { ($0, accepts[$0] ?? ClipboardGeneration.text) }, textPayload: textPayload, item: item)
        if let item {
            let left = ClipboardRichPolicy.payload(item, generation: highestAccepted).left
            if !left.isEmpty { print("Clipboard: a copy went without \(left.map(\.rawValue).joined(separator: ", ")).") }
        }
        if testLog { print("Test clipboard: change \(count) sent to \(sends.count) device\(sends.count == 1 ? "" : "s") by generation.") }
        server.send(each: sends.compactMap { id, payload in
            subscribers[id].map { ($0.connection, StreamMessage(kind: .clipboard, timestamp: Date().timeIntervalSince1970, isKeyframe: false, payload: payload)) }
        })
        for _ in sends { Stats.shared.bump("clip.sent") }
    }

    /// A device's copy of a later generation: written to the pasteboard (generation 2's at once, its
    /// files once their parts are here and kept, ClipStage) and handed to the other subscribers, each its
    /// generation's part of it.
    private func takeRich(_ item: ClipboardRichPolicy.DeviceItem, from connection: NWConnection, who: String) {
        guard !item.transfers.isEmpty else {
            board.write(rich: item)
            Stats.shared.bump("clip.recv")
            let id = ObjectIdentifier(connection)
            let others = ClipboardPolicy.forward(to: order, from: id).filter { subscribers[$0] != nil }
            let png = item.imageType == ClipboardRichPolicy.pngType ? item.image : nil
            let mac = ClipboardRichPolicy.MacItem(text: item.text, html: item.html, rtf: item.rtf, png: png)
            let sends = ClipboardRichPolicy.sends(to: others.map { ($0, accepts[$0] ?? ClipboardGeneration.text) },
                                                  textPayload: item.text.flatMap(ClipboardPolicy.payload(text:)), item: mac)
            if testLog { print("Test clipboard: a copy of generation 2 from \(who) set on the pasteboard, sent to \(sends.count) other device\(sends.count == 1 ? "" : "s").") }
            server.send(each: sends.compactMap { id, payload in
                subscribers[id].map { ($0.connection, StreamMessage(kind: .clipboard, timestamp: Date().timeIntervalSince1970, isKeyframe: false, payload: payload)) }
            })
            for _ in sends { Stats.shared.bump("clip.sent") }
            return
        }
        guard stage != nil, let whenWhole else { return }
        whenWhole(item.transfers, connection) { [weak self] in self?.filesArrived(item, from: connection, who: who) }
    }

    /// A device's copied files are here (or as many as came): kept (ClipStage), then on the pasteboard as
    /// file URLs, and offered to the other subscribers of generation 3.
    private func filesArrived(_ item: ClipboardRichPolicy.DeviceItem, from connection: NWConnection, who: String) {
        guard enabled, let stage, let taken = takeFiles?(item.transfers, connection) else { return }
        let files = item.transfers.compactMap { id in taken[id].map { (id, $0) } }
        guard !files.isEmpty else { print("Clipboard: none of the files \(who) copied arrived whole; this Mac's clipboard is unchanged."); return }
        let board = self.board
        stage.keep(files, folders: item.folders, names: item.names) { [weak self] urls in
            guard !urls.isEmpty else { return }
            Task { @MainActor [weak self] in
                guard let self, self.enabled else { return }
                board.write(files: urls)
                Stats.shared.bump("clip.recv")
                print("Clipboard: \(urls.count) file\(urls.count == 1 ? "" : "s") \(who) copied \(urls.count == 1 ? "is" : "are") on this Mac's clipboard.")
                let id = ObjectIdentifier(connection)
                let takers = Set(ClipboardPolicy.forward(to: self.order, from: id).filter {
                    self.subscribers[$0] != nil && (self.accepts[$0] ?? ClipboardGeneration.text) >= ClipboardGeneration.files })
                guard !takers.isEmpty else { return }
                let offer = self.offers.make(urls, to: takers)
                let payload = Wire.encode(Clipboard(text: urls.map(\.lastPathComponent).joined(separator: "\n"), files: offer))
                self.send(payload: payload, to: takers.compactMap { self.subscribers[$0]?.connection })
            }
        }
    }

    /// A kind 37 for one of the clipboard's offers: true when it is one (served, or refused for a device it
    /// was not offered to or an index it does not have), false for a drag offer's (DragOut's). The file goes
    /// in paced parts on the device's bulk connection when it has one (DataLinks.sendFile); a clipboard file
    /// is never moved to the Trash.
    func answers(_ r: FileRequest, from connection: NWConnection) -> Bool {
        guard offers.knows(r.offer) else { return false }
        guard r.trash != true, let url = offers.file(r, from: ObjectIdentifier(connection)) else { return true }
        server.dataLinks.sendFile(url, transfer: r.transfer, name: url.lastPathComponent, to: connection)
        return true
    }

    private func send(payload: Data?, to connections: [NWConnection]) {
        guard let payload, !connections.isEmpty else { return }
        let message = StreamMessage(kind: .clipboard, timestamp: Date().timeIntervalSince1970, isKeyframe: false, payload: payload)
        server.send(each: connections.map { ($0, message) })
        for _ in connections { Stats.shared.bump("clip.sent") }
    }
}

/// macOS's paste privacy for this Mac's clipboard (ClipboardPolicy.MacAccess; macOS 15.4 and later),
/// for HostClipboard's looks and Sill.app's Settings › General. `access` reads NSPasteboard.general's
/// `accessBehavior` alone, which never brings macOS's alert and reads nothing of the clipboard.
package enum MacClipboard {
    package static func access() -> ClipboardPolicy.MacAccess {
        if #available(macOS 15.4, *) {
            return ClipboardPolicy.macAccess(raw: NSPasteboard.general.accessBehavior.rawValue)
        }
        return ClipboardPolicy.macAccess(raw: nil)
    }

    /// Settings › General's Allow in System Settings while macOS has not asked about Sill yet
    /// (`notAsked`): one read of the clipboard, made because the person clicked, so that macOS asks
    /// about Sill and lists it under Paste from Other Apps. What it read is dropped at once and never
    /// leaves this process. Off the main thread, which macOS's alert would hold until it is answered;
    /// `then` runs on the main queue afterwards. Never from a look.
    package static func askOnce(then done: @escaping @Sendable () -> Void) {
        DispatchQueue.global(qos: .userInitiated).async {
            _ = NSPasteboard.general.string(forType: .string)
            DispatchQueue.main.async(execute: done)
        }
    }
}
