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
- Since 2026-11-01 the whole host **type-checks on Linux against stub frameworks**
  (`Tests/hostcheck/run.sh`: StreamProtocol, SillHostCore, SillHostCLI, SillMenuBar and the probes, as
  SwiftPM builds them, with the type checker and SIL's mandatory diagnostics), with no error. The stubs are
  written from the SDK's signatures and held to compiled code (upstream `e4d3034` and PR #66's host pass
  against them), so a wrong name, label, type, `await` or `try` in the host's code is caught; a stub that
  differs from the real SDK, an API newer than macOS 14 without `#available`, and Objective-C's own rules
  are not (`Tests/hostcheck/README.md`). It does not replace the Mac build.
- The two sides are checked against each other's real code, not only each other's JSON
  (`Tests/checks/coherence-walks`, since 2026-11-01). Kind 45: seeded walks of a simulated Mac go
  through the host's real `CoherenceEntries` and `WindowStateReporter`; each message is written with the
  set a device holds after it by the contract, and `Sill.Checks`' `swift-walks` replays them through the
  client's real `WindowSet`. Kind 29: the client's real `CoherenceController` and `SideStreamPlan` write
  each request with what they mean by it, and the Swift check reads it with the host's `CoherenceAsk`,
  `CoherenceGates` and `SidePlan`. They found three host faults (a listed child sent as a window of its
  own, a forgotten size limit left out, a child in front not streamed as the focus), fixed, and made the
  contract say what a field left out and a spilled child mean. Since 2026-11-02 the kind 29 walks show more
  windows than the six the host streams (and the host's six are checked against the contract's pick), the
  kind 45 walks stop windows and show them again at once, and the client's kind 29 walks are checked against
  a Mac whose view lags by the coalescer (every window it counts as the device's held by it).
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
  resize restarts that window's capture; six side streams at most, children included (beyond six, since
  2026-11-02: the device's window in front and a child of it first, then the windows shown, then the hidden
  ones, each as listed; `SidePlan`, the picks from `CoherenceGates.picks`).
- **Deferred.** Covered (not minimized) windows as hidden is the device's; a child's own role from
  Accessibility beyond sheets and popovers (menus and tooltips go by window level); restoring a window sized in
  place that later moved onto the desk; the DeskPicture check for side windows; a bitrate budget across
  windows (M6); kind 4 at `iconSize` goes for the apps with windows (each as its first window is listed),
  after the 64 px icons every device gets at connect: the installed apps' icons stay 64 px.

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

## M6

- **The Windows client.** Hidden windows: a minimized, cloaked (another virtual desktop) or wholly
  covered window is in kind 29's `hidden`, so the Mac encodes nothing for it (`Coherence/Occlusion.cs`,
  the `occlusion` checks: covered at two looks a second apart, shown again at the first look that finds a
  pixel of it). Install: `tools/publish.ps1` (win-x64 and win-arm64, framework-dependent or
  self-contained), `install.ps1` and `uninstall.ps1` (this user, no administrator; `Sill.exe
  --install-integration`/`--uninstall-integration` for Explorer's Open on Mac, the Run key and the Start
  menu's Mac app shortcuts).
- **The host.** A bitrate budget across Coherence's windows (`CoherenceBudget` in CoherencePolicy.swift,
  applied in `SideStreams.applyGates` at every gate change, stream start and stop): twice the quality's
  bitrate per 60 fps, 8 Mbit/s at least; the focused window keeps its own rate, a hidden one is held at
  1 Mbit/s, the others share the rest by pixels times frame rate, never under 1 Mbit/s nor over their own
  rate (water-filling). When everything fits, every window keeps the rate it had before the budget.
  Checked in `coherence-policy` (the budget's cases and 2,000 random sets) with mutants.
- **Not done.** Signing (Authenticode for the client, a Developer ID for this fork's Sill for Mac), an MSI
  or MSIX package, and the eight-hour soak, which needs a Mac and a PC.

## Trying it: the first build

Nothing here has run on a Mac or on Windows yet. In this order:

1. **The Mac.** On this branch, `swift build -c release` first: the host's AppKit, ScreenCaptureKit,
   Accessibility and VideoToolbox code was written without a macOS SDK, so expect compile errors (the
   riskiest spots are listed in "Port of PR #66", "M1b" and "M4/M5 host" above; `Tests/hostcheck` finds
   none against its stubs, so what remains is where a stub and the SDK differ). Send them as they come.
   Then `Tests/checks/run-all.sh`, then `Scripts/make-app.sh --install --open` (Screen Recording and
   Accessibility are asked for again for a build of your own).
2. **The PC.** `dotnet build WindowsClient\Sill.Windows -c Release` (or `tools\publish.ps1` and
   `install.ps1`), and `Sill.exe --diag` for the HEVC decoder. Without a Mac: `Sill.TestHost` streams test
   windows in Coherence (`WindowsClient/README.md`, "Coherence").
3. **Together.** Pair as before; the classic window first (sound now plays), then the Coherence button.
   Watch for what the kill criteria above name, and the log (`Sill.exe --log <file>`; Sill.log on the Mac).

## Progress

| Milestone | State |
|---|---|
| M0 | Done: the branch on upstream main, .NET 10, sound, kind 44, key repeat, the hook's thread, `Sill.Checks` |
| M1 | Done, uncompiled on the Mac: #66's side streams, desk, files (M1a); kinds 45/46, focus, joins, input by window (M1b) |
| M2, M3 | Done in the client: a window per Mac window, taskbar, popups, menus, DPI, Start menu, one instance |
| M4, M5 | Done on both sides: `sill-data/1`, clipboard generations 2 and 3, drag and drop, SMB, Open on Mac |
| M6 | Done but signing and the soak: hidden windows paused, the bitrate budget, install scripts |

The contract is checked from both ends: `Tests/checks/coherence-fixtures` encodes every Coherence message
with the Mac's Swift types, and `Sill.Checks`' `swift-wire` group reads each with the client's C# types.
`Tests/checks/coherence-walks` checks what the messages mean on both sides: the host's kind 45s through the
client's WindowSet (`swift-walks`), the client's kind 29s through the host's gates (`kind29-walks`).

Settled 2026-11-02 (branch `wip/cap-race`), the two faults the walks had left open:
- **More than six windows.** The client lists every window it shows; the host streams six (`sideStreams`).
  It used to take the first six listed, so a seventh window got no frames even in front. Now (the contract,
  kind 29) the host takes the windows in front first (the focus, and a child of the window in front), then
  those shown, then the hidden ones, each group as listed, and the picks follow the focus (`SidePlan`'s
  places from `CoherenceGates.picks`; SideStreams plans again when the picks change without a kind 29, as a
  child's owner learned from a kind 45). The limit stays six; the client changes nothing.
- **A window stopped and asked for again before the host noticed.** The client forgot a stopped window's
  entry at once, while the host counted the window as the device's until it read a kind 29 without it, and
  sent nothing more of it: shown again within the host's 50 ms pace, or within the client's 100 ms
  coalescing (which hid the stop from the host altogether), it had no entry until a `full` kind 45; and a
  kind 45 written before the host read the stop brought it back on screen against the stop. Now (the
  contract, kind 45) the device keeps a stopped window's entry out of sight until a kind 45 removes it
  (`WindowSet.Forget`, `Show`, `Kept`), and a removal that crosses the kind 29 asking for the window again
  leaves it asked for (`CoherenceController`'s `crossing`). No wire change and no host change: a host-side
  rule alone cannot see a stop the coalescer hid.
Each milestone's entry in CLAUDE.md's Current step says what was built, checked and left.
