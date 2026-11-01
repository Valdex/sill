# Sill for Windows

A Windows client for Sill on the local network: it finds the Macs running Sill on your network, pairs
with one by the 12-digit code its pairing window shows, and streams the Mac's Desktop, or one of its
windows, into a window on your PC, with your mouse, keyboard and clipboard going the other way. It
speaks the same wire format, TLS doors and pairing as Sill for iPhone and iPad, so Sill for Mac needs
nothing new.

It is unofficial: it lives in the fork [Valdex/sill](https://github.com/Valdex/sill), not in upstream
[Saffsanity/sill](https://github.com/Saffsanity/sill), so report its problems in the fork.

C# on .NET 10 and Windows' own frameworks, with no packages: Media Foundation decodes HEVC on the GPU
(through Direct3D 11), Direct3D 11 draws it, waveOut plays the Mac's sound, Schannel does TLS 1.3,
and a few hundred lines of multicast DNS find the Macs.

## Requirements

- Windows 11, 64-bit (x64 or ARM64). Windows 10's TLS has no 1.3, which every Sill door requires.
- The .NET 10 Desktop Runtime (`winget install Microsoft.DotNet.DesktopRuntime.10`), or the .NET 10
  SDK to build it. (It moved from .NET 8, whose support ends on 2026-11-10.)
- **HEVC Video Extensions** from the Microsoft Store (many PCs come with them; `Sill.exe --diag`
  says whether Windows has an HEVC decoder).
- Sill for Mac 0.4.0 or later (a TLS home door) on the same network. A development build or
  `SillHost` without `--pairing` (a plain door) works too, unencrypted.

## Build and run

```
dotnet build WindowsClient\Sill.Windows -c Release
WindowsClient\Sill.Windows\bin\Release\net10.0-windows\Sill.exe
```

or a folder to copy elsewhere: `dotnet publish WindowsClient\Sill.Windows -c Release -o WindowsClient\publish`.

## Using it

1. Open Sill on the Mac (it lives in the menu bar). The Mac shows up in Sill's list on the PC within a
   few seconds; one on another network can be reached with **Connect by Address…** (the Mac's
   address and the port Sill listens on).
2. **Connect**. The first time, the Mac opens its pairing window: type its 12 digits into the PC.
   A Mac whose Require pairing is off (Settings › Devices) connects without a code.
3. The Mac's Desktop streams at once. The toolbar picks a **window** (each with its thumbnail), opens
   an **app**, and sets the Mac's **Quality** (bitrate, Retina or Standard, 60 or 120 fps, the virtual
   display, and whether the Mac's window is sized to this one). **View** has the rest (below), and
   **Full Screen** is Ctrl+Alt+Enter too.

Keys go to the Mac by position, so the Mac's keyboard layout decides the characters, as with a keyboard
plugged into it. Ctrl acts as ⌘, the Windows key as ⌃ and Alt as ⌥ (Settings has the other way
round). In a window Alt+Tab stays Windows'; in full screen it goes to the Mac. The pointer is this PC's,
in the Mac's cursor shape, so it never lags. Text copied on the Mac lands on this PC's clipboard; text
copied here goes to the Mac when Sill's window comes forward (never what a password manager marks).
A mouse's back and forward buttons go back and forward in the Mac's apps (⌘[ and ⌘]). A key held
down repeats at the Mac's own Delay Until Repeat and Key Repeat Rate when the Mac repeats keys itself
(a Sill for Mac whose window list says so, from 2026-10-29), else at Windows'. A picture that has to wait for a keyframe (the
decoder or the GPU starting over) asks the Mac for one. A session that drops dials again by itself
for two minutes.

The Mac's sound plays here when the Mac sends it as plain PCM (`pcm-s16le`, which this fork's Sill
for Mac adds: Windows has no decoder for upstream's AAC-ELD) and its Send Audio is on. It waits 40 to
60 ms here for packets that come late, then 30 ms in the sound card; a stall's sound is dropped
rather than played late, so it never falls behind the picture. **View › Mute** silences it on this PC (kept); the Mac keeps sending.

Sill opens connected: it connects to the Mac it streamed last as soon as Bonjour lists it, or after a
few seconds at the address it last reached it at; hold Shift while Sill opens to stay on the list. The
stream window opens where it was closed, at its size, maximized or in full screen as it was. Settings
turns these off, and can open Sill when you sign in to Windows.

### Shortcuts and the View menu

While the picture has the keyboard, Ctrl+Alt+Shift with one of these letters is Sill's own: the letter
never reaches the Mac (it sees ⌘⌥⇧ come and go), so ⌘⌥⇧V, Paste and Match Style, is this PC's then.

| Shortcut | What it does |
| --- | --- |
| Ctrl+Alt+Enter, Ctrl+Alt+Shift+F | Full screen. There, rest the pointer at the middle of the top edge and the toolbar comes down over the picture; it goes when the pointer leaves it. |
| Ctrl+Alt+Shift+D | The Mac's Desktop. |
| Ctrl+Alt+Shift+W | The Mac's windows, as a menu over the picture (also in full screen; arrows and Enter pick one). |
| Ctrl+Alt+Shift+P | A screenshot of the Mac's picture at its own size, not the window's: a PNG in Pictures\Sill, also on the clipboard. Click the note to show the file. |
| Ctrl+Alt+Shift+V | Types this PC's clipboard text on the Mac, character by character: for a login window, a password field, or a Mac that does not share its clipboard (10,000 characters at most). |
| Ctrl+Alt+Shift+S | Frame rate, round trip, the stream's size and bitrate, in the toolbar (in full screen, in the bottom right corner). |
| Ctrl+Alt+Shift+T | Always on top. |
| Ctrl+Alt+Shift+M | Minimize (a way out of full screen, where the Windows key and Alt+Tab are the Mac's). |
| Ctrl+Alt+Shift+Q | Disconnect. |

View also has **Fit Window to Picture** (the window takes the picture's shape, so no black bars are
left), **Actual Size** (a picture pixel to a screen pixel, as far as the screen allows) and **Mute**.

What it keeps is in `%LOCALAPPDATA%\Sill` (the Macs it paired with and its settings); its key is in
Windows' key storage for this user, non-exportable ("Sill for Windows device key"). Forget a Mac with a
right-click on it.

## Coherence

With a Sill for Mac that can (its window list says `coherence` 1 and `sideStreams`; this fork's,
`docs/coherence-plan.md`), each Mac window you choose becomes a window of this PC: its own taskbar
button grouped under its Mac app (with the app's icon), Alt+Tab, Snap, its own size, and the stream
window steps aside.

- **Coherence** in the toolbar: **This Window**, **All of ‹App›'s Windows** (and its new ones as they
  open), **Choose Windows…**, **Every Window, and New Ones**, or an app's windows. **View › Coherence**
  does the same and is kept: a Mac that can is shown so again the next time Sill connects to it, by
  the apps shown last. Each window streams on its own (one HEVC stream and decoder per window), the
  one in front at the monitor's rate (up to 120 fps), the others at 30, minimized ones (or on another
  virtual desktop) paused, all captured at this PC's DPI scale.
- While it runs, Sill lives in the notification area: its menu lists the windows (a click brings one
  forward), shows another window, opens an app, stops showing a window, **Leave Coherence** (the
  stream window comes back with the window that was in front) and **Disconnect**.
- **A window** shows the Mac window without its plain title bar (a unified toolbar is kept): the
  Windows frame and the Mac's title replace it. Activating it makes the Mac window key; moving it is
  this PC's alone; resizing stretches the last picture until one of the new size comes and sizes the
  Mac window (at most 20 times a second, and at the end); the Mac's least and largest sizes hold;
  **Maximize** sizes the Mac window to the screen (never the Mac's full screen); **Minimize** pauses
  its stream; the close box asks the Mac, and the window stays until the Mac closes it (a "Save
  changes?" sheet may come first). On a monitor of another scale it takes Windows' size for it.
- **Input**: the mouse goes to the window under it, the keyboard to the window in front (only while a
  Coherence window is in front, as with the stream window; Alt+Tab stays Windows'), the pointer takes
  the Mac's cursor shape. A Mac menu or popover that hangs out of its window opens as a window of its
  own beside it, without taking the focus; sheets and dialogs that do are windows owned by theirs.
  When you click away from Sill with a Mac menu open, the Mac gets Esc, so it never stays open.
- **The Mac's menus**: each window has a menu bar with its Mac app's menus, read from the Mac when
  you open one; the shortcuts show as this PC's keys (⌘ as Ctrl, or as the Windows key when Ctrl is
  Ctrl in Settings) and are typed as usual: the keys go to the Mac. The stream window has them under
  **Menus** too.
- **The taskbar and Start**: each Mac app's windows share a button (AppUserModelID
  `Sill.<Mac ID>.<bundle ID>`); pinned, it opens the app on that Mac through Sill (`Sill.exe --launch
  <Mac ID> <bundle ID>`). A Mac app you open from Sill gets a shortcut in Start › Sill and a place in
  the jump list's **Recent Mac apps**. Opening Sill again, or a pinned Mac app, goes to the Sill
  already running. The icons are kept in `%LOCALAPPDATA%\Sill\Icons`.
- Ctrl+Alt+Shift: M minimizes and T keeps on top the window in front, W opens the notification
  area's menu; the stream window's own (full screen, the Desktop, screenshot, stats) wait for it.

## Layout

- `Sill.Windows/Protocol/` — the wire format (`Wire.cs`: the 14-byte header, the kinds and the caps),
  the JSON payloads (`Payloads.cs`), pairing (`Pairing.cs`: the code's Damm digit, PBKDF2, the proofs,
  the Mac ID, the recognition tag, kind 18's signature), HEVC (`Hevc.cs`: length-prefixed to Annex B,
  an SPS's picture size), the sound (`Audio.cs`: kind 43's format and packets), the Coherence
  contract (`Coherence.cs`: every message of `docs/coherence-protocol.md`), the Mac's menus
  (`MacMenu.cs`) and `SafeText.cs`. Each mirrors its file in `Sources/StreamProtocol`, or the contract.
- `Sill.Windows/Net/` — `Mdns.cs` (a one-shot mDNS browser for `_sill._tcp` on every IPv4 interface,
  the TXT record's `p` and `r`), `Transport.cs` (TCP, TLS 1.3 with the pin, ALPN `sill/1` or
  `sill-pair/1`), `Connection.cs` (reader and writer with the caps), `PairingClient.cs` (the ask, the
  code, Cancel), `Session.cs` (the hello first, viewport, pings, stats, the catalog, the Desktop when
  nothing streams, how a session ends; `Session.Coherence.cs`: Coherence's kinds and the menus'
  subscription), `Identity.cs` and `Store.cs`.
- `Sill.Windows/Video/` — `HevcDecoder.cs` (Media Foundation's decoder, low latency, on the GPU through
  a DXGI device manager when it can), `Renderer.cs` (a flip-model swap chain with one frame of latency,
  NV12 to RGB in a shader, supersampled when shown smaller), `VideoPipeline.cs` (the video thread, a
  lost GPU made again), `KeyframeRecovery.cs` (when to ask for a keyframe, kind 44: the iOS client's rule).
- `Sill.Windows/Input/` — `KeyMap.cs` (scan codes to the Mac's HID usages), `MacKeys.cs` (the keys held
  on the Mac, the let-go, the Mac's key repeat), `KeyboardForwarder.cs` (the low-level keyboard hook,
  on a thread of its own, and its rules).
- `Sill.Windows/Audio/` — `AudioReceiver.cs` (what of kind 43 plays), `JitterBuffer.cs` (40 to 60 ms,
  the oldest dropped past that), `WaveOutDevice.cs` (three 10 ms buffers on the sound card),
  `MacSound.cs` (them together, Mute).
- `Sill.Windows/Coherence/` — pure (checked): `WindowSet.cs` (kind 45's reducer, what each window
  becomes), `SideStreamPlan.cs` (kind 29's content, the hidden list, the coalescing),
  `ProxyGeometry.cs` (the crop, the pointer, sizes, limits, a child's place; kind 46's throttle),
  `CoherenceController.cs` (a session's Coherence: routing, kinds 29, 44, 46, 15, input aimed at a
  window), `ShellNames.cs` (AppUserModelIDs, `--launch`, the .ico, the recent apps).
- `Sill.Windows/Menus/` — pure (checked): `MacMenuState.cs` (the iOS client's rules for kinds 24, 25,
  27), `MenuShortcut.cs` (⌘ shortcuts in this PC's words).
- `Sill.Windows/Shell/` — the taskbar identity (`PropertyStore.cs`), the icons (`IconCache.cs`), Start
  menu shortcuts and the jump list (`ShellLinks.cs`), one instance (`SingleInstance.cs`).
- `Sill.Windows/Interop/` — COM by vtable slot for Direct3D 11, DXGI and Media Foundation, the slots
  taken from the Windows SDK's headers.
- `Sill.Windows/UI/` — the connect screen, the stream window, the dialogs, the dark theme; Coherence's
  windows (`ProxyWindow.cs`, `PopupWindow.cs`), their host and the notification area
  (`CoherenceHost.cs`), the Mac's menus in WinForms menus (`MacMenus.cs`).
- `Sill.TestHost/` — TEST ONLY: a stand-in Mac (below).
- `Sill.Checks/` — the client's pure checks (below): a console program on plain .NET that compiles the
  files above with no WinForms or Win32 in them and checks them, on any system.
- `tools/make-icon.ps1` — `Sill.ico` from `design/AppIcon.svg` (Edge renders it).

## Testing without a Mac

`Sill.TestHost` is a stand-in Mac on this PC's loopback: a home door (plain, `--tls` for an open TLS
door, `--pairing` for one that requires pairing, printing its code), a test pattern through Media
Foundation's HEVC encoder that draws what the client sends (a cross at the pointer, a disc while a
button is down, a square that changes colour with each key), and every message printed. Its window
list says `keyRepeat` 1 (`--no-key-repeat`: as a Mac from before), and it prints a key-down sent for a
key already down; to a client whose hello lists `pcm-s16le` it sends Sill's test tone (440 Hz and a
click at each whole second, 48 kHz stereo; `--no-sound`: none); a keyframe request makes its encoder
start again with a keyframe.

```
dotnet run --project WindowsClient\Sill.TestHost -- --pairing --port 47100
set SILL_TEST_DATA=%TEMP%\sill-test
Sill.exe --connect 127.0.0.1:47100 --pair-code <the code it printed> --input-script --log %TEMP%\sill.log
```

Test arguments (`TestArguments.cs`): `--connect host:port` or `--connect "Mac name"`, `--pair-code`,
`--input-script` (pointer, clicks, a scroll, keys, text, the clipboard and a settings change, only to
a host on loopback), `--actions "1:P,2:fit,3:F,4:bar,5:bounds"` (View's actions and shortcuts at
those seconds after the first picture, also only on loopback), `--type-text TEXT` (what Type the
Clipboard's Text types, so a test never reads your clipboard), `--offscreen` (windows, full screen
included, off every display), `--quit-after S`, `--log FILE`; `SILL_TEST_DATA` keeps a test run's
saved Macs, settings and screenshots apart from yours, and its screenshots off the clipboard. `Sill.exe --diag` lists Media Foundation's HEVC decoders and encoders and
whether the decoder runs on the GPU; `Sill.exe --probe host:port [sill/1|sill-pair/1|plain]` dials
once and prints the TLS version, cipher, ALPN and the Mac's key. `Scripts/sillclient.py` reads the test
host as it reads a Mac. `--actions` also takes `mute`.

The test host stands in for a Mac with Coherence too (`--no-coherence`: as a Mac from before): its
window list says `coherence` 1, `sideStreams` 6 and `windowInput` 2; kind 29 streams each window asked
for as a side stream of its own (a colour and a label each, under a fake 28 pt title bar with a white
line right under it, where the client's crop must fall; the focused one at its rate, hidden ones a
frame every 3 s); kind 45 says the windows (full, then partial); kind 46 resizes (its encoder made
again) and is answered; kind 15 activates and closes (TextEdit's window shows a "Save changes?" sheet
first); typed into a window, P opens a menu that hangs out of it (`inCrop` false), Esc closes it, T
takes the window back (kind 41); an app launched joins by kind 29's `apps`; it serves the Mac's menus
and kind 4 icons. `Sill.exe --coherence` enters Coherence with every window at the first picture, and
`--actions` takes `coherence`, `leave`, `resize`, `drag`, `maximize`, `restore`, `minimize`, `close`,
`activate`, `popup`, `takeback`, `deactivate`, `menu`, `stop`, `add`, `launch`, `launchshell` and
`proxies` (each window's bounds, picture, crop, menu strip and icon, and the kind 29 it would send):

```
dotnet run --project WindowsClient\Sill.TestHost -- --port 47100
Sill.exe --connect 127.0.0.1:47100 --coherence --actions "3:proxies,4:popup,5:proxies,6:drag,8:proxies,9:close,10:close,12:proxies,13:leave" --log %TEMP%\sill.log
```

## Checks

```
dotnet run --project WindowsClient/Sill.Checks            # every group; exit 1 if anything failed
dotnet run --project WindowsClient/Sill.Checks -- audio   # the groups whose names contain "audio"
```

In the spirit of upstream's `Tests/checks`: the 14-byte header, every payload of
`docs/coherence-protocol.md` against the contract's own JSON, the window-ID prefix and kind 34's part,
kind 43's packets against `Audio.swift`'s refusals, pairing's check digit and HEVC's Annex B, when to
ask for a keyframe (the iOS check's cases and random sessions), the keys held on the Mac and its key
repeat, what of the sound plays, and the jitter buffer on a simulated clock (a steady minute, a stall,
a Mac clock 0.5 % fast and slow, jittery arrival), and Coherence: kind 45's reducer (generations,
full and partial, removals with children, owners), kind 29's content and coalescing, the hidden list,
kind 46's throttle, the crop and the pointer, the limits, a popup's place, the controller end to end
on a simulated clock (routing by window, kind 44 per window, focus, take-back, a reconnect), the
Mac's menus (the iOS rules, the shortcuts) and the shell's names. It runs on Linux and macOS too.

## Not yet

Three-finger gestures (kind 28), Remote Access away from home (the remote door, the addresses kind 18
carries), Direct Wireless (AWDL is Apple's), the USB cable, a signed installer, and AAC-ELD sound (no
decoder on Windows). Coherence's later milestones (files, drag and drop, the richer clipboard, a
bitrate budget) are on their way: `docs/coherence-plan.md`. Coherence needs this fork's Sill for Mac.
