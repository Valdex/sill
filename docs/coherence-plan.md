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

## M1b, the host's Coherence

M1b (2026-11-01, `wip/host-m1b` onto `coherence`) is the host side of the contract's window management;
the bulk connection, the clipboard generations and `LaunchApp.documents` are another worktree's.

- **Built.** Pure rules in `CoherencePolicy.swift` (`coherence-policy` check, Linux): kind 29 held to the
  contract, each window's gate (the focus at `focusFPS`, the others at `fps`, hidden; a child at its owner's
  rate), the capture knob from the devices' `scale`, windows joining by themselves (`apps`, `all`, an app
  launched or brought forward by a device), kind 45's entries and children (role from AXSubrole or the window
  level, `titleBar` from the close button, `controls`, an owner by overlap or by the menu it came from, `rect`,
  `inCrop` from each stream's picture), what each kind 45 carries (full, then whole entries of what changed),
  20 kind 45s a second per device, kind 46 at 20 a second per window, `min`/`max` learned from refusals, the
  per-window activation gate, kind 4's size. `WindowRegistry.swift`: AXObservers on every regular app,
  NSWorkspace's launches, quits and activations, the window server's list every 0.25 s, Accessibility for the
  shown windows (0.25 s messaging timeouts), the key window, Secure Event Input. `CoherenceController.swift`:
  kinds 29, 45, 46, 44 `{window}`, kind 4 at `iconSize`, take-back and leaving. In SideStreams: captures at
  the focus rate with a gate per window (`applyGates`, the encoder's rate set live: `HEVCEncoder.setRate`),
  children captured alone, a desk window captured with its app while a device is in Coherence (its menus and
  sheets in its picture, `DeskStages.prepare(withApp:)`). In StreamCoordinator (additive, an extension at
  its end): the list's `coherence` 1, `windowInput` 2, `iconSize` 512; every kind 8 with `window` on that
  window's rect, a click or key first activating a window that is not key (the hold, per window); kind 15
  `activate`/`restore`; a launched app joining the set instead of the main stream; in Coherence the cursor's
  shape, the link's ticks and the whole Mac's sound with no source picked, the Mac's menus (kinds 24/25/27)
  those of the key window's app, and a window sized where it was back to its frame at Quit and when no device
  shows it.
- **Uncompiled.** Every host file (no macOS SDK here). `CoherencePolicy.swift` type-checks and runs on Linux;
  `CoherenceController.swift` and `WindowRegistry.swift` type-check on Linux against stand-ins of AppKit,
  Accessibility, CoreGraphics, ScreenCaptureKit, Network and the host types they call; the rest parses.
- **Riskiest spots for the first Mac build.** `WindowRegistry`: the AXObserver callback (a `@convention(c)`
  closure, its refcon relay), `CFRunLoopMode.commonModes`, the dlsym'd `_AXUIElementGetWindow` and
  `IsSecureEventInputEnabled` signatures, `AXUIElementIsAttributeSettable`'s `DarwinBoolean`,
  `CGWindowListOption.optionAll`; notifications arrive only under the AppKit loop (else the poll). SideStreams:
  a desk window captured with its display's app filter, where #66 captured side windows alone (DeskPicture's
  "another display's picture" check covers only the main stream); `HEVCEncoder.setRate` on a live session.
  The coordinator: the lazy `coherence` read in `active`'s didSet, the per-window hold's token beside the
  older global one. Behaviour to watch: a restart of a side stream from a stale catalog frame can size a
  window back to its old size on the desk (the catalog is looked at again 0.3 s after a kind 46); every
  resize restarts that window's capture; six side streams at most, children included.
- **Deferred.** Covered (not minimized) windows as hidden is the device's; a child's own role from
  Accessibility beyond sheets and popovers (menus and tooltips go by window level); restoring a window sized in
  place that later moved onto the desk; the DeskPicture check for side windows; a bitrate budget across
  windows (M6); kind 4 at `iconSize` goes for the apps with windows (each as its first window is listed),
  after the 64 px icons every device gets at connect: the installed apps' icons stay 64 px.

## Progress

Each milestone's entry in CLAUDE.md's Current step says what was built, checked and left.
