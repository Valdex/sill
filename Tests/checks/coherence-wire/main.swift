import Foundation

// The coherence wire (docs/coherence-protocol.md), against Sources/StreamProtocol's Foundation-only files
// compiled into this binary (run.sh lists them, so the check runs on Linux too): the kinds' numbers, the
// contract's example payloads decoded field by field, every new payload round-tripped, the fields added to
// existing payloads left out when nil (the old payloads' bytes unchanged) and skipped by an older reader,
// pull request #66's binary layouts (kinds 30, 31, 34) and its kind 8 with a `window`.
var failures = 0
var checks = 0
func check(_ ok: Bool, _ what: @autoclosure () -> String, line: Int = #line) {
    checks += 1
    if !ok { failures += 1; print("FAIL (line \(line)): \(what())") }
}
let sorted = JSONEncoder(); sorted.outputFormatting = .sortedKeys
func json<T: Encodable>(_ x: T) -> String { String(decoding: try! sorted.encode(x), as: UTF8.self) }
func data(_ s: String) -> Data { Data(s.utf8) }
func object(_ d: Data) -> NSDictionary? { (try? JSONSerialization.jsonObject(with: d)) as? NSDictionary }

// MARK: - Kinds

let kinds: [(StreamMessageKind, UInt8)] = [
    (.sideStreams, 29), (.sideParameterSets, 30), (.sideFrame, 31), (.fileChunk, 34), (.fileDrop, 35), (.dragOffer, 36),
    (.fileRequest, 37), (.liveDrag, 38), (.dockMenu, 39), (.pickDisplay, 40), (.takenBack, 41), (.rebuildDesk, 42),
    (.audio, 43), (.keyframeRequest, 44), (.windowState, 45), (.placeWindow, 46), (.dataTicket, 48), (.dataHello, 49),
]
for (kind, raw) in kinds { check(kind.rawValue == raw && StreamMessageKind(rawValue: raw) == kind, "kind \(raw) is \(kind)") }
for raw: UInt8 in [33, 47, 50] {
    let header = StreamMessage.parseHeader(Data([raw]) + Data(count: 9) + Data([0, 0, 0, 0]))
    check(StreamMessageKind(rawValue: raw) == nil && header?.kind == .unknown, "kind \(raw) is free: an older reader's skip")
}

// MARK: - Capabilities (WindowList, Hello)

var list = WindowList(macName: "Mac mini", windows: [], active: .none)
let plainList = json(list)
check(plainList == #"{"active":{"none":{}},"macName":"Mac mini","windows":[]}"#, "a list without the new fields is as before: \(plainList)")
list.sideStreams = 6; list.fileDrop = 1; list.liveDrag = 1; list.windowInput = 1
list.coherence = 1; list.data = 1; list.documents = 1; list.iconSize = 256; list.middleButton = 1; list.rebuildDesk = 1
list.displays = [DisplayInfo(id: 1, name: "Built-in", width: 1728, height: 1117, main: true)]; list.desktopDisplay = 1
let caps = json(list)
for key in [#""sideStreams":6"#, #""fileDrop":1"#, #""liveDrag":1"#, #""windowInput":1"#, #""coherence":1"#, #""data":1"#,
            #""documents":1"#, #""iconSize":256"#, #""middleButton":1"#, #""rebuildDesk":1"#, #""desktopDisplay":1"#] {
    check(caps.contains(key), "the list carries \(key): \(caps)")
}
check(!caps.contains("dockMenu") && !caps.contains("badges") && !caps.contains(#""audio""#),
      "dockMenu, badges and #66's audio are left out when nil")
check(Wire.decode(WindowList.self, from: Wire.encode(list)).map {
    $0.sideStreams == 6 && $0.fileDrop == 1 && $0.windowInput == 1 && $0.coherence == 1 && $0.iconSize == 256 && $0.displays?.first?.main == true
} == true, "the capabilities round-trip")
check(Wire.decode(WindowList.self, from: data(#"{"macName":"M","windows":[],"active":{"desktop":{}},"fileDrop":true}"#)) == nil,
      "#66's Bool flags do not decode here (Ints), as main's clipboard already said")
struct OldList: Decodable { var macName: String; var windows: [WindowInfo]; var active: StreamSource }
check(Wire.decode(OldList.self, from: Wire.encode(list))?.macName == "Mac mini", "an older reader skips them")

let hello = Hello(appVersion: "1.0", audio: ["pcm-s16le"], coherence: 1, iconSize: 256)
check(json(hello) == #"{"appVersion":"1.0","audio":["pcm-s16le"],"coherence":1,"iconSize":256}"#, "the hello's coherence and iconSize: \(json(hello))")
check(json(Hello(appVersion: "1.0")) == #"{"appVersion":"1.0"}"#, "a hello without them is as before")
check(Hello.maxIconSize == 512, "icons at most 512 px")
check(AudioCodec.choose(offered: hello.audio) == "pcm-s16le", "a device that lists pcm-s16le gets it")
check(AudioCodec.choose(offered: ["aac-eld"]) == "aac-eld", "an Apple device still gets aac-eld")
check(AudioCodec.pcmS16LE == "pcm-s16le" && AudioCodec.pcmFramesPerPacket == 480, "pcm-s16le: 480 frames a packet")

// MARK: - Kind 29

let doc29 = #"{"windows":[4711,4712], "fps":30, "wholeApp":true, "screen":[2560,1400], "coherence":true, "focus":4711, "focusFPS":60, "apps":["com.apple.Safari"], "all":false, "hidden":[4712], "scale":1.25}"#
if let r = Wire.decode(SideStreamRequest.self, from: data(doc29)) {
    check(r.windows == [4711, 4712] && r.fps == 30 && r.wholeApp == true && r.screen == [2560, 1400], "kind 29's #66 fields")
    check(r.coherence == true && r.focus == 4711 && r.focusFPS == 60 && r.apps == ["com.apple.Safari"] && r.all == false
          && r.hidden == [4712] && r.scale == 1.25, "kind 29's coherence fields")
} else { check(false, "the contract's kind 29 decodes") }
check(json(SideStreamRequest(windows: [7])) == #"{"windows":[7]}"#, "a kind 29 of #66's shape carries none of them")
check(Wire.decode(SideStreamRequest.self, from: data(#"{"windows":[1],"audio":true,"audioCodec":"aac-eld"}"#))?.windows == [1],
      "#66's kind 33 fields are skipped")
check(SideStreamRequest.defaultFPS == 15 && SideStreamRequest.maxFPS == 30 && SideStreamRequest.defaultFocusFPS == 60
      && SideStreamRequest.maxFocusFPS == 120, "the rates: 15 and 30 beside, 60 and 120 in front")

// MARK: - Kinds 30, 31, 34 (binary)

let packed = SideStreamRequest.pack(window: 0x0102_0304, Data([9, 8]))
check(packed == Data([1, 2, 3, 4, 9, 8]), "a side stream's payload: the window ID big-endian, then the rest")
check(SideStreamRequest.unpack(packed).map { $0.window == 0x0102_0304 && $0.payload == Data([9, 8]) } == true, "and back")
check(SideStreamRequest.unpack((Data([0xFF]) + packed).dropFirst()).map { $0.window == 0x0102_0304 && $0.payload == Data([9, 8]) } == true,
      "a slice (its start past 0) reads from its own start")
check(SideStreamRequest.unpack(Data([1, 2, 3])) == nil, "under 4 bytes: nothing")
let chunk = FileChunk.pack(id: 42, name: "Résumé ✓.pdf", total: 1_000_000, offset: 524_288, bytes: Data([9, 8, 7]))
let nameBytes = Array("Résumé ✓.pdf".utf8)
check(Array(chunk.prefix(6)) == [0, 0, 0, 42, 0, UInt8(nameBytes.count)] && Array(chunk[6..<(6 + nameBytes.count)]) == nameBytes,
      "kind 34: the ID, the name's length and the name")
check(FileChunk.unpack(chunk).map { $0.id == 42 && $0.name == "Résumé ✓.pdf" && $0.total == 1_000_000 && $0.offset == 524_288 && $0.bytes == Data([9, 8, 7]) } == true,
      "kind 34 round-trips")
check(FileChunk.unpack(chunk.prefix(10)) == nil, "kind 34 cut inside the name: nothing")
check(FileChunk.maxBytes == 512 * 1024 && FileChunk.maxBytes + 2_048 < StreamMessage.maxClientPayload, "a part fits a device's message")

// MARK: - Kinds 35–38, 41

let doc35 = #"{"files":[1,2], "x":0.4, "y":0.6, "window":4711, "folders":[2], "offer":9}"#
check(Wire.decode(FileDrop.self, from: data(doc35)) == FileDrop(files: [1, 2], x: 0.4, y: 0.6, offer: 9, window: 4711, folders: [2]), "the contract's kind 35")
let doc36 = #"{"id":9, "files":[{"name":"a.pdf","size":1048576,"folder":false}]}"#
check(Wire.decode(DragOffer.self, from: data(doc36)) == DragOffer(id: 9, files: [.init(name: "a.pdf", size: 1_048_576, folder: false)]), "the contract's kind 36")
let doc37 = #"{"offer":9, "index":0, "transfer":31, "trash":false}"#
check(Wire.decode(FileRequest.self, from: data(doc37)) == FileRequest(offer: 9, index: 0, transfer: 31, trash: false), "the contract's kind 37")
let doc38 = #"{"id":5, "phase":"move", "window":4711, "x":0.5, "y":0.5, "modifiers":0, "offer":9, "files":[1], "folders":[]}"#
check(Wire.decode(LiveDrag.self, from: data(doc38)) == LiveDrag(id: 5, phase: LiveDrag.move, window: 4711, x: 0.5, y: 0.5, modifiers: 0,
                                                                offer: 9, files: [1], folders: []), "the contract's kind 38")
check([LiveDrag.start, LiveDrag.filesSent, LiveDrag.move, LiveDrag.drop, LiveDrag.cancel] == ["start", "files", "move", "drop", "cancel"], "kind 38's phases")
check(Wire.decode(LiveDrag.self, from: data(#"{"id":8,"phase":"later","x":0,"y":0}"#))?.phase == "later", "a phase a host does not know still decodes")
check(Wire.decode(TakenBack.self, from: data(#"{"windows":[4711]}"#))?.windows == [4711], "the contract's kind 41")
check(Wire.decode(PickDisplay.self, from: Wire.encode(PickDisplay(display: 5)))?.display == 5, "kind 40 round-trips")
check(DesktopWindow.id(display: 3) == 0x8000_0003 && DesktopWindow.display(of: 0x8000_0003) == 3 && DesktopWindow.display(of: 4711) == nil,
      "a Desktop window's ID: the display with the top bit")

// MARK: - Kind 8 with a window

let scroll = InputEvent.scroll(x: 0.25, y: 0.5, dx: 0, dy: 0.1)
let aimed = WindowInput(window: 4711, event: scroll).encoded()
check(object(aimed)?["window"] as? Int == 4711 && object(aimed)?["scroll"] != nil && object(aimed)?.count == 2, "kind 8 with `window` beside the event's key")
check(WindowInput.decode(aimed) == WindowInput(window: 4711, event: scroll), "and back")
check(WindowInput.decode(Wire.encode(scroll)) == WindowInput(window: nil, event: scroll), "a plain kind 8 has no window")
let click = InputEvent.pointer(.leftDown, x: 0.5, y: 0.5)
check(WindowInput.decode(data(#"{"pointer":{"_0":"leftDown","x":0.5,"y":0.5},"window":7}"#)) == WindowInput(window: 7, event: click),
      "a click aimed at a window (generation 2) decodes")
check(WindowInput.decode(data(#"{"scroll":{"x":0,"y":0,"dx":0,"dy":1},"window":5,"extra":1}"#)) == nil
      && WindowInput.decode(data(#"{"scroll":{"x":0,"y":0,"dx":0,"dy":1},"window":"5"}"#)) == nil, "a third key, or a window that is no number: nothing")
check(WindowInput.allowed(scroll) && WindowInput.allowed(.pointer(.move, x: 0, y: 0)) && !WindowInput.allowed(click),
      "generation 1 takes moves and scrolls only")
check(WindowInputGeneration.movesAndScrolls == 1 && WindowInputGeneration.every == 2, "windowInput's generations")
check(json(InputEvent.pointer(.middleDown, x: 0.5, y: 0.25)).contains("middleDown") && PointerAction.middleDown.isButtonDown
      && !PointerAction.middleUp.isButtonDown && PointerAction.leftDown.rawValue == "leftDown", "the middle button; the old spellings kept")

// MARK: - Kind 44

check(KeyframeRequest.window(in: Data()) == nil, "an empty kind 44: the picked stream's")
check(KeyframeRequest.window(in: data(#"{"window":4711}"#)) == 4711, "a kind 44 naming a window")
check(KeyframeRequest.window(in: data("{}")) == nil && KeyframeRequest.window(in: data("x")) == nil, "{} or nonsense: the picked stream's")

// MARK: - Kind 45

let doc45 = #"""
{"gen":812, "full":false, "answering":7,
 "windows":[
   {"id":4711, "app":"com.apple.Safari", "appName":"Safari", "title":"Apple", "role":"window",
    "size":[1280,800], "min":[500,300], "resizable":true, "focused":true, "minimized":false,
    "fullScreen":false, "titleBar":28, "joined":true,
    "controls":{"close":[8,8,12,12], "minimize":[28,8,12,12], "zoom":[48,8,12,12]}},
   {"id":4790, "app":"com.apple.Safari", "role":"menu", "owner":4711, "rect":[40,22,220,310],
    "inCrop":true}],
 "removed":[4650], "order":[4790,4711], "focus":4711, "secureInput":false}
"""#
if let w = Wire.decode(WindowStateMessage.self, from: data(doc45)) {
    check(w.gen == 812 && w.full == false && w.answering == 7 && w.removed == [4650] && w.order == [4790, 4711] && w.focus == 4711
          && w.secureInput == false, "kind 45's message fields")
    let a = w.windows?.first, b = w.windows?.last
    check(a?.id == 4711 && a?.app == "com.apple.Safari" && a?.appName == "Safari" && a?.title == "Apple" && a?.role == "window"
          && a?.size == [1280, 800] && a?.min == [500, 300] && a?.max == nil && a?.resizable == true && a?.focused == true
          && a?.minimized == false && a?.fullScreen == false && a?.titleBar == 28 && a?.joined == true, "a top-level window's fields")
    check(a?.controls == WindowControls(close: [8, 8, 12, 12], minimize: [28, 8, 12, 12], zoom: [48, 8, 12, 12]), "its controls")
    check(b?.id == 4790 && b?.role == "menu" && b?.owner == 4711 && b?.rect == [40, 22, 220, 310] && b?.inCrop == true, "a child's fields")
    check(Wire.decode(WindowStateMessage.self, from: Wire.encode(w)) == w, "kind 45 round-trips")
} else { check(false, "the contract's kind 45 decodes") }
check(json(WindowStateMessage(gen: 1)) == #"{"gen":1}"#, "only gen when nothing else")
check(Wire.decode(WindowStateMessage.self, from: data(#"{"full":true}"#)) == nil, "gen is always there")
check(WindowStateEntry.roles == ["window", "sheet", "dialog", "panel", "menu", "popover", "tooltip"], "the roles")
check(WindowStateEntry.knownRole("drawer") == "window" && WindowStateEntry.knownRole(nil) == "window" && WindowStateEntry.knownRole("sheet") == "sheet",
      "a role a device does not know is a window")

// MARK: - Kind 46

check(Wire.decode(PlaceWindow.self, from: data(#"{"id":4711, "size":[1100,760], "token":7}"#)) == PlaceWindow(id: 4711, size: [1100, 760], token: 7),
      "the contract's kind 46")
check(json(PlaceWindow(id: 1, size: [2, 3])) == #"{"id":1,"size":[2,3]}"#, "a kind 46 without a token")
check(PlaceWindow.maxPerSecond == 20, "about 20 a second during a resize")

// MARK: - Kinds 48, 49

let token = Data((0..<DataTicket.tokenBytes).map { UInt8($0) }).base64EncodedString()
check(json(DataTicket(token: token)) == "{\"token\":\"\(token)\"}" && Wire.decode(DataHello.self, from: Wire.encode(DataHello(token: token)))?.token == token,
      "kinds 48 and 49: the token")
check(DataHello.alpn == "sill-data/1" && DataHello.deadline == 4 && DataHello.maxPayload == 1_048_576 + 64, "the data connection's ALPN, deadline and cap")

// MARK: - Kind 15, kind 7

check(json(WindowCommand(id: 4711, action: .activate)) == #"{"action":"activate","id":4711}"#
      && json(WindowCommand(id: 4711, action: .restore)) == #"{"action":"restore","id":4711}"#, "kind 15's activate and restore")
check(Wire.decode(WindowCommand.self, from: data(#"{"id":1,"action":"close"}"#))?.action == .close, "the old actions as before")
check(Wire.decode(WindowCommand.self, from: data(#"{"id":1,"action":"later"}"#)) == nil, "an action this build does not know: dropped whole")
check(json(LaunchApp(bundleID: "com.apple.TextEdit")) == #"{"bundleID":"com.apple.TextEdit"}"#, "kind 7 without documents is as before")
check(json(LaunchApp(bundleID: "", documents: ["/Users/a/b.txt"])) == #"{"bundleID":"","documents":["\/Users\/a\/b.txt"]}"#,
      "kind 7 with documents")
check(Wire.decode(LaunchApp.self, from: data(#"{"bundleID":"","documents":["/Users/a/b.txt"]}"#)).map { $0.bundleID == "" && $0.documents == ["/Users/a/b.txt"] } == true,
      "kind 7's documents")

// MARK: - Kind 32's generations

check(json(Clipboard(text: "Hello")) == #"{"text":"Hello"}"# && json(Clipboard()) == "{}", "generation 1's payloads as before")
let rich = Clipboard(text: "a", image: Data([1, 2, 3]), accepts: 3, html: "<b>a</b>", rtf: Data([4, 5]),
                     files: ClipboardFiles(offer: 12, files: [.init(name: "a.pdf", size: 1_048_576)]))
check(json(rich) == #"{"accepts":3,"files":{"files":[{"name":"a.pdf","size":1048576}],"offer":12},"html":"<b>a<\/b>","image":"AQID","rtf":"BAU=","text":"a"}"#,
      "generations 2 and 3: \(json(rich))")
check(Wire.decode(Clipboard.self, from: Wire.encode(rich)) == rich, "and back")
check(Wire.decode(Clipboard.self, from: data(#"{"files":{"transfers":[3,4]}}"#))?.files?.transfers == [3, 4], "a device's files: their transfer IDs")
check(json(Clipboard(accepts: 2)) == #"{"accepts":2}"#, "a subscription that takes generation 2")
check(json(Clipboard(text: "pw", concealed: true)) == #"{"concealed":true,"text":"pw"}"#, "a concealed item from the host: `concealed` true")
check(!json(Clipboard(text: "pw")).contains("concealed") && Wire.decode(Clipboard.self, from: data(#"{"text":"a"}"#))?.concealed == nil,
      "`concealed` left out when nil, and nil from a peer that never sends it")
check(Wire.decode(Clipboard.self, from: data(#"{"text":"a","concealed":true}"#))?.concealed == true, "`concealed` read back")
check(ClipboardGeneration.text == 1 && ClipboardGeneration.rich == 2 && ClipboardGeneration.files == 3 && CoherenceGeneration.current == 1, "the generations")

// MARK: - Kind 43's pcm-s16le format

let pcm = AudioFormat(codec: AudioCodec.pcmS16LE, sampleRate: 48_000, channels: 2, framesPerPacket: 480, primingFrames: 0, bitrate: 1_536_000, epoch: 1)
check(json(pcm) == #"{"bitrate":1536000,"channels":2,"codec":"pcm-s16le","epoch":1,"framesPerPacket":480,"primingFrames":0,"sampleRate":48000}"#,
      "a pcm-s16le format: \(json(pcm))")

print(failures == 0 ? "\(checks) checks, 0 failed" : "\(failures) FAILED of \(checks)")
exit(failures == 0 ? 0 : 1)
