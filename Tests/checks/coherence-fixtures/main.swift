// Writes the Coherence messages as Sill for Mac's own types encode them, one JSON object per line
// ({"type": <the C# type that reads it>, "json": <the payload>}), for the Windows client's checks
// (WindowsClient/Sill.Checks, group "swift-wire"): what the Mac sends, the PC must read and write
// back the same. Foundation only: run.sh builds it with StreamProtocol's Foundation files on any system.
import Foundation

var lines: [String] = []
func emit<T: Encodable>(_ type: String, _ value: T) {
    let e = JSONEncoder()
    e.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
    let json = String(data: try! e.encode(value), encoding: .utf8)!
    lines.append("{\"type\":\"\(type)\",\"json\":\(json)}")
}

var req = SideStreamRequest(windows: [4711, 4712], fps: 30, wholeApp: true, screen: [2560, 1400])
req.coherence = true; req.focus = 4711; req.focusFPS = 60
req.apps = ["com.apple.Safari"]; req.all = false; req.hidden = [4712]; req.scale = 1.25
emit("SideStreamRequest", req)
emit("SideStreamRequest", SideStreamRequest(windows: []))

var top = WindowStateEntry(id: 4711)
top.app = "com.apple.Safari"; top.appName = "Safari"; top.title = "Apple — Start"; top.role = WindowStateEntry.window
top.size = [1280, 800]; top.min = [500, 300]; top.resizable = true; top.focused = true; top.minimized = false
top.fullScreen = false; top.titleBar = 28; top.joined = true
top.controls = WindowControls(close: [8, 8, 12, 12], minimize: [28, 8, 12, 12], zoom: [48, 8, 12, 12])
var menu = WindowStateEntry(id: 4790)
menu.app = "com.apple.Safari"; menu.role = WindowStateEntry.menu; menu.owner = 4711; menu.rect = [40, 22, 220, 310]; menu.inCrop = false
var state = WindowStateMessage(gen: 812)
state.full = false; state.answering = 7; state.windows = [top, menu]; state.removed = [4650]; state.order = [4790, 4711]
state.focus = 4711; state.secureInput = false
emit("WindowState", state)
emit("WindowState", WindowStateMessage(gen: 1))

emit("PlaceWindow", PlaceWindow(id: 4711, size: [1100, 760], token: 7))
emit("KeyframeRequest", KeyframeRequest(window: 4711))
emit("DataTicket", DataTicket(token: "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="))
emit("DataHello", DataHello(token: "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="))
emit("TakenBack", TakenBack(windows: [4711, 4712]))
emit("FileDrop", FileDrop(files: [1, 2], x: 0.4, y: 0.6, offer: 9, window: 4711, folders: [2]))
emit("DragOffer", DragOffer(id: 9, files: [DragOffer.File(name: "a.pdf", size: 1_048_576), DragOffer.File(name: "Photos", size: 2048, folder: true)]))
emit("FileRequest", FileRequest(offer: 9, index: 0, transfer: 31, trash: false))
emit("LiveDrag", LiveDrag(id: 5, phase: LiveDrag.move, window: 4711, x: 0.5, y: 0.25, modifiers: 131072, offer: nil, files: [1], folders: []))

var clip = Clipboard(text: "hello")
clip.html = "<b>hello</b>"; clip.rtf = Data("{\\rtf1 hello}".utf8); clip.image = Data([0x89, 0x50, 0x4E, 0x47])
clip.files = ClipboardFiles(offer: 12, files: [DragOffer.File(name: "a.pdf", size: 1_048_576)], transfers: nil)
emit("ClipboardPayload", clip)
emit("ClipboardPayload", Clipboard(text: nil, image: nil, stop: true))
emit("ClipboardPayload", Clipboard(text: "hunter2", concealed: true))
emit("ClipboardPayload", Clipboard(text: "a.pdf", files: ClipboardFiles(offer: 13, files: [DragOffer.File(name: "Photos", size: 0, folder: true)])))

var launch = LaunchApp(bundleID: "com.apple.Preview")
launch.documents = ["/Users/v/Documents/a.pdf"]
emit("LaunchApp", launch)

var hello = Hello(appVersion: "0.5.5", build: "1", protocol: 1, device: "Windows PC", audio: ["pcm-s16le"])
hello.coherence = 1; hello.iconSize = 256
emit("Hello", hello)

print(lines.joined(separator: "\n"))
