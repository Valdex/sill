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

## Progress

Each milestone's entry in CLAUDE.md's Current step says what was built, checked and left.
