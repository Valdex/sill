# Sill for Windows

A Windows client for Sill on the local network: it finds the Macs running Sill on your network, pairs
with one by the 12-digit code its pairing window shows, and streams the Mac's Desktop, or one of its
windows, into a window on your PC, with your mouse, keyboard and clipboard going the other way. It
speaks the same wire format, TLS doors and pairing as Sill for iPhone and iPad, so Sill for Mac needs
nothing new.

It is unofficial: it lives in the fork [Valdex/sill](https://github.com/Valdex/sill), not in upstream
[Saffsanity/sill](https://github.com/Saffsanity/sill), so report its problems in the fork.

C# on .NET 10 and Windows' own frameworks, with no packages: Media Foundation decodes HEVC on the GPU
(through Direct3D 11), Direct3D 11 draws it, Schannel does TLS 1.3, and a few hundred lines of
multicast DNS find the Macs.

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
A mouse's back and forward buttons go back and forward in the Mac's apps (⌘[ and ⌘]). A session that
drops dials again by itself for two minutes.

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
left) and **Actual Size** (a picture pixel to a screen pixel, as far as the screen allows).

What it keeps is in `%LOCALAPPDATA%\Sill` (the Macs it paired with and its settings); its key is in
Windows' key storage for this user, non-exportable ("Sill for Windows device key"). Forget a Mac with a
right-click on it.

## Layout

- `Sill.Windows/Protocol/` — the wire format (`Wire.cs`: the 14-byte header and kinds), the JSON
  payloads (`Payloads.cs`), pairing (`Pairing.cs`: the code's Damm digit, PBKDF2, the proofs, the Mac
  ID, the recognition tag, kind 18's signature) and HEVC (`Hevc.cs`: length-prefixed to Annex B, an
  SPS's picture size). Each mirrors its file in `Sources/StreamProtocol`.
- `Sill.Windows/Net/` — `Mdns.cs` (a one-shot mDNS browser for `_sill._tcp` on every IPv4 interface,
  the TXT record's `p` and `r`), `Transport.cs` (TCP, TLS 1.3 with the pin, ALPN `sill/1` or
  `sill-pair/1`), `Connection.cs` (reader and writer with the caps), `PairingClient.cs` (the ask, the
  code, Cancel), `Session.cs` (the hello first, viewport, pings, stats, the catalog, the Desktop when
  nothing streams, how a session ends), `Identity.cs` and `Store.cs`.
- `Sill.Windows/Video/` — `HevcDecoder.cs` (Media Foundation's decoder, low latency, on the GPU through
  a DXGI device manager when it can), `Renderer.cs` (a flip-model swap chain with one frame of latency,
  NV12 to RGB in a shader, supersampled when shown smaller), `VideoPipeline.cs` (the video thread).
- `Sill.Windows/Input/` — `KeyMap.cs` (scan codes to the Mac's HID usages), `KeyboardForwarder.cs` (the
  low-level keyboard hook and its rules).
- `Sill.Windows/Interop/` — COM by vtable slot for Direct3D 11, DXGI and Media Foundation, the slots
  taken from the Windows SDK's headers.
- `Sill.Windows/UI/` — the connect screen, the stream window, the dialogs, the dark theme.
- `Sill.TestHost/` — TEST ONLY: a stand-in Mac (below).
- `tools/make-icon.ps1` — `Sill.ico` from `design/AppIcon.svg` (Edge renders it).

## Testing without a Mac

`Sill.TestHost` is a stand-in Mac on this PC's loopback: a home door (plain, `--tls` for an open TLS
door, `--pairing` for one that requires pairing, printing its code), a test pattern through Media
Foundation's HEVC encoder that draws what the client sends (a cross at the pointer, a disc while a
button is down, a square that changes colour with each key), and every message printed.

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
host as it reads a Mac.

## Not yet

The Mac's menus (kinds 24, 25, 27), three-finger gestures (kind 28), Remote Access away from home (the
remote door, the addresses kind 18 carries), Direct Wireless (AWDL is Apple's), the USB cable, and a
signed installer.
