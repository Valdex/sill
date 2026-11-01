"""Mutants of the bulk connection's rules (Sources/SillHost/DataLinkPolicy.swift): each must compile with the
check and make it fail. usage: mutants.py ROOT"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import module

P = "Sources/SillHost/DataLinkPolicy.swift"
MUTANTS = [
    ("a token of any count", P, "bytes.count == DataTicket.tokenBytes ? Data(bytes).base64EncodedString() : nil", "Data(bytes).base64EncodedString()"),
    ("a ticket of 31 bytes taken", P, "return d.count == DataTicket.tokenBytes", "return d.count >= DataTicket.tokenBytes - 1"),
    ("a ticket without coherence", P, "offered && (coherence ?? 0) >= CoherenceGeneration.current && listSent", "offered && listSent"),
    ("a ticket before the list", P, "&& listSent && !sent", "&& !sent"),
    ("a second ticket", P, "&& !sent && !revoked", "&& !revoked"),
    ("a ticket to a revoked session", P, "&& !sent && !revoked", "&& !sent"),
    ("no door check", P, "guard held.remote == presented.remote, held.encrypted == presented.encrypted else { return .refuse(.door) }", ""),
    ("TLS and plain alike", P, "guard held.remote == presented.remote, held.encrypted == presented.encrypted else", "guard held.remote == presented.remote else"),
    ("no key check", P, "guard let key = held.fingerprint, let shown = presented.fingerprint, key == shown else { return .refuse(.key) }", ""),
    ("a missing key matches a missing key", P, "guard let key = held.fingerprint, let shown = presented.fingerprint, key == shown else", "guard held.fingerprint == presented.fingerprint else"),
    ("a plain connection's key ignored", P, "            guard presented.fingerprint == nil else { return .refuse(.door) }\n", ""),
    ("no address check", P, "guard held.address == presented.address else { return .refuse(.address) }", ""),
    ("a revoked session's link", P, "guard !revoked.contains(ticket.session) else { return .refuse(.revoked) }", ""),
    ("two links per session", P, "guard !linked.contains(ticket.session) else { return .refuse(.busy) }", ""),
    ("the plain door reads every first message", P, "!$0.peer.encrypted && !$0.peer.remote && $0.peer.address == address && !linked.contains($0.session)", "!$0.peer.encrypted"),
    ("the plain door reads once linked", P, "$0.peer.address == address && !linked.contains($0.session)", "$0.peer.address == address"),
    ("a long kind 49 at the plain door", P, "case .dataHello: return length > 0 && length <= maxHelloPayload ? .data : .close", "case .dataHello: return .data"),
    ("a hello past the cap", P, "case .hello: return length <= StreamMessage.maxClientPayload ? .hello : .close", "case .hello: return .hello"),
    ("TLS: a long kind 49", P, "kind == .dataHello && length > 0 && length <= maxHelloPayload", "kind == .dataHello && length > 0"),
    ("TLS: any first kind", P, "kind == .dataHello && length > 0 && length <= maxHelloPayload", "length > 0 && length <= maxHelloPayload"),
    ("no cap on the link", P, "guard length >= 0, length <= maxPayload else { return .close }", "guard length >= 0 else { return .close }"),
    ("input acts from the link", P, "return kind == .fileChunk || kind == .fileRequest ? .deliver : .skip", "return kind == .fileChunk || kind == .fileRequest || kind == .input ? .deliver : .skip"),
    ("the link's queue ignored", P, "if load.onLink, load.linkPending >= Self.linkLimit(remote: load.remote) { return .wait(Self.pause) }", ""),
    ("withholding ignored", P, "if load.withholding || load.sessionPending > Self.sessionLimit(load) {", "if load.sessionPending > Self.sessionLimit(load) {"),
    ("the session's queue ignored", P, "if load.withholding || load.sessionPending > Self.sessionLimit(load) {", "if load.withholding {"),
    ("starved for good", P, "if now - since < Self.starvation { return", "if true { return"),
    ("no rate", P, "if tokens + 1 < need { return .wait(max(Self.minWait, (need - tokens) / rate)) }", ""),
    ("ten times the rate at home", P, "case (true, false): return 20 * 1_048_576", "case (true, false): return 200 * 1_048_576"),
    ("away as fast as home", P, "case (true, true): return 1_048_576", "case (true, true): return 20 * 1_048_576"),
    ("512 KB parts on the session", P, "onLink ? FileChunk.maxBytes : remote ? 16 * 1024 : 64 * 1024", "FileChunk.maxBytes"),
    ("64 KB parts away on the session", P, "onLink ? FileChunk.maxBytes : remote ? 16 * 1024 : 64 * 1024", "onLink ? FileChunk.maxBytes : 64 * 1024"),
    ("the session limit away as at home", P, "case (true, true): return 40 * 1024", "case (true, true): return 128 * 1024"),
    ("the session limit at home over the side budget", P, "case (true, false): return 128 * 1024", "case (true, false): return 1_048_576"),
    ("the session's own limit as the link's", P, "case (false, false): return 32 * 1024", "case (false, false): return 128 * 1024"),
    ("a part sent never spends", P, "        tokens -= Double(bytes)\n", ""),
    ("a part sent keeps the hold", P, "        heldSince = nil\n    }", "    }"),
    ("no quiet", P, "let until = min(max(askedAt, lastPart ?? askedAt) + quiet, askedAt + most)", "let until = askedAt + most"),
    ("parts never extend", P, "let until = min(max(askedAt, lastPart ?? askedAt) + quiet, askedAt + most)", "let until = askedAt + quiet"),
    ("ready with one whole", P, "if names.allSatisfy(whole.contains) { return .ready }", "if names.contains(where: whole.contains) || names.isEmpty { return .ready }"),
]
module.mutate("data-link", MUTANTS, sys.argv[1])
