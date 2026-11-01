# Coherence for Sill for Windows: the plan

Goal: several Mac app windows on a Windows PC at once, each a window of its own (taskbar button
grouped by Mac app, Alt-Tab, Snap, its own size), with the keyboard and mouse going back to the Mac,
a shared clipboard with images and files, drag and drop both ways, the Mac's files reachable from
Windows, and the Mac's sound. Local network only. Later the reverse (a Windows host, a Mac client).

The research behind it (2026-11-01, four agents over upstream main, this fork and the web) is
summarised here; the wire contract is `docs/coherence-protocol.md`.

## Decisions

| Area | Decision | Why |
|---|---|---|
| Base | Branch `coherence` = upstream main (e4d3034) + this fork's Windows client | The fork was 510 commits behind (no sound, key repeat, capture scale, the security fixes) |
| Host design | Port the host side of upstream PR #66 (Sill Viewer, unmerged, fe6c001): side streams, AppCrop, Sill's desk, file drops, live drag, take-back. Use its kinds | It already streams several windows at once for a Mac client, tested by hand; one wire for the Mac and Windows clients |
| Video | One HEVC stream per window (kinds 30/31), never a canvas | A canvas of 4 windows at 150 % is 11+ MP (over HEVC level 5 and many decoders), re-encodes whole on any change, and one keyframe stalls every window. Per window, only what changes is encoded |
| Encoder budget | Focused window 60 fps, others 15–30, hidden ones paused; capture at the device's pixel scale | The limit is the encoder's throughput (~650 MP/s on an M2 Pro), not its session count |
| Frame on Windows | v1 a native Windows frame, the Mac's plain title bar cropped (`titleBar`); unified toolbars kept | Snap Layouts, rounded corners, shadow, DPI and Alt-Tab for free |
| Move and resize | Moves local only; resizes stretch the last frame locally, then `placeWindow`; maximize = a resize, never the Mac's full screen | WSLg's server-side moves lag and lose snapping; the Mac's full screen opens a Space and hides every other window |
| Focus and input | Window activated on Windows → `windowCommand activate`; every input event carries `window`; the host holds input until the window is key | A key typed while focus moves must not land in the previous window |
| Popups | Inside the owner's picture (`wholeApp`); outside → an owned, non-activating popup window from a side stream of its own; Esc to the Mac when the client loses focus | Mac menus otherwise never close when Windows is clicked |
| Taskbar | AppUserModelID `Sill.<macID>.<bundleID>` per window, the Mac app's icon at 256 px, relaunch properties, Start menu shortcuts for apps launched | Grouping per Mac app, as Parallels does per Windows app |
| Mac menus | A `MenuStrip` per window, filled lazily from kinds 24/25/27 | The host already serves them |
| Transport | TCP + TLS 1.3 as today; bulk data on a second TLS connection (`sill-data/1`) | Enough on a LAN; QUIC only if Wi-Fi misses the latency targets |
| Clipboard | Generation 2 (PNG, HTML, RTF inline), generation 3 (files, fetched on paste); `AddClipboardFormatListener` on Windows | Text alone was main's first generation |
| Drag and drop | #66's host side (kinds 34–38); on Windows `IDropTarget` and `DoDragDrop` with virtual files | The hard Mac half is written and was tested by hand in #66 |
| Files | macOS File Sharing (SMB) mapped as a drive, with a guide and path mapping, so "Open on Mac" works; a virtual drive (Cloud Files API) later, optional | No driver, no licence, locking and names for free |
| Sound | Main's kind 43 with a new `pcm-s16le` codec, played through waveOut | Windows has no AAC-ELD decoder; 1.5 Mbit/s is nothing on a LAN |
| Licences | No code from Sunshine, Moonlight, xpra, Deskflow (GPL) or MirageKit (PolyForm Shield); FreeRDP (Apache-2.0) may be read and borrowed | Sill is Apache-2.0 |

## Milestones

| | What | Done when |
|---|---|---|
| M0 | The branch on upstream main; .NET 10 (8 ends 2026-11-10); kind 34-size messages no longer dropped silently by `Connection.Send`; the keyboard hook on a thread of its own; kind 44; the Mac's key repeat honoured; sound (kind 43, `pcm-s16le`); `Sill.Checks`, pure checks of the client's logic that run anywhere; this plan and the contract | The client works with today's Sill for Mac, with sound |
| M1 | Host: PR #66's side streams, AppCrop, desk, take-back on main; kinds 45 and 46; `window` on every input; `activate`/`restore`; per-window keyframes; `focus`, `apps`, `all`, `hidden`, `scale`; 256 px icons | Four windows stream at once, the focused one at 60 fps |
| M2 | Windows: a `ProxyWindow` and pipeline per Mac window, the native frame, focus and activation, local moves, `placeWindow`, taskbar grouping and icons | Three or four Mac windows used as Windows windows |
| M3 | Owned popups and sheets, Mac menus, DPI and monitors, Alt-Tab, Start menu shortcuts, jump list, one instance (`--launch`) | Menus and dialogs of ten everyday apps where they belong |
| M4 | `sill-data/1`, clipboard generations 2 and 3, drag and drop both ways | Images and files copy and drag both ways |
| M5 | SMB guide, path mapping, Open on Mac (`LaunchApp.documents`) | A file on the Mac's share opens in a Windows app and the other way |
| M6 | Hidden windows paused, a bitrate budget, an installer script, the docs | Eight hours without degradation |

Kill criteria (stop and rethink if any holds on a wired LAN with four windows live): the focused
window's glass-to-glass p50 over 60 ms or p95 over 100 ms; side windows below 10 fps; a window ever
showing another's picture; a key landing in the wrong window or a key or button left down; fewer than
95 % of menus, popovers and sheets right across ten apps; an activation round trip over 150 ms.

## How it is verified, and what is not

This branch was built in a Linux container: no Mac, no Windows.
- The Windows client is compiled with the .NET 10 SDK (`EnableWindowsTargeting`), and its pure logic
  runs in `WindowsClient/Sill.Checks` (a console program, no packages) on Linux.
- `Sources/StreamProtocol`'s Foundation-only files are type-checked with Swift 6.4 on Linux.
- The host's AppKit, ScreenCaptureKit and VideoToolbox code is **not compiled here**: the first
  `swift build -c release` on a Mac is its first compile. Fix-ups are expected.
- Nothing ran against a real Mac or on Windows. `Sill.TestHost` gains coherence (several windows,
  kind 45) so the client can be tried on a PC without a Mac.

## Port of PR #66

M1a (2026-11-01, `wip/host-m1` onto `coherence`) ported the host side of upstream PR #66 (fe6c001, written
against 2ecb6399, about 400 commits before main) by hand: a three-way merge per file as the base, then
each hunk read against main's code (CaptureScale, the encoder fallback, revoked devices, key repeat,
the catalog's look, kind 43).

- **Ported.** The protocol (`SideStreams.swift`; kinds 29–31 and 34–42 in `StreamMessage.swift`; #66's
  `WindowList` fields as `Int` generations; the middle button) and coherence's types (`Coherence.swift`,
  plus the fields the contract adds to existing payloads, `pcm-s16le`). On the host: `SideStreams` (now on
  `SidePlan`, each window a `SideWindowPipeline` whose `SideFrameGate` caps its rate and takes a hidden
  flag: the seam for `focus` and `hidden`), `AppCrop`/`SharePill`, `DeskStages`, `DeskPicture`,
  `DisplayStreamProbe`, `FileDrops`/`DragOut`, `LiveDrags`, the take-back (kind 41, the menu's Stop
  Sharing), the Desktop's display (kind 40), the desk's refresh (kind 42), and their hooks in
  StreamCoordinator, StreamServer, VirtualStage, VirtualDisplay, WindowCapture, WindowCatalog,
  WindowSizer, InputInjector, KeyStrokes, HostShutdown, HostStatus and the menu bar. The pure rules
  went into `SideStreamPolicy.swift` (`side-streams` check, Linux). The host makes `pcm-s16le` too.
- **Left out, and why.** `AppAudio` (#66's kind 33 sound: main's kind 43 serves, and 33 stays free);
  `DockMenus` (kind 39, a Mac client's Dock), with the badges it read and the Dock-icon "reopen" two
  paths used (a running app with no window is launched as on main; a picked window not drawn is
  captured where it is); `HostClipboard`'s images, `ClipboardImage` and `ClipboardPasteboard` (the
  clipboard generations are M1b's/M4's); Finder in the app list and the menu's new Virtual Display
  note (an iPhone or iPad would see them). Kinds 45/46, `activate`/`restore`, `window` on every event,
  the data channel and the clipboard generations are M1b's (the host refuses `activate`/`restore` and
  advertises none of them).
- **iOS unchanged** when no side streams are asked for: desk-only behaviour sits behind
  `VirtualStage.forDesk`, `SideStreams.asked`/`asks(_:)` or `desk.holdsDisplays` (the list of other
  Spaces, the announced source, drag-out watching, the Dock-press swallow, the viewport kept across a
  pick, the exit's capture stops, display waits and 20 s watchdog), and the Desktop keeps
  `preferMainDisplay`. Shared fixes kept: a retired pipeline's last frame dropped (`OutputSwitch`),
  a new display taken out of a mirror set, WindowSizer's title match without invisible characters (its
  size-only fallback only for a window not drawn), a window found on a Sill display homed on the main.
- **Riskiest uncompiled spots for the first Mac build** (nothing here was compiled against the macOS
  SDK; every file parses): StreamCoordinator's `select` window branch (stage, desk, crop and
  `displayScale` together) and `startPipeline` (announce, `deskCheck`, `encoderLive`); VirtualStage's
  merge onto `VirtualDisplaySize` (the 1× desk path, the `forDesk` gates, the nested `make(serial)`);
  `SideStreams.swift` (rewritten: a `@MainActor` pipeline with a `nonisolated` gate read on the
  capture queue); `StreamServer.sendSide` and the per-codec audio in `StreamServer`; `AudioPipeline`
  (type-checked on Linux against stubs only); `VirtualDisplay.resize` (main's `applySettings(_:to:)`);
  `WindowSpaces`' dlsym of SkyLight; `HostShutdown`'s `MainActor.assumeIsolated` for the budget.

## M4/M5 host

Built (2026-11-01, `wip/host-m45` onto `coherence`; the Windows client's side was already there):
- **`sill-data/1`.** `RemoteTLS.serverALPNs` offers it; DoorPolicy trusts it as `sill/1` and reads it at
  `.ready` under its session's rules; Door reads its kind 49 in the place and 4 s a pairing connection's
  kind 19 has; DataLinks (`DataLink.swift`) admits it for the session holding the ticket (same door, way,
  key and address, one per session, never a revoked session) and closes it with the session or at its
  revocation. The plain door reads a first message only while a plain session at that address awaits its
  bulk connection. Parts on it reach the coordinator as the session's own. Kind 48 goes once after the
  first window list to a hello of `coherence` 1. Sends (drag offers' and clipboard offers' kind 37) are
  paced by `DataPacing` (contract, "The bulk connection"); drops, live drags and clipboard copies wait for
  parts still on the bulk connection (`TransferWait`).
- **Clipboard generations 2 and 3.** `accepts` per subscription; PNG (or a TIFF made a PNG), HTML, RTF and
  file offers to the Mac's devices by generation within 4 MiB, `concealed` for a password manager's markers;
  a device's HTML, RTF, PNG or JPEG and copied files written to the pasteboard (`.currentHostOnly`, the
  own-write marker), the files kept under `~/Library/Caches/<bundle>/Clip/`. `clipboard` 3 (2 on a test
  host). `ClipboardRich.swift`, HostClipboard.
- **Documents.** Kind 7's `documents` opened with the named app or each with its own (`Documents.swift`);
  `documents` 1.
- Pure and checked on Linux: `DataLinkPolicy.swift` (`data-link`), `ClipboardRichPolicy.swift`
  (`clipboard-rich`), `DocumentPolicy.swift` (`documents`); door-policy's oracles gain `sill-data/1`.

**Uncompiled** (no macOS SDK here; every file parses): DataLink.swift, the StreamServer and Door hooks,
HostClipboard and ClipboardRich.swift, Documents.swift, FileDrops/LiveDrags' waits and the coordinator's
few lines. DataLink.swift and the clipboard files type-checked on Linux against hand-made stand-ins for
Network and AppKit (which prove the logic's types, not the SDK's signatures), and a scratch simulation drove
DataLinks with scripted connections.

**Riskiest spots for the first Mac build and run:**
- Door's `.data` case and `readDataHello`: the place's deadline re-armed at `.ready`, the `take` before or
  after DataLinks installs its own state handler, and a refused ticket's failure (5 in 60 s back an address
  off: a device that keeps presenting a stale ticket after its session ended could be backed off).
- The plain door's `readPlainFirst`: it holds a connection unregistered until its first message, so an old
  device without a hello, from the same address as a Coherence session still waiting for its bulk
  connection, is closed after 4 s; the first message it read is handed on by hand (`handFirst`).
- The ticket hook at the end of `StreamServer.send(_:to:)` (the first window list's byte), and `took`.
- `DataLinks.address(of:)` comparing the session's and the bulk connection's addresses as Door's
  `source(of:)` prints them (an IPv4-mapped address or a scope that prints differently would refuse).
- `DataPacing`'s numbers, tuned on paper against SideSend's budgets: watch the side streams' frame rate
  while a 500 MB file copies, and `data.out`, `side.dropped` and `net.dropped` in the `[1s]` lines.
- HostClipboard's rich look (the board queue reads HTML, RTF, a TIFF conversion and file URLs; a large TIFF
  is converted there), and the Task hops of a device's copied files (ClipStage's queue, then the main
  actor). Writing `public.png` without TIFF beside it: an app that reads only TIFF will not paste it.
- Reading a copied file the device fetches (kind 37) may bring macOS's prompt for Documents, Desktop or
  Downloads to Sill (as DragOut already could); `ditto -x -k` is trusted with a device's zip.

## Progress

Each milestone's entry in CLAUDE.md's Current step says what was built, checked and left.
