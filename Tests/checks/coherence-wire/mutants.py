"""Mutants of the coherence wire (docs/coherence-protocol.md): each copies Sources/StreamProtocol's
Foundation-only files, applies one mutation, compiles them with the check and expects it to fail.
usage: mutants.py WORKTREE"""
import os, re, shutil, subprocess, sys, tempfile
WT = sys.argv[1]
HERE = os.path.dirname(os.path.abspath(__file__))
MUTANTS = [
    ("kind 45 numbered 47", "StreamMessage.swift", "case windowState = 45", "case windowState = 47"),
    ("kind 49 numbered 50", "StreamMessage.swift", "case dataHello = 49", "case dataHello = 50"),
    ("kind 38 numbered 33", "StreamMessage.swift", "case liveDrag = 38", "case liveDrag = 33"),
    ("pcm-s16le misspelt", "Audio.swift", 'public static let pcmS16LE = "pcm-s16le"', 'public static let pcmS16LE = "pcm-s16"'),
    ("the host makes no pcm", "Audio.swift", "public static let hostMakes = [aacELD, pcmS16LE]", "public static let hostMakes = [aacELD]"),
    ("pcm packets of 512", "Audio.swift", "public static let pcmFramesPerPacket = 480", "public static let pcmFramesPerPacket = 512"),
    ("the side stream's ID little-endian", "SideStreams.swift", "d.appendBigEndian(window)", "d.append(contentsOf: withUnsafeBytes(of: window.littleEndian) { Array($0) })"),
    ("a slice read from 0", "SideStreams.swift", "d.subdata(in: (d.startIndex + 4)..<d.endIndex))\n    }\n}", "d.subdata(in: 4..<d.endIndex))\n    }\n}"),
    ("a windowed kind 8 with a third key", "SideStreams.swift", "guard object.count == 2,", "guard object.count >= 2,"),
    ("generation 1 takes clicks", "SideStreams.swift", "case .pointer(.move, _, _), .scroll, .scrollGesture: return true", "case .pointer, .scroll, .scrollGesture: return true"),
    ("kind 44's window never read", "Coherence.swift", "return Wire.decode(KeyframeRequest.self, from: payload)?.window", "return nil"),
    ("an unknown role kept", "Coherence.swift", "guard let role, roles.contains(role) else { return window }", "guard let role else { return window }"),
    ("gen optional", "Coherence.swift", "    public var gen: Int\n", "    public var gen: Int?\n"),
    ("the data ALPN misspelt", "Coherence.swift", 'public static let alpn = "sill-data/1"', 'public static let alpn = "sill-data"'),
    ("WindowInput every as 1", "Coherence.swift", "public static let every = 2", "public static let every = 1"),
    ("the hello's iconSize not kept", "Compatibility.swift", "self.iconSize = iconSize", "self.iconSize = nil"),
    ("activate spelt activateWindow", "Switcher.swift", "case close, minimize, fullScreen, activate, restore", "case close, minimize, fullScreen, activate = \"activateWindow\", restore"),
    ("documents not kept", "Switcher.swift", "self.bundleID = bundleID; self.documents = documents", "self.bundleID = bundleID; self.documents = nil"),
    ("the clipboard's html not kept", "Clipboard.swift", "self.html = html;", "self.html = nil;"),
    ("concealed not kept", "Clipboard.swift", "self.concealed = concealed", "self.concealed = nil"),
    ("a Desktop window's bit 30", "Switcher.swift", "public static let bit: UInt32 = 0x8000_0000", "public static let bit: UInt32 = 0x4000_0000"),
    ("middle counted as no press", "Input.swift", "|| self == .middleDown }", "}"),
]
NOT_FOUNDATION = re.compile(r"^import (Network|Security|CryptoKit|CommonCrypto)", re.M)
caught = 0
for name, file, old, new in MUTANTS:
    with tempfile.TemporaryDirectory() as t:
        src = os.path.join(t, "StreamProtocol"); shutil.copytree(os.path.join(WT, "Sources/StreamProtocol"), src)
        path = os.path.join(src, file); text = open(path).read()
        if text.count(old) != 1:
            print(f"{name}: NOT APPLIED (pattern found {text.count(old)} times)"); continue
        open(path, "w").write(text.replace(old, new, 1))
        files = [os.path.join(src, f) for f in sorted(os.listdir(src))
                 if f.endswith(".swift") and not NOT_FOUNDATION.search(open(os.path.join(src, f)).read())]
        exe = os.path.join(t, "check")
        c = subprocess.run(["swiftc", "-O", *files, os.path.join(HERE, "main.swift"), "-o", exe], capture_output=True, text=True)
        if c.returncode != 0:
            print(f"{name}: did not compile\n{c.stderr[:400]}"); continue
        r = subprocess.run([exe], capture_output=True, text=True, timeout=300)
        failed = [l for l in r.stdout.splitlines() if l.startswith("FAIL")]
        if r.returncode != 0:
            caught += 1
            print(f"{name}: caught ({failed[0][:90] if failed else f'exit status {r.returncode}'})")
        else:
            print(f"{name}: NOT CAUGHT")
print(f"mutants caught: {caught} of {len(MUTANTS)}")
sys.exit(0 if caught == len(MUTANTS) else 1)
