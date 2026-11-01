# Coherence: the wire contract (this fork)

The messages this fork adds so that a Windows PC shows several Mac windows at once, each as a window
of its own, with files, a richer clipboard and sound. `docs/coherence-plan.md` has why and in what
order. Everything here is additive and follows `Sources/StreamProtocol/Compatibility.swift`'s rules:
JSON for control messages, every field optional and left out when nil, strings instead of enums on
the wire, a field never renamed or retyped, kinds an older peer does not know skipped by it, and a
peer sends a kind or a field only to a peer that said it takes it (a generation `Int` in `WindowList`
from the host, in `Hello` from the device). No `SillProtocol` bump, no new session ALPN.

Kinds 29–31 and 34–42 keep the meanings upstream's pull request #66 (Sill Viewer, a Mac client) gives
them, so this fork and an eventual upstream merge of #66 agree. Kind 33 (#66's sound) is not used:
main's sound is kind 43. Kinds 45 and up are this fork's; they should be reserved upstream before
upstream uses them for something else.

## Capabilities

`WindowList` (host → device) gains, each an `Int`, nil from hosts without it:

| Field | Value | Meaning |
|---|---|---|
| `sideStreams` | n ≥ 1 | the host streams up to n windows besides the picked one (kind 29), as #66 |
| `coherence` | 1 | kinds 45 and 46, kind 29's `coherence`, `focus`, `focusFPS`, `apps`, `all`, `hidden`, `scale`; kind 15's `activate` and `restore`; kind 44's `window` |
| `windowInput` | 2 | every kind 8 event may carry `window` (1, #66's, was moves and scrolls only) |
| `fileDrop` | 1 | kinds 34, 35, 36, 37 |
| `liveDrag` | 1 | kind 38 |
| `data` | 1 | kind 48 and the `sill-data/1` connection |
| `documents` | 1 | `LaunchApp.documents` |
| `clipboard` | 1, 2, 3 | 1 text (main); 2 adds `image`, `html`, `rtf`, `concealed`; 3 adds `files` |
| `iconSize` | px | the largest icon the host draws for kind 4 when the hello asks (`Hello.iconSize`) |

`Hello` (device → host) gains `coherence: Int?` (1), `iconSize: Int?` (pixels, at most 512; the host
draws kind 4 at that size, else 64 as before) and lists `"pcm-s16le"` in `audio` when it plays it.

`#66`'s own `Bool` flags (`fileDrop`, `liveDrag`, `windowInput`) are `Int`s here, as main's
`Switcher.swift` already says #66's `clipboard` becomes as it rebases.

## Video: side streams (kinds 29, 30, 31)

**29 `sideStreams`**, device → host, JSON `SideStreamRequest` (#66's fields first):

```json
{"windows":[4711,4712], "fps":30, "wholeApp":true, "screen":[2560,1400],
 "coherence":true, "focus":4711, "focusFPS":60, "apps":["com.apple.Safari"], "all":false,
 "hidden":[4712], "scale":1.25}
```

- `windows`: the host's window IDs (`WindowInfo.id`) to stream. Each gets its own capture and HEVC
  encoder on the host. The whole set is sent each time; a window left out stops.
- `fps`: frames a second for each window but the focused one (default 15, at most 30, #66's rule).
- `wholeApp`: capture the window with what belongs to it (its menus, popovers, sheets, the system's
  panels over it), as #66's `AppCrop`.
- `screen`: the device's largest screen in points; the room the host keeps for each window (Sill's
  desk: a virtual display per window, #66) is at least this big.
- `coherence` (new): true, every listed window is a peer. No window has to be picked with kind 6
  (the device sends kind 6 `none`, or never sends it); the host streams them all.
- `focus` (new): the window the device has in front; streamed at `focusFPS` (default 60, at most
  120). Changing it never restarts a stream: the host only stops dropping that window's frames.
- `apps` (new): bundle IDs whose windows join the set by themselves as they open (each new window
  reported in kind 45 with `"joined":true`), `all` (new): every app's.
- `hidden` (new): windows minimized or fully covered on the device: the host drops their frames
  (one every few seconds at most) and keeps their capture.
- `scale` (new): the device's pixels per point (1.0, 1.25, 1.5, 2…), so the host captures at the
  device's size rather than always at the display's 2×. The host takes at most its display's scale.

**30 `sideParameterSets`**, **31 `sideFrame`**, host → device: the window ID (UInt32, big endian),
then exactly what kinds 0 and 1 carry for that window. The header's keyframe flag is the frame's.
Side frames are dropped on the host, never queued, when the connection is behind.

**44 `keyframeRequest`** gains an optional JSON payload `{"window":4711}` (to a host whose list says
`coherence` 1): only that window's encoder makes a keyframe. An empty payload is the picked stream's,
as before.

## Window state (kinds 45, 46)

**45 `windowState`**, host → device, JSON, modelled on RDP RemoteApp's window orders:

```json
{"gen":812, "full":false, "answering":7,
 "windows":[
   {"id":4711, "app":"com.apple.Safari", "appName":"Safari", "title":"Apple", "role":"window",
    "size":[1280,800], "min":[500,300], "resizable":true, "focused":true, "minimized":false,
    "fullScreen":false, "titleBar":28, "joined":true,
    "controls":{"close":[8,8,12,12], "minimize":[28,8,12,12], "zoom":[48,8,12,12]}},
   {"id":4790, "app":"com.apple.Safari", "role":"menu", "owner":4711, "rect":[40,22,220,310],
    "inCrop":true}],
 "removed":[4650], "order":[4790,4711], "focus":4711, "secureInput":false}
```

- `gen` rises with every message; a device drops one older than the last it applied. `full` true:
  the message lists every window the device should have (sent when coherence starts and after a
  gap); otherwise only windows that changed, and `removed`.
- A window: `id`; `app` (bundle ID) and `appName`; `title`; `role`: `window`, `sheet`, `dialog`,
  `panel`, `menu`, `popover`, `tooltip` (a role a device does not know is a `window`); `size` in
  points (top-level windows: their position on Sill's desk means nothing to the device, which places
  them itself); `min` and `max` in points when known, learned from sizes the window refused;
  `resizable`; `focused`; `minimized`; `fullScreen`; `modal`; `titleBar`, the height in points of a
  plain title bar the device may crop and replace with its own (0 or absent: a unified toolbar, keep
  it); `controls`, the traffic lights' rects in points from the window's top-left, for a device that
  draws the Mac's frame itself; `joined`, the window came by itself (kind 29's `apps` or `all`).
- A child (`owner` set): `rect` is its frame in points relative to its owner's top-left; `inCrop`
  true when it shows inside the owner's picture (`wholeApp`), so the device needs no stream for it;
  false when it spills out and the device should ask for it as a side stream of its own and show it
  as an owned, non-activating window.
- `order`, front to back, and `focus`: the Mac's own stacking and key window, for information; the
  device's stacking is its own.
- `secureInput`: macOS's Secure Event Input is on (a password field), so typed keys may not land.
- `answering`: the `token` of the kind 46 this answers.

**46 `placeWindow`**, device → host, JSON `{"id":4711, "size":[1100,760], "token":7}`: size the
window, in points, through Accessibility. Moves are the device's alone and never sent. The host
answers with a kind 45 carrying the size the window took and `answering`. A device sends it at the
end of a resize (`WM_EXITSIZEMOVE`) and at most about 20 times a second during one. "Maximize" on the
device is a `placeWindow` to its work area, never the Mac's full screen (which opens a Space and
hides the other windows).

**15 `windowCommand`** gains the actions `activate` (bring the window's app forward and make the
window key, as a click would: the device sends it when its own window is activated) and `restore`
(un-minimize on the Mac). Sent only to a host whose list says `coherence` 1.

**8 `input`** with `window` (to a host whose list says `windowInput` 2): the event is aimed at that
window, its coordinates fractions of that window's picture. The host activates the window first when
the event is a click or a key, holding the input until it is key (the host's existing hold).

**41 `takenBack`**, host → device, `{"windows":[4711]}`: the person at the Mac took these windows back
(they went back on the Mac's screen and stopped streaming); the device closes its windows of them.

## Files and drag and drop (kinds 34–38)

As #66 (`SideStreams.swift` there; ported to `Sources/StreamProtocol/SideStreams.swift`):

- **34 `fileChunk`**, either way, binary: transfer ID (UInt32 BE), name (UInt16 BE length, UTF-8), the
  whole size and this part's offset (UInt64 BE each), the bytes (at most 512 KB a part).
- **35 `fileDrop`**, device → host, `{"files":[1,2], "x":0.4, "y":0.6, "window":4711, "folders":[2],
  "offer":9}`: those transfers, all sent, dropped at a point of the window's picture.
- **36 `dragOffer`**, host → device, `{"id":9, "files":[{"name":"a.pdf","size":1048576,"folder":false}]}`:
  an app on the Mac is dragging these files under the device's held button.
- **37 `fileRequest`**, device → host, `{"offer":9, "index":0, "transfer":31, "trash":false}`: send one
  offered file back as kind 34 parts with `transfer` as their ID. Also used for clipboard files
  (below), whose offers share the ID space.
- **38 `liveDrag`**, device → host, `{"id":5, "phase":"start|files|move|drop|cancel", "window":4711,
  "x":0.5, "y":0.5, "modifiers":0, "offer":9, "files":[1], "folders":[]}`: a drag of files over the
  Mac's windows followed live.

A folder travels zipped (`ditto -c -k --sequesterRsrc --keepParent`, #66's `FolderZip`); the receiver
unzips it where it lands.

## The bulk connection (kind 48, ALPN `sill-data/1`)

Large transfers (kinds 34–37's parts, clipboard images and files) would hold up video on the one TCP
connection, so a host whose list says `data` 1 offers a second one:

- **48 `dataTicket`**, host → device on the session, JSON `{"token":"<base64 of 32 random bytes>"}`,
  sent once per session, right after its first window list, to a device whose hello says `coherence` 1
  (and never to a session the Mac has stopped serving). The ticket stays valid while the session lives,
  so a device whose bulk connection goes may dial again with it; it dies with the session.
- The device opens a second connection to the same door and port with ALPN `sill-data/1`, the same
  TLS 1.3 and the same pinned keys as its session (a plain door: plain), and sends **49 `dataHello`**
  `{"token":"…"}` as its first message.
- **Admission.** At a TLS door the verify block trusts `sill-data/1` exactly as `sill/1` (at home a paired
  key, or any key while Require pairing is off; at the remote door a paired key), and at `.ready` it is
  judged again under its session's rules (the remote door: a paired key while Remote Access is on; never
  a goodbye, and never counted against the remote door's session limit). The connection holds one of the
  door's waiting places, as a pairing connection does, until its kind 49: at most 512 bytes, within 4 s
  of `.ready` (`DataHello.deadline`) and 10 s of accept. Anything else first, or nothing in time, is
  refused as an unpaired connection. The kind 49 admits it only for the session holding that token when
  every one of these holds: the same door (home or remote), the same way (TLS, or plain at the plain
  door), the same key (a TLS session's, never a missing one; a plain one presents none), the same
  address (without the port; a link-local address with its interface), the Mac still serves that session,
  and the session has no bulk connection yet. A refused ticket costs the connection's address one failure
  toward the door's backoff, as any refusal does; an admitted one leaves its place as a session does. The
  plain door has no ALPN: it reads a connection's first message before registering it only while a plain
  session from that address holds a ticket and has no bulk connection (a hello there is the session's,
  judged as the device gate judges one; nothing within 4 s closes it); every other connection is
  registered as before. A bulk connection is never a session: no catalog, frames, device row or "Client
  connected". It closes with its session, and at once when the Mac stops serving the session's device
  (Remove, Require pairing turned on, Remote Access or internet access turned off).
- **On it**, either side sends kind 34 (parts) and the host also reads 37; each is taken exactly as if it
  had come on the session (the host keys a file in parts, a drop and a clipboard copy by the session).
  Any other kind is read and dropped. Each part is still at most 512 KB, and the data connection's cap on
  a message is 1 MiB + 64 bytes (a header announcing more closes it).
- **Pacing (host → device).** The host sends a file's parts on the bulk connection when it is up, 512 KB
  each, else on the session, 64 KB each at home and 16 KB away. Before each part:
  - nothing goes while the server is withholding the session's picture (a delta dropped and a keyframe
    awaited, or frames held back this second), or while the session's own unsent bytes are past 128 KiB
    at home or 40 KiB away beside the bulk connection, 32 KiB or 8 KiB when the part would go on the
    session itself (below the 192 KiB and 64 KiB past which side streams' frames are dropped), but one
    part at least every 2 s, so a session stuck waiting for a keyframe never stops a transfer for good;
  - at most 1 MiB (home) or 512 KB (away) handed to the bulk connection and not yet taken;
  - otherwise a token bucket of two parts at 20 MiB/s at home and 1 MiB/s away on the bulk connection,
    4 MiB/s and 256 KiB/s on the session.
  A transfer whose bulk connection goes mid-way starts again from its first byte on the session under the
  same transfer ID; the receiver places parts by offset from either connection, so nothing is written
  twice.
- **Ordering.** A message that names transfers (kind 35's `files`, kind 38's `files`, kind 32's
  `files.transfers`) goes on the session and can overtake its last parts on the bulk connection: the host
  waits until every named transfer is whole, or until no part from that device has come for 3 s (600 s
  at most), then acts with what came.
- Without it (an older host, or before it is up) kind 34 goes on the session as #66 does.

## Clipboard generations (kind 32)

A device subscribes with `{"accepts":2}` or `{"accepts":3}` (a plain `{}` stays text only); a host
sends a field only to a subscription that accepts its generation, and always fills `text` too when
the item has text, so a generation 1 reader keeps working. The host takes a bare subscription's `accepts`
(1 without it), a copy's `accepts` when it carries one (a copy without keeps the subscription's), never
more than it offers. Sill for Mac offers 3 while it shares the clipboard (2 from a synthetic test host,
which writes no file); a subscription of 1 gets exactly generation 1's message, byte for byte. Every
pasteboard read stays behind macOS's paste privacy (`ClipboardPolicy.hostLooks`), and nothing of this
Mac's own writes or of Universal Clipboard's items is read.

- **2**: `image` (PNG bytes, base64 in JSON, at most what keeps the message within the 1 MiB / 4 MiB
  caps), `html` (UTF-8 HTML fragment, no CF_HTML header: the Windows side adds and strips it), `rtf`
  (RTF bytes). Larger images go as a clipboard file (generation 3) or are skipped. From the host,
  `concealed: true` (a `Bool`, to a subscription of generation 2 or later, left out otherwise) marks an
  item a password manager marked concealed or transient on the Mac (any of `org.nspasteboard.ConcealedType`,
  `org.nspasteboard.TransientType`, `de.petermaurer.TransientPasteboardType`, `com.agilebits.onepassword`
  on the item): the Mac still sends it (since 2026-10-29), and the device keeps it out of its clipboard
  history and cloud clipboard (Windows: `CanIncludeInClipboardHistory` and `CanUploadToCloudClipboard`
  0). A device never sends it; a host that does not mark items leaves it out.
- **3**: `files`, `{"offer":12, "files":[{"name":"a.pdf","size":1048576}]}`: files on the sender's
  clipboard, fetched only when pasted: the receiver sends kind 37 with that `offer` (the host) or, for
  files the device offers the host, the device sends them as kind 34 parts first and then a kind 32
  with `files.transfers` listing the transfer IDs, which the host writes to the Mac's pasteboard as
  file URLs of the received copies.
  - From the host: the file URLs on the Mac's pasteboard (files and folders that are there; with them
    no `image`, which would be Finder's icon), offered only to the subscriptions of 3 it went to, which
    alone may fetch them; a folder's `size` is 0 and `folder` true, and it goes zipped as #66's
    `FolderZip` when asked. The newest four offers stay fetchable. A kind 37 for one is never a move to
    the Trash.
  - From a device: `files.files[i]` lines up with `files.transfers[i]` (its name, and `folder: true` for a
    folder sent zipped, which the host unpacks); the host waits for parts still on the bulk connection
    (above), takes only that device's own whole transfers, keeps them in
    `~/Library/Caches/<bundle>/Clip/<copy>/` (512 MB a copy, two copies, a day, 1 GiB in all) and writes
    one pasteboard item a file, for this Mac only. At most 256 files a copy.

## Sound: `pcm-s16le` (kind 43)

Media Foundation has no AAC-ELD decoder, so the host also makes `pcm-s16le` (`AudioCodec.pcmS16LE`):
kind 43's format (type 1) says `"codec":"pcm-s16le"`, the sample rate and channel count, and each
packet (type 2) is interleaved signed 16-bit little-endian frames, 480 frames (10 ms at 48 kHz) a
packet. 1.5 Mbit/s at 48 kHz stereo, for the local network. A device lists it in `Hello.audio`.

## Opening documents on the Mac

**7 `launchApp`** gains `documents: [String]?`, absolute paths on the Mac (the device maps a path on
its own side through the share it mounted, `docs/coherence-plan.md` §M5), to a host whose list says
`documents` 1: the host opens them with that app (`NSWorkspace.open(_:withApplicationAt:)`); an empty
`bundleID` opens each with its default app. The app comes forward. The host opens only absolute, plain
paths (no `.`, `..` or empty part, no control character, at most 1024 bytes) to files or folders that are
there and readable (a symbolic link followed), at most 64 a message, each once; anything else is refused
with a line in the host's log naming the path's last part and why, and a bundle ID that is not one
refuses the message. A test host opens nothing. `documents` 1 only from a host that captures its Mac.
