import Foundation
import StreamProtocol

/// Coherence's bulk connection, the host's rules (`sill-data/1`, kinds 48 and 49; docs/coherence-protocol.md,
/// "The bulk connection"): the ticket a session gets, who a second connection presenting it is admitted
/// for, what is read on it, how the host paces the file parts it sends so the session's picture comes
/// first, and how long a message that names transfers waits for their parts. DataLink.swift runs them on
/// the network queue; Door.swift and StreamServer.swift hand it the connections.
///
/// Pure (Foundation and StreamProtocol's wire values), so `Tests/checks/data-link` runs it on Linux too.
/// Nothing here reads a clock or the network: `now` is passed in.
enum DataLinkPolicy {
    /// `WindowList.data`: kind 48 and the `sill-data/1` connection.
    static let generation = 1
    /// The ALPN (RemoteTLS.dataALPN).
    static let alpn = DataHello.alpn
    /// The most a kind 49's payload may be: `{"token":"<44 characters of base64>"}` is 56 bytes.
    static let maxHelloPayload = 512
    /// The cap on any message on the connection, either way (a 512 KB part and its header with room).
    static let maxPayload = DataHello.maxPayload
    /// Seconds from `.ready` (TLS) or `.ready` (plain) to kind 49. The TLS doors' own deadline for it is
    /// DoorPolicy.requestDeadline, the same 4 s, and never later than 10 s after accept.
    static let deadline = DataHello.deadline

    // MARK: The ticket

    /// The token of `bytes`, `DataTicket.tokenBytes` random bytes from the Mac's random source; nil for
    /// any other count.
    static func token(_ bytes: [UInt8]) -> String? {
        bytes.count == DataTicket.tokenBytes ? Data(bytes).base64EncodedString() : nil
    }

    /// A token as a ticket the host made reads: base64 of exactly `DataTicket.tokenBytes` bytes (44
    /// characters; the length is looked at first only so a long string is never decoded).
    static func tokenValid(_ token: String) -> Bool {
        guard token.utf8.count <= 64, let d = Data(base64Encoded: token) else { return false }
        return d.count == DataTicket.tokenBytes
    }

    /// A session gets a ticket once: when the host offers `data` (`offered`), its hello said `coherence` 1
    /// or later, its window list has gone (`listSent`), it has none yet, and the Mac still serves it.
    static func sendsTicket(offered: Bool, coherence: Int?, listSent: Bool, sent: Bool, revoked: Bool) -> Bool {
        offered && (coherence ?? 0) >= CoherenceGeneration.current && listSent && !sent && !revoked
    }

    // MARK: Admission

    /// Who a connection is, as admission compares a session with its bulk connection.
    struct Peer: Equatable, Sendable {
        /// Admitted at the remote door (RemoteServer's), not the home door.
        var remote: Bool
        /// TLS: every remote session, and every session at a TLS home door; false at the plain home door.
        var encrypted: Bool
        /// The key it proved in its TLS handshake (SPKI fingerprint); nil at the plain door.
        var fingerprint: Data?
        /// Its address without the port, with a link-local IPv6 address's interface ("10.0.0.5",
        /// "fe80::1%en0").
        var address: String
    }

    /// A ticket a session holds: the session, and who it is.
    struct Ticket<ID: Hashable>: Equatable {
        var session: ID
        var peer: Peer
    }

    enum Refusal: String, Equatable, Sendable {
        case notTicket = "not a ticket"
        case unknown = "no session holds that ticket"
        case door = "not at its session's door"
        case key = "another key than its session's"
        case address = "another address than its session's"
        case busy = "its session has one already"
        case revoked = "the Mac no longer serves its session"
    }

    enum Verdict<ID: Hashable>: Equatable {
        case admit(ID)
        case refuse(Refusal)
    }

    /// A bulk connection presenting `token` (nil: its kind 49 did not decode), as `presented`: admitted
    /// for the session holding the token, when it came to the same door the same way (TLS or plain), with
    /// the same key (a TLS session's: never a missing one), from the same address, while the Mac still
    /// serves the session (`revoked` holds the sessions it has cut off) and the session has no bulk
    /// connection yet (`linked`). Any one of these failing refuses it, the first that fails named.
    static func admit<ID: Hashable>(token: String?, tickets: [String: Ticket<ID>], linked: Set<ID>, revoked: Set<ID>,
                                    presented: Peer) -> Verdict<ID> {
        guard let token, tokenValid(token) else { return .refuse(.notTicket) }
        guard let ticket = tickets[token] else { return .refuse(.unknown) }
        let held = ticket.peer
        guard held.remote == presented.remote, held.encrypted == presented.encrypted else { return .refuse(.door) }
        if held.encrypted {
            guard let key = held.fingerprint, let shown = presented.fingerprint, key == shown else { return .refuse(.key) }
        } else {
            guard presented.fingerprint == nil else { return .refuse(.door) }
        }
        guard held.address == presented.address else { return .refuse(.address) }
        guard !revoked.contains(ticket.session) else { return .refuse(.revoked) }
        guard !linked.contains(ticket.session) else { return .refuse(.busy) }
        return .admit(ticket.session)
    }

    /// The plain home door reads a connection's first message before it registers it only while a plain
    /// session from the same address holds a ticket and has no bulk connection: that connection may be
    /// the bulk one, whose first message is kind 49 rather than a hello. Every other connection is
    /// registered as before, its first message unread.
    static func awaitsPlain<ID: Hashable>(address: String, tickets: [String: Ticket<ID>], linked: Set<ID>) -> Bool {
        tickets.values.contains { !$0.peer.encrypted && !$0.peer.remote && $0.peer.address == address && !linked.contains($0.session) }
    }

    /// What the plain door does with such a connection's first message, from its header.
    enum PlainFirst: Equatable, Sendable {
        /// Kind 49 within `maxHelloPayload`: the bulk connection's hello, judged by `admit`.
        case data
        /// A hello: the session's, judged by the device gate as ever.
        case hello
        /// Anything else a session may send first (an older device's viewport): the session, that message
        /// handed on as if the receive loop had read it.
        case other
        /// Longer than any device's message (or a kind 49 longer than a ticket): closed.
        case close
    }

    static func plainFirst(kind: StreamMessageKind, length: Int) -> PlainFirst {
        switch kind {
        case .dataHello: return length > 0 && length <= maxHelloPayload ? .data : .close
        case .hello: return length <= StreamMessage.maxClientPayload ? .hello : .close
        default: return length <= StreamMessage.maxClientPayload ? .other : .close
        }
    }

    /// A TLS door's `sill-data/1` connection: its first message must be kind 49 within `maxHelloPayload`;
    /// anything else refuses it.
    static func readsHello(kind: StreamMessageKind, length: Int) -> Bool {
        kind == .dataHello && length > 0 && length <= maxHelloPayload
    }

    // MARK: On the connection

    enum LinkRead: Equatable, Sendable {
        /// Kinds 34 (a part) and 37 (a file asked for): handed on as the session's own.
        case deliver
        /// Any other kind: read and dropped, as a kind a peer does not know is skipped.
        case skip
        /// A header announcing more than `maxPayload`: a broken or hostile peer, closed.
        case close
    }

    static func onLink(kind: StreamMessageKind, length: Int) -> LinkRead {
        guard length >= 0, length <= maxPayload else { return .close }
        return kind == .fileChunk || kind == .fileRequest ? .deliver : .skip
    }
}

/// How the host paces the file parts it sends a device (kind 34, for kind 37), so the session's picture
/// comes first. One per session; `decide` before each part, `sent` after it. Pure: `now` in seconds on
/// one monotonic clock.
///
/// - On the bulk connection a part is up to 512 KB; without it, on the session, 64 KB at home and 16 KB
///   away (`partBytes`), so input, pings and frames queued behind one wait a few milliseconds.
/// - The session first: while the server is withholding its picture's frames (a delta dropped and a
///   keyframe awaited, frames held back this second) or the session's own queue is longer than
///   `sessionLimit`, below where the server starts dropping side streams' frames (SideSend's budgets,
///   192 KiB at home and 64 KiB away), no part goes; but one at least every `starvation` seconds, so a
///   session stuck waiting for a keyframe never stops a transfer for good.
/// - The connection's own queue: at most `linkLimit` bytes handed to the bulk connection and not taken.
/// - A modest rate otherwise (`rate`, a token bucket of `burst`): the link's room for the picture is kept
///   before the picture has to suffer for it.
struct DataPacing: Equatable {
    /// What the server says of the session and its bulk connection now.
    struct Load: Equatable, Sendable {
        /// The bulk connection is up: the part goes there.
        var onLink: Bool
        /// The session came through the remote door (away from home).
        var remote: Bool
        /// The server is withholding the session's frames (StreamServer.dataLoad).
        var withholding: Bool
        /// Bytes handed to the session's connection and not taken yet.
        var sessionPending: Int
        /// Bytes handed to the bulk connection and not taken yet (0 without one).
        var linkPending: Int
        init(onLink: Bool, remote: Bool = false, withholding: Bool = false, sessionPending: Int = 0, linkPending: Int = 0) {
            self.onLink = onLink; self.remote = remote; self.withholding = withholding
            self.sessionPending = sessionPending; self.linkPending = linkPending
        }
    }

    enum Decision: Equatable, Sendable {
        case send
        /// Ask again after this many seconds (or when something the connection took frees room).
        case wait(Double)
    }

    /// A part's bytes: FileChunk's most on the bulk connection; on the session 64 KB at home (the Windows
    /// client's own split) and 16 KB away, under SideSend's 64 KiB budget there.
    static func partBytes(onLink: Bool, remote: Bool = false) -> Int { onLink ? FileChunk.maxBytes : remote ? 16 * 1024 : 64 * 1024 }

    /// Bytes a second: on the bulk connection 20 MiB at home (about 170 Mbit/s, a third of what Wi-Fi 6
    /// carries) and 1 MiB away; on the session 4 MiB at home and 256 KiB away.
    static func rate(_ load: Load) -> Double {
        switch (load.onLink, load.remote) {
        case (true, false): return 20 * 1_048_576
        case (true, true): return 1_048_576
        case (false, false): return 4 * 1_048_576
        case (false, true): return 256 * 1024
        }
    }

    /// The bucket: two parts on the bulk connection, two on the session.
    static func burst(onLink: Bool, remote: Bool = false) -> Double { Double(2 * partBytes(onLink: onLink, remote: remote)) }

    /// The session's own queue past which its link counts as busy, two thirds of SideSend's budget (past
    /// which a side stream's delta is dropped): 128 KiB at home and 40 KiB away beside the bulk connection
    /// (a keyframe crossing holds parts back); on the session itself a part waits for its queue to be
    /// nearly empty, 32 KiB at home and 8 KiB away, so the part and what queues behind it stay under it.
    static func sessionLimit(_ load: Load) -> Int {
        switch (load.onLink, load.remote) {
        case (true, false): return 128 * 1024
        case (true, true): return 40 * 1024
        case (false, false): return 32 * 1024
        case (false, true): return 8 * 1024
        }
    }

    /// The bulk connection's own queue: two parts at home, one away.
    static func linkLimit(remote: Bool) -> Int { remote ? FileChunk.maxBytes : 2 * FileChunk.maxBytes }

    /// How long a part waits before asking again while the session comes first or a queue is full.
    static let pause = 0.05
    /// The longest a transfer goes without a part while the session comes first.
    static let starvation = 2.0
    /// The shortest wait (a timer's own granularity).
    static let minWait = 0.001

    private(set) var tokens: Double
    private(set) var refilledAt: Double?
    /// Since when the session has held parts back (nil while parts go).
    private(set) var heldSince: Double?

    init(onLink: Bool = true, remote: Bool = false) { tokens = Self.burst(onLink: onLink, remote: remote) }

    /// Whether the next part of `bytes` goes now.
    mutating func decide(bytes: Int, load: Load, now: Double) -> Decision {
        let rate = Self.rate(load), burst = Self.burst(onLink: load.onLink, remote: load.remote)
        if let at = refilledAt { tokens = min(burst, tokens + max(0, now - at) * rate) } else { tokens = min(tokens, burst) }
        refilledAt = now
        if load.onLink, load.linkPending >= Self.linkLimit(remote: load.remote) { return .wait(Self.pause) }
        if load.withholding || load.sessionPending > Self.sessionLimit(load) {
            let since = heldSince ?? now
            heldSince = since
            if now - since < Self.starvation { return .wait(min(Self.pause, Self.starvation - (now - since))) }
        }
        // A byte's slack and a millisecond at least: the clock's rounding never makes a wait of nothing.
        let need = Double(min(bytes, Int(burst)))
        if tokens + 1 < need { return .wait(max(Self.minWait, (need - tokens) / rate)) }
        return .send
    }

    /// A part of `bytes` went at `now`.
    mutating func sent(bytes: Int, now: Double) {
        tokens -= Double(bytes)
        heldSince = nil
    }
}

/// How long a device's message that names its transfers (kind 35's drop, kind 38's `files`, kind 32's
/// `files.transfers`) waits for their parts. With the bulk connection, parts and the message go on two
/// connections, and the message can overtake the last parts (the device sends it once every part is
/// queued, not taken). It waits while any named transfer is not whole and parts of them keep coming,
/// and goes with what came once `quiet` seconds pass with none, or `most` after it was asked.
enum TransferWait {
    static let quiet: Double = 3
    static let most: Double = 600

    enum Verdict: Equatable { case ready, wait(until: Double) }

    /// `names`: the transfers named; `whole`: those whole now; `askedAt`: when the message came;
    /// `lastPart`: when a part of any named one last came (nil for none since).
    static func decide(names: [UInt32], whole: Set<UInt32>, askedAt: Double, lastPart: Double?, now: Double) -> Verdict {
        if names.allSatisfy(whole.contains) { return .ready }
        let until = min(max(askedAt, lastPart ?? askedAt) + quiet, askedAt + most)
        return now >= until ? .ready : .wait(until: until)
    }
}
