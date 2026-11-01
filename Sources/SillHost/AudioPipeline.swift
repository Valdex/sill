import Foundation
import StreamProtocol

/// A source of the Mac's sound: ScreenCaptureKit's audio-only stream (AudioCapture), or a synthetic
/// host's test tone (SyntheticAudio). Both deliver on the pipeline's queue.
protocol AudioSource: AnyObject {
    /// On the pipeline's queue: PCM as delivered, its format, and the host time of its first frame.
    var onPCM: ((PCMChunk) -> Void)? { get set }
    /// It ended by itself; why, for the log and the Mac's status. Not called by `stop()`.
    var onStopped: ((String) -> Void)? { get set }
    func start() async throws
    /// Returns once no chunk is in flight.
    func stop() async
}

/// A source's own failure, in its own words (AudioCapture's timeouts); any other error is the system's
/// and is shown with its domain and code.
protocol AudioSourceFailure: Error, CustomStringConvertible {}

/// Seconds of the host clock: mach absolute time, the clock ScreenCaptureKit stamps its buffers with
/// (CMClockGetHostTimeClock counts the same ticks).
enum HostClock {
    private static let scale: Double = {
        var info = mach_timebase_info_data_t()
        mach_timebase_info(&info)
        return Double(info.numer) / Double(info.denom) / 1_000_000_000
    }()

    static func now() -> Double { Double(mach_absolute_time()) * scale }
}

/// The Mac's sound, from its source to the server (docs/audio-plan.md §4.5): the source, the
/// packetizer and the AAC-ELD encoder on `sill.audio` (and, for a device that plays it, `pcm-s16le` with
/// no encoder: this fork's, docs/coherence-protocol.md), the epochs, and what the coordinator and the
/// Mac's status see of them. It never imports ScreenCaptureKit (the coordinator's `make` builds the
/// source), so the encoder-free harness (Scripts/audio) compiles it with the test tone.
///
/// Main actor: which source is wanted and which runs (`follow`, one transition at a time in a Task, a
/// newer request replacing one not yet begun, as `setTarget`'s changes do), and the status. The
/// pipeline's queue: the chunks, the blocks, the encoder, the stamps and the messages, handed to the
/// server (`onFormat`, `onPackets`) the moment they exist. Nothing of the sound touches the picture's
/// queues or counters, and a failure of the sound never stops, restarts or delays the picture: it
/// sets `status.problem`, prints one line and waits for the next `follow`.
///
/// Epochs: each codec's stream of the sound is an epoch of its own (0 to 65535, wrapping, one counter for
/// both codecs, so no two streams share a number), started again for another source, a source started
/// again, a new PCM format, and when a codec comes back after no device played it. Its format message goes
/// to the server before the epoch's first packet, and the packets' seq counts from 0 in each. The codecs
/// made are the connected devices' (`setCodecs`, the server's word): AAC-ELD for Apple's devices, as
/// before, and `pcm-s16le` only while a device that plays it is connected; neither while none is.
final class AudioPipeline: @unchecked Sendable {   // main-actor state, and state confined to `queue`, as marked
    /// Where the sound is made: the source's buffers, the tone's timer, the packetizer, the encoder.
    let queue = DispatchQueue(label: "sill.audio", qos: .userInteractive)

    /// A whole kind 43 message: the format of an epoch (type 1) of a codec, on `queue`, before that
    /// epoch's first packet; the server keeps it for the devices of that codec that join the epoch later.
    var onFormat: ((Data, Int, String) -> Void)?
    /// A whole kind 43 message of one packet (type 2) of an epoch of a codec, on `queue`, the moment it is
    /// made.
    var onPackets: ((Data, Int, String) -> Void)?

    /// What the Mac shows of the sound while it is wanted: its source ("Safari", "Whole Mac", "Test
    /// Tone"), whether it is captured, and why not.
    struct Status: Equatable {
        var source: String
        var capturing: Bool
        /// "couldn’t capture the sound of Safari: …"; nil while all is well.
        var problem: String?
    }
    /// Nil while no sound is wanted. Main actor.
    private(set) var status: Status? { didSet { if status != oldValue { onStatus?(status) } } }
    /// Main actor: every change of `status`.
    var onStatus: ((Status?) -> Void)?
    /// The status of the source there is (running, starting, failed or ended), whatever is wanted:
    /// `status` shows it while a source is wanted. Main actor.
    private var sourceStatus: Status? { didSet { if wanted != .none { status = sourceStatus } } }
    /// The newest source asked for. Main actor.
    private var wanted = AudioKey.none

    /// What `make` gives: a source, and the line that says what it captures, printed as it starts; or
    /// why there is none.
    enum Made {
        case source(AudioSource, line: String)
        case failed(String)
    }

    // Main actor.
    private var current: AudioKey = .none
    private var phase = AudioSourceRule.Phase.idle
    private var lastStartAt: TimeInterval = -.infinity
    private var source: AudioSource?
    private var generation = 0
    private struct Request {
        let key: AudioKey
        let reason: String
        let make: @MainActor (AudioKey) async -> Made
    }
    private var requested: Request?
    private var worker: Task<Void, Never>?
    /// AAC-ELD could not be made (`encoderProblem`): no sound this run, and every source shows why.
    private var encoderGone: String?
    /// "Audio stopped" was said for the source that runs.
    private var stopSaid = false

    // On `queue`.
    private var live = 0                 // the generation whose chunks count
    private var info = (source: AudioFormat.sourceTest, app: String?.none)
    /// One codec's stream of the source's sound: its epoch, its packetizer and its packets' count.
    private struct CodecStream {
        let codec: String
        let epoch: Int
        var packetizer: AudioPacketizer
        var seq: UInt32 = 0
        var gapsSeen = 0
    }
    private var aac: CodecStream?        // AAC-ELD's, through `encoder`
    private var encoder: AudioEncoder?
    private var pcm: CodecStream?        // pcm-s16le's, no encoder (PCMPacket)
    /// The last epoch given out, either codec's.
    private var epoch = 0
    /// The codecs the connected devices play (`setCodecs`); AAC-ELD alone until the server says.
    private var codecs: Set<String> = [AudioCodec.aacELD]
    private var encoderUnavailable = false
    private var formatRefused = false

    init() {}

    /// The codecs the connected devices play (the server's word, `StreamServer.onAudioCodecsChanged`), so a
    /// codec no device plays is not made: AAC-ELD for an iPhone or iPad, `pcm-s16le` for a Windows PC, both
    /// while both are connected. A codec no longer played stops; one played again starts with its next chunk,
    /// a new epoch. Any thread.
    func setCodecs(_ wanted: Set<String>) {
        queue.async {
            guard wanted != self.codecs else { return }
            self.codecs = wanted
        }
    }

    /// On `queue`: every codec's stream gone (another source, a source stopped or ended).
    private func dropStreams() {
        aac = nil
        encoder = nil
        pcm = nil
    }

    // MARK: The encoder, ready before the first chunk

    /// Send Audio is on (the coordinator: at start, and when it turns on): AudioToolbox's AAC-ELD codec
    /// is loaded now, in the background, off `sill.audio`. The first encoder of a process pays for the
    /// load (13 to 53 ms here; 1.9 to 3.3 s on a Mac loaded by other work, the chunks waiting behind it
    /// on `sill.audio`, so the session's first second came as a burst of late packets: docs/audio-plan.md,
    /// "The review"), and every later one takes about a millisecond, whatever its packet size. What it
    /// makes is thrown away: each epoch makes its own encoder on `queue`, at the size its first chunk
    /// says. Any thread; the load runs once a process (`codecLoaded`), and a failure is the epoch's to say.
    func prepare() {
        DispatchQueue.global(qos: .userInitiated).async { _ = Self.codecLoaded }
    }
    private static let codecLoaded: Bool = (try? AudioEncoder(framesPerPacket: 480)) != nil

    // MARK: The source (main actor)

    /// The sound should be of `key` (`.none`: no sound, `reason` saying why for the log's "Audio
    /// stopped" line: "nothing streams", "no connected device plays sound", "Send Audio is off").
    /// Returns at once; the work runs in a Task, and a newer call replaces one not yet begun. The same
    /// source running is kept; one that ended by itself or failed is started again at most every 10 s
    /// (`AudioSourceRule`); anything else stops what runs and starts the wanted one, with a new epoch.
    /// `make` builds the source when one is to start.
    @MainActor
    func follow(_ key: AudioKey, reason: String = "", make: @escaping @MainActor (AudioKey) async -> Made) {
        requested = Request(key: key, reason: reason, make: make)
        wanted = key
        if key == .none { status = nil }
        if worker == nil { worker = Task { @MainActor in await self.run() } }
    }

    /// Sill is quitting: said now, while the process still runs (the stop itself may not come before
    /// it ends, and needs nothing: the process's end takes the capture with it).
    @MainActor
    func shutdown() {
        if !stopSaid, current != .none, phase == .starting || phase == .running {
            stopSaid = true
            print("Audio stopped: Sill is quitting.")
        }
        requested = Request(key: .none, reason: "Sill is quitting", make: { _ in .failed("quitting") })
        wanted = .none
        status = nil
        if worker == nil { worker = Task { @MainActor in await self.run() } }
    }

    @MainActor
    private func run() async {
        while let r = requested {
            requested = nil
            switch AudioSourceRule.decide(current: current, phase: phase, lastStartAt: lastStartAt, wanted: r.key,
                                          now: HostClock.now()) {
            case .keep:
                // The source there is stays (a stop asked for and taken back before it ran included).
                if r.key != .none { status = sourceStatus }
                continue
            case .stop:
                if !stopSaid, phase == .starting || phase == .running { print("Audio stopped: \(r.reason).") }
                await stopSource()
                current = .none
                phase = .idle
                sourceStatus = nil
            case .start:
                await stopSource()
                await startSource(r.key, make: r.make)
            }
        }
        worker = nil
    }

    /// On the main actor: the source goes, and its chunks with it (a stopped source delivers none, and
    /// a late one is of an old generation). Its callbacks are left as they are: the source reads them on
    /// its own queues, and the generation makes a late call inert (`took`, `ended`).
    @MainActor
    private func stopSource() async {
        generation += 1
        let g = generation
        queue.async { self.live = g; self.dropStreams() }
        guard let s = source else { return }
        source = nil
        await s.stop()
    }

    @MainActor
    private func startSource(_ key: AudioKey, make: @MainActor (AudioKey) async -> Made) async {
        current = key
        phase = .starting
        stopSaid = false
        lastStartAt = HostClock.now()
        sourceStatus = Status(source: Self.shown(key), capturing: false, problem: encoderGone)
        generation += 1
        let g = generation
        // One transition at a time (`run`): nothing else starts or stops a source while this one is made
        // and started. A request that comes meanwhile is decided once this start is done.
        let made = await make(key)
        guard case .source(let s, let line) = made else {
            if case .failed(let why) = made { failed(key, why) }
            return
        }
        let (formatSource, app) = Self.formatSource(key)
        queue.async {
            self.live = g
            self.info = (formatSource, app)
            self.dropStreams()
            self.formatRefused = false
        }
        s.onPCM = { [weak self] chunk in self?.took(chunk, generation: g) }
        s.onStopped = { [weak self] why in Task { @MainActor in self?.ended(generation: g, why: why) } }
        source = s
        print(line)   // before its first buffer and the epoch's encoder line; a failure says so after it
        do {
            try await s.start()
        } catch {
            guard g == generation else { return }
            failed(key, Self.describe(error))
            await stopSource()   // a new generation: what the source still delivers is inert
            return
        }
        guard g == generation else { return }   // it ended by itself meanwhile (`ended` has said so)
        phase = .running
        guard encoderGone == nil else { return }   // no sound this run: the status says why
        sourceStatus = Status(source: Self.shown(key), capturing: true, problem: nil)
    }

    /// A source that could not start: one line, the problem shown, and the key kept (`.ended`), so the
    /// same source is tried again only after `AudioSourceRule.retryInterval`.
    @MainActor
    private func failed(_ key: AudioKey, _ why: String) {
        phase = .ended
        print("Audio: could not capture \(Self.said(key)): \(why). The picture is unaffected.")
        sourceStatus = Status(source: Self.shown(key), capturing: false, problem: "couldn’t capture \(Self.soundOf(key)): \(why)")
    }

    /// The source ended by itself (the permission revoked, the system stopping the stream).
    @MainActor
    private func ended(generation g: Int, why: String) {
        guard g == generation, phase == .running || phase == .starting else { return }
        phase = .ended
        source = nil
        print("Audio capture ended: \(why). The picture is unaffected.")
        sourceStatus = Status(source: Self.shown(current), capturing: false, problem: "couldn’t capture \(Self.soundOf(current)): \(why)")
        generation += 1
        let next = generation
        queue.async { self.live = next; self.dropStreams() }
    }


    /// An error in one line: its own words for Sill's errors, else the system's with its domain and
    /// code (ScreenCaptureKit's −3801 for a missing permission, −3818 for sound that failed to start).
    static func describe(_ error: Error) -> String {
        if let e = error as? AudioSourceFailure { return e.description }
        let e = error as NSError
        return "\(e.localizedDescription) (\(e.domain) \(e.code))"
    }

    /// The Mac's status: "Safari", "Whole Mac", "Test Tone".
    static func shown(_ key: AudioKey) -> String {
        switch key {
        case .app(_, let name): return name
        case .desktop: return "Whole Mac"
        case .test: return "Test Tone"
        case .none: return ""
        }
    }

    /// The log's words: "Safari's sound", "the whole Mac's sound", "the test tone".
    static func said(_ key: AudioKey) -> String {
        switch key {
        case .app(_, let name): return "\(name)'s sound"
        case .desktop: return "the whole Mac's sound"
        case .test: return "the test tone"
        case .none: return "no sound"
        }
    }

    /// The Mac's and the device's words: "the sound of Safari", "the sound of the whole Mac".
    static func soundOf(_ key: AudioKey) -> String {
        switch key {
        case .app(_, let name): return "the sound of \(name)"
        case .desktop: return "the sound of the whole Mac"
        case .test: return "the test tone"
        case .none: return "no sound"
        }
    }

    private static func formatSource(_ key: AudioKey) -> (String, String?) {
        switch key {
        case .app(_, let name): return (AudioFormat.sourceApp, SafeText.label(name))
        case .desktop: return (AudioFormat.sourceDesktop, nil)
        case .test, .none: return (AudioFormat.sourceTest, nil)
        }
    }

    // MARK: The sound (on `queue`)

    /// A chunk from the source of generation `g`: into each codec's packetizer, each block it completes
    /// through the AAC-ELD encoder or as plain `pcm-s16le`, each packet stamped and handed to the server.
    /// The first chunk of a source, or of a new format, starts each codec's stream (its epoch, its format
    /// message); a codec no device plays is not made (`setCodecs`).
    private func took(_ chunk: PCMChunk, generation g: Int) {
        guard g == live else { return }
        let makesAAC = codecs.contains(AudioCodec.aacELD) && !encoderUnavailable
        let makesPCM = codecs.contains(AudioCodec.pcmS16LE)
        guard makesAAC || makesPCM else { return }
        Stats.shared.bump("aud.in")
        guard takes(chunk) else { return }
        if !makesAAC { aac = nil; encoder = nil }
        if !makesPCM { pcm = nil }
        // Another format than the streams' (its rate or channels): each starts again, a new epoch.
        if let running = aac ?? pcm, !running.packetizer.takes(chunk) { dropStreams() }
        if makesAAC, aac == nil { startAAC(for: chunk) }
        if makesPCM, pcm == nil { startPCM(for: chunk) }
        let countsGaps = aac != nil ? AudioCodec.aacELD : AudioCodec.pcmS16LE   // one stream's gaps, not both
        if var s = aac, let enc = encoder {
            for block in push(chunk, into: &s, countsGaps: countsGaps == s.codec) {
                if block.segmentStart { enc.reset() }
                guard let packet = enc.encode(block.samples) else { continue }
                send(packet, of: block, in: &s)
            }
            aac = s
        }
        if var s = pcm {
            for block in push(chunk, into: &s, countsGaps: countsGaps == s.codec) {
                send(PCMPacket.s16le(block.samples), of: block, in: &s)
            }
            pcm = s
        }
    }

    /// Whether the chunk is 48 kHz stereo, which both codecs take (the capture asks for it, and PCMLayout
    /// makes every buffer stereo); another is refused with one line and the Mac's status.
    private func takes(_ chunk: PCMChunk) -> Bool {
        guard chunk.sampleRate == AudioEncoder.sampleRate, chunk.channels == AudioEncoder.channels else {
            if !formatRefused {
                formatRefused = true
                let why = "the capture delivered \(Int(chunk.sampleRate)) Hz with \(chunk.channels) channel\(chunk.channels == 1 ? "" : "s"), and Sill encodes 48 kHz stereo only"
                print("Audio: \(why).")
                Task { @MainActor in self.formatProblem(why) }
            }
            return false
        }
        return true
    }

    /// The blocks a stream's packetizer completes with this chunk, and its new gaps counted (`aud.gap`).
    private func push(_ chunk: PCMChunk, into s: inout CodecStream, countsGaps: Bool) -> [AudioBlock] {
        let blocks = s.packetizer.push(chunk)
        if s.packetizer.gaps > s.gapsSeen {
            if countsGaps { Stats.shared.bump("aud.gap") }
            s.gapsSeen = s.packetizer.gaps
        }
        return blocks
    }

    /// One packet of a block to the server, stamped with the wall clock of its first decoded sample.
    private func send(_ packet: Data, of block: AudioBlock, in s: inout CodecStream) {
        Stats.shared.bump("aud.out")
        // The host clock's time to the wall clock, as the video's stamps are (StreamMessage): at the
        // moment it goes, so a step of the Mac's wall clock shows in the sound as in the picture.
        let stamp = Date().timeIntervalSince1970 - (HostClock.now() - block.hostTime)
        let payload = AudioPackets(epoch: s.epoch, seq: s.seq, segmentStart: block.segmentStart, packets: [packet]).serialized()
        s.seq &+= 1
        onPackets?(StreamMessage(kind: .audio, timestamp: stamp, isKeyframe: false, payload: payload).serialized(), s.epoch, s.codec)
    }

    /// The next epoch's number, either codec's.
    private func nextEpoch() -> Int {
        epoch = (epoch + 1) & 0xFFFF
        return epoch
    }

    /// AAC-ELD's stream for `chunk`'s format: its encoder (the packet size from this first chunk), its
    /// packetizer, its epoch and its format message to the server. None when AAC-ELD cannot be made (said
    /// once; the Mac's status says why, and a pcm-s16le device still hears the sound).
    private func startAAC(for chunk: PCMChunk) {
        let packet = AudioPacketizer.packetSize(firstChunkFrames: chunk.frames)
        let enc: AudioEncoder
        do {
            enc = try AudioEncoder(framesPerPacket: packet)
        } catch {
            encoderUnavailable = true
            print("Audio encoder: AAC-ELD unavailable: \(error)")
            Task { @MainActor in self.encoderProblem("\(error)") }
            return
        }
        let e = nextEpoch()
        encoder = enc
        aac = CodecStream(codec: AudioCodec.aacELD, epoch: e,
                          packetizer: AudioPacketizer(framesPerPacket: enc.framesPerPacket, priming: enc.priming, channels: chunk.channels,
                                                      sampleRate: chunk.sampleRate))
        let format = AudioFormat(codec: AudioCodec.aacELD, sampleRate: Int(AudioEncoder.sampleRate), channels: AudioEncoder.channels,
                                 framesPerPacket: enc.framesPerPacket, primingFrames: enc.priming, bitrate: enc.bitrate, epoch: e,
                                 cookie: enc.cookie, source: info.source, app: info.app)
        let ms = String(format: "%.1f", Double(enc.framesPerPacket) / AudioEncoder.sampleRate * 1000)
        print("Audio encoder: AAC-ELD 48 kHz stereo, \(enc.framesPerPacket) frames a packet (\(ms) ms), \(enc.bitrate / 1000) kbps, codec delay \(enc.priming) frames.")
        onFormat?(StreamMessage(kind: .audio, timestamp: Date().timeIntervalSince1970, isKeyframe: false,
                                payload: AudioMessage.format(format).serialized()).serialized(), e, AudioCodec.aacELD)
    }

    /// pcm-s16le's stream for `chunk`'s format (docs/coherence-protocol.md): `AudioCodec.pcmFramesPerPacket`
    /// frames a packet whatever the capture's chunks, no priming, no cookie, no encoder; its epoch and its
    /// format message to the server.
    private func startPCM(for chunk: PCMChunk) {
        let e = nextEpoch()
        let frames = AudioCodec.pcmFramesPerPacket
        pcm = CodecStream(codec: AudioCodec.pcmS16LE, epoch: e,
                          packetizer: AudioPacketizer(framesPerPacket: frames, priming: 0, channels: chunk.channels, sampleRate: chunk.sampleRate))
        let bitrate = PCMPacket.bitrate(sampleRate: chunk.sampleRate, channels: chunk.channels)
        let format = AudioFormat(codec: AudioCodec.pcmS16LE, sampleRate: Int(chunk.sampleRate), channels: chunk.channels,
                                 framesPerPacket: frames, primingFrames: 0, bitrate: bitrate, epoch: e, source: info.source, app: info.app)
        let ms = String(format: "%.1f", Double(frames) / chunk.sampleRate * 1000)
        print("Audio: pcm-s16le \(Int(chunk.sampleRate) / 1000) kHz stereo, \(frames) frames a packet (\(ms) ms), \(bitrate / 1000) kbps, no encoder.")
        onFormat?(StreamMessage(kind: .audio, timestamp: Date().timeIntervalSince1970, isKeyframe: false,
                                payload: AudioMessage.format(format).serialized()).serialized(), e, AudioCodec.pcmS16LE)
    }

    @MainActor
    private func encoderProblem(_ why: String) {
        let problem = "couldn’t encode the sound: AAC-ELD is unavailable (\(why))"
        encoderGone = problem
        guard var s = sourceStatus else { return }
        s.capturing = false
        s.problem = problem
        sourceStatus = s
    }

    @MainActor
    private func formatProblem(_ why: String) {
        guard var s = sourceStatus else { return }
        s.capturing = false
        s.problem = "couldn’t use the sound: \(why)"
        sourceStatus = s
    }
}
