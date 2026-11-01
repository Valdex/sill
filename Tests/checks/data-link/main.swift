import Foundation

// Coherence's bulk connection, the host's rules (Sources/SillHost/DataLinkPolicy.swift; docs/coherence-protocol.md,
// "The bulk connection"): the ticket, admission of a `sill-data/1` connection for the session holding it,
// what the plain door and the connection read, the pacing of the host's parts on a simulated clock, and
// how long a message naming transfers waits for them. Then random runs against an oracle of the contract.
var failures = 0
var checks = 0
func check(_ ok: Bool, _ what: @autoclosure () -> String, line: Int = #line) {
    checks += 1
    if !ok { failures += 1; print("FAIL (line \(line)): \(what())") }
}
var rng = SystemRandomNumberGenerator()
typealias P = DataLinkPolicy

// MARK: - The ticket

let zeros = [UInt8](repeating: 0, count: 32)
check(P.token(zeros) == "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", "32 bytes as base64: 44 characters")
check(P.token([UInt8](repeating: 1, count: 31)) == nil && P.token([UInt8](repeating: 1, count: 33)) == nil && P.token([]) == nil,
      "any other count makes no token")
check(DataTicket.tokenBytes == 32 && P.generation == 1 && P.alpn == "sill-data/1", "32 bytes; data 1; the ALPN")
check(P.maxPayload == (1 << 20) + 64 && P.deadline == 4 && P.maxHelloPayload == 512, "the caps and the 4 s")
var tokens = Set<String>()
for _ in 0..<1_000 {
    let t = P.token((0..<32).map { _ in UInt8.random(in: 0...255, using: &rng) })!
    tokens.insert(t)
    if !P.tokenValid(t) || Data(base64Encoded: t)?.count != 32 { check(false, "a random token reads as a ticket: \(t)"); break }
}
check(tokens.count == 1_000, "1,000 random tokens, all different")
check(!P.tokenValid("") && !P.tokenValid("not base64!") && !P.tokenValid(Data(count: 31).base64EncodedString())
      && !P.tokenValid(Data(count: 33).base64EncodedString()) && !P.tokenValid(String(repeating: "A", count: 68))
      && !P.tokenValid("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
      "not a ticket: empty, not base64, 31 or 33 bytes, too long, unpadded")
for offered in [false, true] { for c in [nil, 0, 1, 2] { for list in [false, true] { for sent in [false, true] { for revoked in [false, true] {
    let want = offered && (c ?? 0) >= 1 && list && !sent && !revoked
    check(P.sendsTicket(offered: offered, coherence: c, listSent: list, sent: sent, revoked: revoked) == want,
          "ticket: offered \(offered) coherence \(String(describing: c)) list \(list) sent \(sent) revoked \(revoked)")
} } } } }

// MARK: - Admission

let keyA = Data([1, 2, 3]), keyB = Data([4, 5, 6])
let tokA = P.token((0..<32).map { UInt8($0) })!, tokB = P.token((0..<32).map { UInt8(255 - $0) })!, tokC = P.token((0..<32).map { _ in 7 })!
let homeA = P.Peer(remote: false, encrypted: true, fingerprint: keyA, address: "10.0.0.5")
let awayB = P.Peer(remote: true, encrypted: true, fingerprint: keyB, address: "100.64.0.9")
let plainC = P.Peer(remote: false, encrypted: false, fingerprint: nil, address: "fe80::1%en0")
let tickets: [String: P.Ticket<String>] = [tokA: .init(session: "A", peer: homeA), tokB: .init(session: "B", peer: awayB),
                                          tokC: .init(session: "C", peer: plainC)]
func admit(_ t: String?, _ p: P.Peer, linked: Set<String> = [], revoked: Set<String> = []) -> P.Verdict<String> {
    P.admit(token: t, tickets: tickets, linked: linked, revoked: revoked, presented: p)
}
check(admit(tokA, homeA) == .admit("A"), "home TLS: the same key and address at the same door: its session")
check(admit(tokB, awayB) == .admit("B"), "remote: the same key and address: its session")
check(admit(tokC, plainC) == .admit("C"), "the plain door: plain, from the session's address (its interface too)")
check(admit(nil, homeA) == .refuse(.notTicket) && admit("abc", homeA) == .refuse(.notTicket), "a kind 49 that does not decode, or no ticket")
check(admit(P.token([UInt8](repeating: 9, count: 32))!, homeA) == .refuse(.unknown), "a ticket nobody holds")
var other = homeA; other.fingerprint = keyB
check(admit(tokA, other) == .refuse(.key), "another key than its session's")
other = homeA; other.fingerprint = nil
check(admit(tokA, other) == .refuse(.key), "a TLS session's ticket with no key")
do {
    // A TLS session always has a key; were one held without, nothing would match it, a keyless peer least.
    var keyless = tickets; keyless[tokA]!.peer.fingerprint = nil
    var bare = homeA; bare.fingerprint = nil
    check(P.admit(token: tokA, tickets: keyless, linked: [], revoked: [], presented: bare) == .refuse(.key)
          && P.admit(token: tokA, tickets: keyless, linked: [], revoked: [], presented: homeA) == .refuse(.key),
          "a TLS ticket held without a key admits nothing, a peer without a key least")
}
other = homeA; other.remote = true
check(admit(tokA, other) == .refuse(.door), "a home session's ticket at the remote door")
other = awayB; other.remote = false
check(admit(tokB, other) == .refuse(.door), "a remote session's ticket at the home door")
other = homeA; other.encrypted = false; other.fingerprint = nil
check(admit(tokA, other) == .refuse(.door), "a TLS session's ticket on a plain connection")
other = plainC; other.encrypted = true; other.fingerprint = keyA
check(admit(tokC, other) == .refuse(.door), "a plain session's ticket over TLS")
other = plainC; other.fingerprint = keyA
check(admit(tokC, other) == .refuse(.door), "a plain connection claiming a key")
other = homeA; other.address = "10.0.0.6"
check(admit(tokA, other) == .refuse(.address), "another address than its session's")
other = plainC; other.address = "fe80::1%en1"
check(admit(tokC, other) == .refuse(.address), "the same link-local address on another interface")
check(admit(tokA, homeA, linked: ["A"]) == .refuse(.busy) && admit(tokA, homeA, linked: ["B"]) == .admit("A"), "one per session")
check(admit(tokA, homeA, revoked: ["A"]) == .refuse(.revoked) && admit(tokA, homeA, linked: ["A"], revoked: ["A"]) == .refuse(.revoked),
      "never for a session the Mac cut off")
// An oracle of the contract over random tickets and presentations.
func oracle(_ token: String?, _ p: P.Peer, _ linked: Set<String>, _ revoked: Set<String>) -> P.Verdict<String> {
    guard let token, let raw = Data(base64Encoded: token), raw.count == 32, token.count <= 64 else { return .refuse(.notTicket) }
    guard let t = tickets[token] else { return .refuse(.unknown) }
    if t.peer.remote != p.remote || t.peer.encrypted != p.encrypted { return .refuse(.door) }
    if t.peer.encrypted { if t.peer.fingerprint == nil || t.peer.fingerprint != p.fingerprint { return .refuse(.key) } } else if p.fingerprint != nil { return .refuse(.door) }
    if t.peer.address != p.address { return .refuse(.address) }
    if revoked.contains(t.session) { return .refuse(.revoked) }
    if linked.contains(t.session) { return .refuse(.busy) }
    return .admit(t.session)
}
var agreed = 0, admitted = 0
let keys: [Data?] = [nil, keyA, keyB]
let addresses = ["10.0.0.5", "10.0.0.6", "100.64.0.9", "fe80::1%en0"]
for _ in 0..<20_000 {
    let token = [tokA, tokB, tokC, nil, "x", P.token([UInt8](repeating: 3, count: 32))!].randomElement(using: &rng)!
    let p = P.Peer(remote: Bool.random(using: &rng), encrypted: Bool.random(using: &rng), fingerprint: keys.randomElement(using: &rng)!,
                   address: addresses.randomElement(using: &rng)!)
    let linked = Set(["A", "B", "C"].filter { _ in Int.random(in: 0..<4, using: &rng) == 0 })
    let revoked = Set(["A", "B", "C"].filter { _ in Int.random(in: 0..<6, using: &rng) == 0 })
    let v = admit(token, p, linked: linked, revoked: revoked)
    if v == oracle(token, p, linked, revoked) { agreed += 1 } else { check(false, "random admission: \(String(describing: token)) \(p) → \(v)"); break }
    if case .admit(let s) = v {
        admitted += 1
        // Whatever is admitted presented exactly its session's door, way, key and address.
        if tickets.values.first(where: { $0.session == s })?.peer != p { check(false, "admitted with another presentation: \(p)"); break }
    }
}
check(agreed == 20_000 && admitted > 0, "20,000 random presentations as the contract says (\(admitted) admitted, each its session's exact peer)")

// The plain door reads a first message only while a plain session at that address awaits its connection.
check(P.awaitsPlain(address: "fe80::1%en0", tickets: tickets, linked: []), "a plain session's address awaits")
check(!P.awaitsPlain(address: "fe80::1%en0", tickets: tickets, linked: ["C"]), "not once it has its connection")
check(!P.awaitsPlain(address: "10.0.0.5", tickets: tickets, linked: []), "a TLS session's address never (the plain door takes no TLS)")
check(!P.awaitsPlain(address: "10.9.9.9", tickets: tickets, linked: []) && !P.awaitsPlain(address: "fe80::1%en0", tickets: [String: P.Ticket<String>](), linked: []),
      "another address, or no ticket: every connection as before")
var remotePlain = tickets; remotePlain[tokC]!.peer.remote = true
check(!P.awaitsPlain(address: "fe80::1%en0", tickets: remotePlain, linked: []), "never for the remote door")

check(P.plainFirst(kind: .dataHello, length: 56) == .data && P.plainFirst(kind: .dataHello, length: 512) == .data, "a kind 49: the bulk connection")
check(P.plainFirst(kind: .dataHello, length: 0) == .close && P.plainFirst(kind: .dataHello, length: 513) == .close, "a kind 49 empty or longer than a ticket: closed")
check(P.plainFirst(kind: .hello, length: 300) == .hello && P.plainFirst(kind: .hello, length: 1 << 20) == .hello
      && P.plainFirst(kind: .hello, length: (1 << 20) + 1) == .close, "a hello: the session's, within the device cap")
check(P.plainFirst(kind: .viewport, length: 80) == .other && P.plainFirst(kind: .unknown, length: 0) == .other
      && P.plainFirst(kind: .ping, length: (1 << 20) + 1) == .close, "anything else: the session's first message, within the cap")
check(P.readsHello(kind: .dataHello, length: 56) && P.readsHello(kind: .dataHello, length: 512), "TLS: a kind 49 within 512 bytes")
check(!P.readsHello(kind: .dataHello, length: 0) && !P.readsHello(kind: .dataHello, length: 513) && !P.readsHello(kind: .hello, length: 56)
      && !P.readsHello(kind: .fileChunk, length: 56), "TLS: anything else first refuses it")
check(P.onLink(kind: .fileChunk, length: (1 << 20) + 64) == .deliver && P.onLink(kind: .fileRequest, length: 40) == .deliver, "kinds 34 and 37 handed on, up to the cap")
check(P.onLink(kind: .fileChunk, length: (1 << 20) + 65) == .close && P.onLink(kind: .input, length: Int(UInt32.max)) == .close, "past the cap: closed")
check(P.onLink(kind: .input, length: 30) == .skip && P.onLink(kind: .clipboard, length: 30) == .skip && P.onLink(kind: .unknown, length: 0) == .skip
      && P.onLink(kind: .hello, length: 30) == .skip, "anything else read and dropped (input never acts from it)")

// MARK: - Pacing

typealias L = DataPacing.Load
check(DataPacing.partBytes(onLink: true) == 512 * 1024 && DataPacing.partBytes(onLink: true, remote: true) == 512 * 1024
      && DataPacing.partBytes(onLink: false) == 64 * 1024 && DataPacing.partBytes(onLink: false, remote: true) == 16 * 1024,
      "parts: 512 KB on the link, 64 KB on the session at home, 16 KB away")
check(DataPacing.sessionLimit(L(onLink: true)) < SideSend.budgetHome && DataPacing.sessionLimit(L(onLink: true, remote: true)) < SideSend.budgetRemote
      && DataPacing.sessionLimit(L(onLink: false)) + DataPacing.partBytes(onLink: false) < SideSend.budgetHome
      && DataPacing.sessionLimit(L(onLink: false, remote: true)) + DataPacing.partBytes(onLink: false, remote: true) < SideSend.budgetRemote,
      "parts wait below where side streams' frames are dropped (SideSend's budgets), a part on the session included")
/// Sends parts of `bytes` whenever the pacer lets them for `seconds` on a simulated clock (the
/// connection takes each at once unless `pending` says otherwise); returns the parts sent and when.
func simulate(_ load: (Double) -> L, bytes: Int, seconds: Double, pacer: inout DataPacing) -> [Double] {
    var now = 0.0, sent: [Double] = []
    var guardSteps = 0
    while now < seconds, guardSteps < 1_000_000 {
        guardSteps += 1
        switch pacer.decide(bytes: bytes, load: load(now), now: now) {
        case .send: pacer.sent(bytes: bytes, now: now); sent.append(now)
        case .wait(let t):
            if !(t > 0) { check(false, "a wait of \(t) s at \(now)"); return sent }
            now += t
        }
    }
    return sent
}
do {
    var p = DataPacing(onLink: true)
    let s = simulate({ _ in L(onLink: true) }, bytes: 512 * 1024, seconds: 10, pacer: &p)
    let mib = Double(s.count) * 0.5
    check(s.prefix(2).allSatisfy { $0 == 0 }, "home, the link idle: the first two parts at once (the bucket)")
    check(mib >= 200 && mib <= 202 + 1, "home on the link: 20 MiB a second (\(mib) MiB in 10 s)")
    let gaps = zip(s.dropFirst(2), s.dropFirst(3)).map { $1 - $0 }
    check(gaps.allSatisfy { abs($0 - 0.025) < 1e-6 }, "then a part every 25 ms, evenly")
}
do {
    var p = DataPacing(onLink: true)
    let s = simulate({ _ in L(onLink: true, remote: true) }, bytes: 512 * 1024, seconds: 10, pacer: &p)
    check(s.count >= 21 && s.count <= 22, "away on the link: 1 MiB a second (\(s.count) parts in 10 s)")
    var q = DataPacing(onLink: false)
    let h = simulate({ _ in L(onLink: false) }, bytes: 64 * 1024, seconds: 10, pacer: &q)
    check(h.count >= 641 && h.count <= 643, "home on the session: 4 MiB a second of 64 KB parts (\(h.count))")
    var r = DataPacing(onLink: false, remote: true)
    let a = simulate({ _ in L(onLink: false, remote: true) }, bytes: 16 * 1024, seconds: 10, pacer: &r)
    check(a.count >= 161 && a.count <= 163, "away on the session: 256 KiB a second of 16 KB parts (\(a.count))")
}
do {
    var p = DataPacing(onLink: true)
    check(p.decide(bytes: 512 * 1024, load: L(onLink: true, linkPending: 1 << 20), now: 0) == .wait(DataPacing.pause), "the link holds two parts at home: wait")
    check(p.decide(bytes: 512 * 1024, load: L(onLink: true, linkPending: (1 << 20) - 1), now: 0) == .send, "under it: go")
    check(p.decide(bytes: 512 * 1024, load: L(onLink: true, remote: true, linkPending: 512 * 1024), now: 0) == .wait(DataPacing.pause), "away it holds one")
    var q = DataPacing(onLink: false)
    check(q.decide(bytes: 64 * 1024, load: L(onLink: false, linkPending: 50 << 20), now: 0) == .send, "on the session the link's queue means nothing")
}
do {
    // Withholding throughout 10 s: a part only every 2 s.
    var p = DataPacing(onLink: true)
    let s = simulate({ _ in L(onLink: true, withholding: true) }, bytes: 512 * 1024, seconds: 10.001, pacer: &p)
    check(s.count >= 5 && s.count <= 6, "while the session's frames are withheld: one part every 2 s at most (\(s.count) in 10 s: \(s))")
    check(zip(s, s.dropFirst()).allSatisfy { $1 - $0 >= DataPacing.starvation - 1e-9 }, "never two within 2 s")
    var q = DataPacing(onLink: true)
    let b = simulate({ _ in L(onLink: true, sessionPending: 128 * 1024 + 1) }, bytes: 512 * 1024, seconds: 10.001, pacer: &q)
    check(b.count >= 5 && b.count <= 6, "the session's own queue past 128 KiB at home: the same (\(b.count))")
    var r = DataPacing(onLink: true)
    let c = simulate({ _ in L(onLink: true, sessionPending: 128 * 1024) }, bytes: 512 * 1024, seconds: 1, pacer: &r)
    check(c.count > 20, "at 128 KiB exactly it goes on (\(c.count) in 1 s)")
    var w = DataPacing(onLink: true, remote: true)
    let d = simulate({ _ in L(onLink: true, remote: true, sessionPending: 40 * 1024 + 1) }, bytes: 512 * 1024, seconds: 4.001, pacer: &w)
    check(d.count <= 3, "away: past 40 KiB the session comes first (\(d.count) in 4 s)")
    var f = DataPacing(onLink: false)
    let e = simulate({ _ in L(onLink: false, sessionPending: 32 * 1024 + 1) }, bytes: 64 * 1024, seconds: 4.001, pacer: &f)
    check(e.count <= 3, "on the session: past 32 KiB of its own queue, the part waits (\(e.count) in 4 s)")
    var g2 = DataPacing(onLink: false, remote: true)
    let e2 = simulate({ _ in L(onLink: false, remote: true, sessionPending: 8 * 1024 + 1) }, bytes: 16 * 1024, seconds: 4.001, pacer: &g2)
    check(e2.count <= 3, "away on the session: past 8 KiB (\(e2.count) in 4 s)")
    // Withholding for 3 s, then not: the bucket is full again, so parts go at once.
    var g = DataPacing(onLink: true)
    let h = simulate({ $0 < 3 ? L(onLink: true, withholding: true) : L(onLink: true) }, bytes: 512 * 1024, seconds: 4, pacer: &g)
    let before = h.filter { $0 < 3 }.count, after = h.filter { $0 >= 3 && $0 < 3.06 }.count
    check(before <= 2 && after >= 2, "withheld 3 s then free: \(before) parts while withheld, \(after) in the first 60 ms after")
    check(h.filter { $0 >= 3 }.count >= 39, "and the rate after (\(h.filter { $0 >= 3 }.count) in the last second)")
}
// Random loads: never past the link's limit, never two parts within 2 s while the session is held, never
// faster than the rate allows over any window, and never stalled beyond 2 s while only the session holds.
for run in 0..<300 {
    let remote = run % 3 == 0
    var p = DataPacing(onLink: true, remote: remote)
    var now = 0.0, last: Double? = nil, lastHeld: Double? = nil, sentBytes = 0.0
    var ok = true
    var steps = 0
    while now < 20, steps < 200_000 {
        steps += 1
        let held = Int.random(in: 0..<4, using: &rng) == 0
        let pending = Int.random(in: 0..<5, using: &rng) == 0 ? (1 << 20) : 0
        let load = L(onLink: true, remote: remote, withholding: held, sessionPending: 0, linkPending: pending)
        switch p.decide(bytes: 512 * 1024, load: load, now: now) {
        case .send:
            if load.linkPending >= DataPacing.linkLimit(remote: remote) { ok = false }
            if held, let l = lastHeld, now - l < DataPacing.starvation - 1e-9, let s = last, s >= l { ok = false }
            p.sent(bytes: 512 * 1024, now: now)
            sentBytes += 512 * 1024
            last = now
        case .wait(let t):
            if !(t > 0 && t <= 1) { ok = false }
            if held, lastHeld == nil { lastHeld = now }
            now += t
        }
        if !held { lastHeld = nil }
    }
    let allowed = DataPacing.rate(L(onLink: true, remote: remote)) * now + DataPacing.burst(onLink: true, remote: remote)
    if !ok || sentBytes > allowed + 1 { check(false, "random pacing run \(run): ok \(ok), \(sentBytes) bytes > \(allowed)"); break }
}
check(true, "300 random load runs: the link's limit, the rate and the waits hold")

// MARK: - Waiting for transfers

typealias W = TransferWait
check(W.decide(names: [1, 2], whole: [1, 2, 3], askedAt: 10, lastPart: nil, now: 10) == .ready, "every named transfer whole: at once")
check(W.decide(names: [], whole: [], askedAt: 10, lastPart: nil, now: 10) == .ready, "naming none: at once")
check(W.decide(names: [1, 2], whole: [1], askedAt: 10, lastPart: nil, now: 10) == .wait(until: 13), "one not whole and no part since: 3 s")
check(W.decide(names: [1, 2], whole: [1], askedAt: 10, lastPart: 12, now: 12.5) == .wait(until: 15), "a part came: 3 s after it")
check(W.decide(names: [1, 2], whole: [1], askedAt: 10, lastPart: 9, now: 12.5) == .wait(until: 13), "a part before the ask counts from the ask")
check(W.decide(names: [1, 2], whole: [1], askedAt: 10, lastPart: 12, now: 15) == .ready, "3 s without a part: with what came")
check(W.decide(names: [1], whole: [], askedAt: 0, lastPart: 599, now: 600) == .ready && W.decide(names: [1], whole: [], askedAt: 0, lastPart: 590, now: 590) == .wait(until: 593),
      "parts that keep coming: 600 s at most")
// Random: ready exactly when every one is whole, or 3 s passed since the ask and the last part, or 600 s.
for _ in 0..<5_000 {
    let names = (0..<Int.random(in: 0...4, using: &rng)).map { _ in UInt32.random(in: 1...6, using: &rng) }
    let whole = Set((1...6).filter { _ in Bool.random(using: &rng) }.map(UInt32.init))
    let asked = Double.random(in: 0...100, using: &rng)
    let lastPart: Double? = Bool.random(using: &rng) ? Double.random(in: 0...800, using: &rng) : nil
    let now = asked + Double.random(in: 0...700, using: &rng)
    let v = W.decide(names: names, whole: whole, askedAt: asked, lastPart: lastPart, now: now)
    let quietSince = max(asked, lastPart ?? asked)
    let ready = names.allSatisfy(whole.contains) || now >= min(quietSince + 3, asked + 600)
    if (v == .ready) != ready { check(false, "random wait: \(names) whole \(whole) asked \(asked) last \(String(describing: lastPart)) now \(now) → \(v)"); break }
    if case .wait(let until) = v, !(until > now) { check(false, "a wait that is already over: \(until) ≤ \(now)"); break }
}
check(true, "5,000 random waits as the rule says")

print(failures == 0 ? "\(checks) checks, 0 failed" : "\(failures) FAILED of \(checks)")
exit(failures == 0 ? 0 : 1)
