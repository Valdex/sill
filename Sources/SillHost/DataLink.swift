import Foundation
import Network
import Security
import StreamProtocol

/// Coherence's bulk connection on the host (`sill-data/1`, kinds 48 and 49; docs/coherence-protocol.md,
/// "The bulk connection"; the rules are DataLinkPolicy's). File parts (kinds 34 and 37) are large, and on
/// the session they queue in front of the picture, input and pings; a device whose hello says
/// `coherence` 1 gets a ticket (kind 48, once, after its window list) and opens a second connection to
/// the same door with it.
///
/// - Tickets: 32 random bytes as base64, one per session, kept while the session lives (a device dials
///   again with it when its bulk connection goes), forgotten when it ends.
/// - Admission: a TLS door's `sill-data/1` connection (Door, at its kind 49) or the plain door's
///   connection whose first message is kind 49 (StreamServer) is admitted only for the session holding
///   its ticket, through the same door the same way, with the same key and from the same address, while
///   the Mac still serves that session and it has no bulk connection; never registered as a session (no
///   catalog, no frames, no device row, no "Client connected"). One line each way.
/// - Lifetime: closed with its session (`sessionEnded`), and at once when the Mac stops serving the
///   session's device (`revoke`: Remove, Require pairing, Remote Access or internet access off).
/// - On it: kinds 34 and 37 are read (up to `DataHello.maxPayload`) and handed on exactly as if they had
///   come on the session (`StreamServer.onMessage` with the session's connection), so FileDrops, LiveDrags
///   and the clipboard key them by the session as before; any other kind is read and dropped.
/// - Sending (`sendFile`, for a kind 37 of a drag offer or a clipboard offer): a file in parts on the bulk
///   connection when it is up, else on the session, paced by DataPacing so the session's picture comes
///   first; a folder zipped first (FolderZip). Parts are read off the network queue. A transfer whose
///   bulk connection goes mid-way starts again from its first byte on the session (the device puts the
///   parts of one transfer together by offset from either connection, so nothing is written twice).
///
/// Threading: StreamServer's network queue for everything (`queue`); `sendFile` may be called from any
/// thread. Files are read on a queue of their own.
final class DataLinks {
    private let queue: DispatchQueue
    /// Set by StreamServer right after it makes this.
    weak var server: StreamServer?
    private let reads = DispatchQueue(label: "sill.data.read", qos: .utility)

    // On `queue`.
    private var tickets: [String: DataLinkPolicy.Ticket<ObjectIdentifier>] = [:]
    private var tokens: [ObjectIdentifier: String] = [:]
    private var sessions: [ObjectIdentifier: NWConnection] = [:]
    private var links: [ObjectIdentifier: Link] = [:]
    private var revoked: Set<ObjectIdentifier> = []
    private var senders: [ObjectIdentifier: Sender] = [:]

    private final class Link {
        let connection: NWConnection
        let session: ObjectIdentifier
        let sessionConnection: NWConnection
        /// Bytes handed to the connection and not taken yet.
        var pending = 0
        init(connection: NWConnection, session: ObjectIdentifier, sessionConnection: NWConnection) {
            self.connection = connection; self.session = session; self.sessionConnection = sessionConnection
        }
    }

    /// One file on its way to a device.
    private final class Outgoing {
        let transfer: UInt32
        let name: String
        /// What is read: the file, or a folder's zip.
        let source: URL
        /// A folder's zip, removed once sent or given up.
        let removeAfter: Bool
        let total: UInt64
        var handle: FileHandle?
        /// The next byte to send.
        var offset: UInt64 = 0
        /// A part read and not sent yet, and the offset it was read at.
        var part: Data?
        var partAt: UInt64 = 0
        var reading = false
        /// A part of it went on the bulk connection (begun again if that connection goes).
        var usedLink = false
        /// Bumped when it starts again, so a read made for the old start is dropped.
        var generation = 0
        init(transfer: UInt32, name: String, source: URL, removeAfter: Bool, total: UInt64, handle: FileHandle) {
            self.transfer = transfer; self.name = name; self.source = source; self.removeAfter = removeAfter
            self.total = total; self.handle = handle
        }
    }

    /// A session's files, sent one after another, and its pacing.
    private final class Sender {
        var files: [Outgoing] = []
        var pacing = DataPacing()
        var timer: DispatchWorkItem?
    }

    init(queue: DispatchQueue) { self.queue = queue }

    /// The pacing's clock: seconds of uptime, which a change of the Mac's clock never moves.
    private static func clock() -> Double { ProcessInfo.processInfo.systemUptime }

    /// A connection's address as admission compares it: without the port, with a link-local address's
    /// interface ("10.0.0.5", "fe80::1%en0").
    static func address(of c: NWConnection) -> String {
        Door.source(of: c)?.display ?? "\(c.endpoint)"
    }

    // MARK: Tickets

    /// A ticket for `session`, admitted by `route`, made now and kept while it lives: its token, or nil
    /// when the Mac's random source fails (no ticket then; the device's parts go on the session). On `queue`.
    func issue(for session: NWConnection, route: ClientRoute) -> String? {
        let id = ObjectIdentifier(session)
        if let known = tokens[id] { return known }
        var bytes = [UInt8](repeating: 0, count: DataTicket.tokenBytes)
        guard SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes) == errSecSuccess, let token = DataLinkPolicy.token(bytes) else {
            print("Bulk connection: no ticket for \(session.endpoint) (the random source failed); its files go on the session.")
            return nil
        }
        let peer = DataLinkPolicy.Peer(remote: route.isRemote, encrypted: route.encrypted, fingerprint: route.fingerprint,
                                       address: Self.address(of: session))
        tickets[token] = DataLinkPolicy.Ticket(session: id, peer: peer)
        tokens[id] = token
        sessions[id] = session
        return token
    }

    /// Whether the plain door should read `c`'s first message before registering it: a plain session at
    /// its address awaits its bulk connection (DataLinkPolicy.awaitsPlain). On `queue`.
    func awaitsPlain(_ c: NWConnection) -> Bool {
        guard !tickets.isEmpty else { return false }
        return DataLinkPolicy.awaitsPlain(address: Self.address(of: c), tickets: tickets, linked: Set(links.keys))
    }

    // MARK: Admission

    /// A bulk connection's kind 49 (`hello`, its payload), from a TLS door (`fingerprint` the key it
    /// proved, `encrypted`) or the plain door: admitted for the session holding its ticket, or refused with
    /// one line (the caller closes a refused one and counts it at its door). True when admitted: the
    /// connection is this one's from then on. On `queue`.
    func admit(_ c: NWConnection, hello: Data, fingerprint: Data?, remote: Bool, encrypted: Bool) -> Bool {
        let token = Wire.decode(DataHello.self, from: hello)?.token
        let presented = DataLinkPolicy.Peer(remote: remote, encrypted: encrypted, fingerprint: fingerprint, address: Self.address(of: c))
        switch DataLinkPolicy.admit(token: token, tickets: tickets, linked: Set(links.keys), revoked: revoked, presented: presented) {
        case .refuse(let why):
            print("Bulk connection from \(c.endpoint) refused: \(why.rawValue).")
            Stats.shared.bump("data.refused")
            return false
        case .admit(let id):
            guard let session = sessions[id], server?.dataLoad(id) != nil else {
                print("Bulk connection from \(c.endpoint) refused: its session has ended.")
                return false
            }
            let link = Link(connection: c, session: id, sessionConnection: session)
            links[id] = link
            c.stateUpdateHandler = { [weak self, weak link] state in
                guard let self, let link else { return }
                switch state {
                case .failed:
                    c.cancel()
                    self.linkEnded(link)
                case .cancelled:
                    self.linkEnded(link)
                default:
                    break
                }
            }
            print("Bulk connection for \(session.endpoint) admitted (\(c.endpoint)\(encrypted ? ", \(DataLinkPolicy.alpn)" : ", plain")).")
            readLoop(link)
            if senders[id] != nil { pump(id) }      // what is on its way moves onto it
            return true
        }
    }

    /// The session left: its ticket forgotten, its bulk connection closed, its files given up. On `queue`.
    func sessionEnded(_ session: NWConnection) {
        let id = ObjectIdentifier(session)
        if let token = tokens.removeValue(forKey: id) { tickets[token] = nil }
        sessions[id] = nil
        revoked.remove(id)
        links[id]?.connection.cancel()
        stop(id)
    }

    /// The Mac no longer serves the session (`closeSessions`): its bulk connection closed at once and
    /// none admitted for it again, its files given up. On `queue`.
    func revoke(_ session: NWConnection) {
        let id = ObjectIdentifier(session)
        guard tokens[id] != nil || links[id] != nil || senders[id] != nil else { return }
        revoked.insert(id)
        links[id]?.connection.cancel()
        stop(id)
    }

    private func linkEnded(_ link: Link) {
        guard links[link.session] === link else { return }
        links[link.session] = nil
        if sessions[link.session] != nil, !revoked.contains(link.session) {
            print("Bulk connection for \(link.sessionConnection.endpoint) closed; files go on the session until the device dials it again.")
        }
        // What went on it may be lost with it: those files start again from their first byte.
        if let s = senders[link.session] {
            for f in s.files where f.usedLink {
                f.usedLink = false
                f.offset = 0
                f.part = nil
                f.generation += 1
            }
            pump(link.session)
        }
    }

    // MARK: Reading

    private func readLoop(_ link: Link) {
        let c = link.connection
        c.receive(minimumIncompleteLength: StreamMessage.headerLength, maximumLength: StreamMessage.headerLength) { [weak self] data, _, isComplete, error in
            guard let self else { return }
            guard let data, let header = StreamMessage.parseHeader(data) else {
                if isComplete || error != nil { c.cancel() }
                return
            }
            let read = DataLinkPolicy.onLink(kind: header.kind, length: header.payloadLength)
            if read == .close {
                print("Closing the bulk connection \(c.endpoint): it announced a \(header.payloadLength)-byte message (the limit is \(DataLinkPolicy.maxPayload)).")
                c.cancel()
                return
            }
            let take = { (payload: Data) in
                if read == .deliver, self.links[link.session] === link, !self.revoked.contains(link.session) {
                    Stats.shared.bump("data.in")
                    self.server?.onMessage?(StreamMessage(kind: header.kind, timestamp: header.timestamp, isKeyframe: false, payload: payload),
                                            link.sessionConnection)
                }
                self.readLoop(link)
            }
            if header.payloadLength == 0 { take(Data()); return }
            c.receive(minimumIncompleteLength: header.payloadLength, maximumLength: header.payloadLength) { data, _, isComplete, error in
                guard let data, data.count == header.payloadLength else {
                    if isComplete || error != nil { c.cancel() }
                    return
                }
                take(data)
            }
        }
    }

    // MARK: Sending

    /// `url` (a file, or a folder sent zipped) to `session`'s device as kind 34 parts under `transfer`,
    /// named `name`, after whatever else is on its way to it. Thread-safe; the zip and the reads happen off
    /// the network queue.
    func sendFile(_ url: URL, transfer: UInt32, name: String, to session: NWConnection) {
        let id = ObjectIdentifier(session)
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let folder = (try? url.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true
            let source = folder ? FolderZip.zip(url) : url
            guard let source, let handle = try? FileHandle(forReadingFrom: source) else {
                print("Files: could not read \(name) to send it.")
                return
            }
            let total = UInt64((try? source.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0)
            let file = Outgoing(transfer: transfer, name: name, source: source, removeAfter: folder, total: total, handle: handle)
            guard let self else { DataLinks.close(file); return }
            self.queue.async { self.enqueue(file, for: id) }
        }
    }

    private func enqueue(_ file: Outgoing, for id: ObjectIdentifier) {
        guard server?.dataLoad(id) != nil, !revoked.contains(id) else { Self.close(file); return }
        let s = senders[id] ?? Sender()
        senders[id] = s
        s.files.append(file)
        print("Files: sending \(file.name) (\(file.total) bytes) as transfer \(file.transfer) on \(links[id] != nil ? "the bulk connection" : "the session").")
        pump(id)
    }

    /// Sends what the pacing lets go now, and asks again when it says. On `queue`.
    private func pump(_ id: ObjectIdentifier) {
        guard let s = senders[id] else { return }
        s.timer?.cancel()
        s.timer = nil
        guard let server, let session = server.dataLoad(id) else { stop(id); return }
        while let file = s.files.first {
            let link = links[id]
            let load = DataPacing.Load(onLink: link != nil, remote: session.remote, withholding: session.withholding,
                                       sessionPending: session.pending, linkPending: link?.pending ?? 0)
            guard let part = file.part else {
                read(file, for: id, bytes: DataPacing.partBytes(onLink: load.onLink, remote: load.remote))
                return
            }
            let now = Self.clock()
            switch s.pacing.decide(bytes: part.count, load: load, now: now) {
            case .wait(let seconds):
                let item = DispatchWorkItem { [weak self] in self?.pump(id) }
                s.timer = item
                queue.asyncAfter(deadline: .now() + seconds, execute: item)
                return
            case .send:
                let message = StreamMessage(kind: .fileChunk, timestamp: Date().timeIntervalSince1970, isKeyframe: false,
                                            payload: FileChunk.pack(id: file.transfer, name: file.name, total: file.total,
                                                                    offset: file.partAt, bytes: part)).serialized()
                if let link {
                    send(message, on: link, for: id)
                    file.usedLink = true
                } else if !server.sendPart(message, toSession: id) {
                    stop(id)
                    return
                }
                s.pacing.sent(bytes: part.count, now: now)
                Stats.shared.bump("data.out")
                file.part = nil
                file.offset = file.partAt + UInt64(part.count)
                if file.offset >= file.total {
                    Self.close(file)
                    s.files.removeFirst()
                    continue
                }
                read(file, for: id, bytes: DataPacing.partBytes(onLink: link != nil, remote: session.remote))
                return
            }
        }
        senders[id] = nil
    }

    private func send(_ data: Data, on link: Link, for id: ObjectIdentifier) {
        link.pending += data.count
        link.connection.send(content: data, completion: .contentProcessed { [weak self, weak link] _ in
            guard let self, let link else { return }
            link.pending -= data.count
            // Room on the connection: a part waiting for it may go now.
            if self.senders[id]?.timer != nil { self.pump(id) }
        })
    }

    /// The next part of `file` from its offset, on the read queue; the pump runs again with it.
    private func read(_ file: Outgoing, for id: ObjectIdentifier, bytes: Int) {
        guard !file.reading, let handle = file.handle else { return }
        file.reading = true
        let at = file.offset, generation = file.generation
        let want = Int(min(UInt64(bytes), file.total - min(at, file.total)))
        reads.async { [weak self] in
            let data: Data? = {
                do {
                    try handle.seek(toOffset: at)
                    return want == 0 ? Data() : (try handle.read(upToCount: want) ?? Data())
                } catch {
                    return nil
                }
            }()
            guard let self else { return }
            self.queue.async {
                file.reading = false
                guard file.generation == generation, file.handle != nil else { self.pump(id); return }
                guard let data, data.count == want else {
                    print("Files: \(file.name) could not be read whole (it changed while it was sent); transfer \(file.transfer) given up.")
                    self.drop(file, for: id)
                    return
                }
                file.part = data
                file.partAt = at
                self.pump(id)
            }
        }
    }

    private func drop(_ file: Outgoing, for id: ObjectIdentifier) {
        Self.close(file)
        senders[id]?.files.removeAll { $0 === file }
        pump(id)
    }

    /// Every file on its way to `id`'s device given up.
    private func stop(_ id: ObjectIdentifier) {
        guard let s = senders.removeValue(forKey: id) else { return }
        s.timer?.cancel()
        for f in s.files { Self.close(f) }
    }

    private static func close(_ file: Outgoing) {
        try? file.handle?.close()
        file.handle = nil
        if file.removeAfter { try? FileManager.default.removeItem(at: file.source) }
    }
}
