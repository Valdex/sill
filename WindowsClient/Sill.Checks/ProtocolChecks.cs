using System.Text;
using Sill.Windows.Protocol;

namespace Sill.Checks;

// The wire as the client writes and reads it: the 14-byte header, every payload docs/coherence-protocol.md
// adds (each against JSON written by hand from the contract, so a name or a type that drifts fails),
// the window-ID prefix of kinds 30 and 31, kind 34's part, and kind 43's packets against Audio.swift's
// layout and refusals.
static class ProtocolChecks
{
    static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>Decoded into T and encoded again: the same JSON, so every field is read and written
    /// under its contract name and nothing null is written.</summary>
    static T? RoundTrip<T>(string json, string what) where T : class
    {
        var value = Json.Decode<T>(U(json));
        Check.That(value != null, $"{what}: decodes");
        if (value != null) Check.SameJson(json, Json.Encode(value), $"{what}: encodes back to the contract's JSON");
        return value;
    }

    public static void Header()
    {
        Check.Equal(14, Sill.Windows.Protocol.Header.Length, "the header is 14 bytes");
        var m = Wire.Message(Kind.Ping, new byte[] { 1, 2, 3 }, 1.5, keyframe: true);
        Check.Bytes(new byte[] { 10, 0x3F, 0xF8, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 3, 1, 2, 3 }, m,
                    "kind, the time as a big-endian Double (1.5), the keyframe flag, the length (big endian), the payload");
        var h = Sill.Windows.Protocol.Header.Parse(m);
        Check.That(h.Kind == 10 && h.Timestamp == 1.5 && h.IsKeyframe && h.PayloadLength == 3, "parsed back: kind 10, 1.5 s, a keyframe, 3 bytes");
        var big = Sill.Windows.Protocol.Header.Parse(new byte[] { 31, 0x41, 0xD9, 0xD9, 0xF0, 0x84, 0x80, 0, 0, 0, 0x00, 0x01, 0x00, 0x00 });
        Check.That(big.Kind == 31 && Math.Abs(big.Timestamp - 1_734_853_138) < 1 && !big.IsKeyframe && big.PayloadLength == 65536,
                   $"a side frame's header from bytes: kind 31, a 2024 wall clock, not a keyframe, 65,536 bytes ({big.Timestamp})");
        Check.That(!Sill.Windows.Protocol.Header.Parse(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0 }).IsKeyframe, "a keyframe byte other than 1 is not a keyframe (Swift's == 1)");
        Check.Equal(int.MaxValue, Sill.Windows.Protocol.Header.Parse(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF }).PayloadLength,
                    "a length past int's range reads as int.MaxValue, over every cap");
        Check.Equal((byte)200, Sill.Windows.Protocol.Header.Parse(new byte[] { 200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }).Kind, "a kind this build does not know is read (and skipped)");
        Check.Equal(Header(Wire.Message(Kind.KeyframeRequest, ReadOnlySpan<byte>.Empty, 2.0)).PayloadLength, 0, "kind 44's empty payload: length 0");

        // The kinds' numbers, as the contract and #66 have them.
        Check.That((int)Kind.SideStreams == 29 && (int)Kind.SideParameterSets == 30 && (int)Kind.SideFrame == 31 && (int)Kind.Clipboard == 32, "kinds 29, 30, 31 and 32");
        Check.That((int)Kind.FileChunk == 34 && (int)Kind.FileDrop == 35 && (int)Kind.DragOffer == 36 && (int)Kind.FileRequest == 37 && (int)Kind.LiveDrag == 38,
                   "kinds 34 to 38");
        Check.That((int)Kind.TakenBack == 41 && (int)Kind.Audio == 43 && (int)Kind.KeyframeRequest == 44, "kinds 41, 43 and 44");
        Check.That((int)Kind.WindowState == 45 && (int)Kind.PlaceWindow == 46 && (int)Kind.DataTicket == 48 && (int)Kind.DataHello == 49, "kinds 45, 46, 48 and 49");
        Check.That(!Enum.IsDefined(typeof(Kind), (byte)33) && !Enum.IsDefined(typeof(Kind), (byte)47), "33 (#66's sound) and 47 are not used");

        // The caps a device keeps to.
        Check.That(Wire.Fits(1 << 20) && !Wire.Fits((1 << 20) + 1), "a device's message: 1 MiB fits, a byte more does not");
        Check.That(Wire.Fits((1 << 20) + 64, Wire.MaxDataPayload) && !Wire.Fits((1 << 20) + 65, Wire.MaxDataPayload), "the bulk connection: 1 MiB + 64 bytes");
        Check.That(!Wire.Fits(-1), "a negative length never fits");
        Check.That(Wire.Fits(FileChunk.FixedBytes + FileChunk.MaxNameBytes + FileChunk.MaxBytes), "the largest file part (512 KB and a 1 KB name) fits the session's cap");
        Check.Equal(512 * 1024, FileChunk.MaxBytes, "a file part carries at most 512 KB");
    }

    static Header Header(byte[] message) => Sill.Windows.Protocol.Header.Parse(message);

    public static void JsonPayloads()
    {
        // Kind 29, the contract's example, then #66's fields alone.
        var side = RoundTrip<SideStreamRequest>(
            """{"windows":[4711,4712], "fps":30, "wholeApp":true, "screen":[2560,1400], "coherence":true, "focus":4711, "focusFPS":60, "apps":["com.apple.Safari"], "all":false, "hidden":[4712], "scale":1.25}""",
            "kind 29, the contract's example");
        Check.That(side is { Fps: 30, WholeApp: true, Coherence: true, Focus: 4711, FocusFPS: 60, All: false, Scale: 1.25 } && side.Windows.SequenceEqual(new uint[] { 4711, 4712 })
                   && side.Screen!.SequenceEqual(new double[] { 2560, 1400 }) && side.Hidden!.Single() == 4712 && side.Apps!.Single() == "com.apple.Safari",
                   "kind 29: every field read");
        Check.SameJson("""{"windows":[7],"fps":15}""", Json.Encode(new SideStreamRequest { Windows = { 7 }, Fps = 15 }), "kind 29 from #66's device: the coherence fields left out");
        Check.That(SideStreamRequest.DefaultFPS == 15 && SideStreamRequest.MaxFPS == 30 && SideStreamRequest.DefaultFocusFPS == 60 && SideStreamRequest.MaxFocusFPS == 120,
                   "kind 29's rates: 15 by default, 30 at most; the focused window 60, at most 120");

        // Kind 44's payload.
        Check.Equal(0, KeyframeRequest.Payload(null, 1).Length, "kind 44 for the picked stream: empty");
        Check.Equal(0, KeyframeRequest.Payload(4711, null).Length, "kind 44 for a window, to a host without coherence: empty (the picked stream's)");
        Check.Equal(0, KeyframeRequest.Payload(4711, 0).Length, "… nor to one that says 0");
        Check.SameJson("""{"window":4711}""", KeyframeRequest.Payload(4711, 1), "kind 44 for a window, to a host that says coherence 1");
        Check.That(KeyframeRequest.Parse(ReadOnlySpan<byte>.Empty) is { Window: null }, "an empty kind 44 reads as the picked stream's");
        Check.That(KeyframeRequest.Parse(U("""{"window":9}""")) is { Window: 9 }, "kind 44 with a window reads it");
        Check.That(KeyframeRequest.Parse(U("not json")) == null, "kind 44 that is not JSON: null");

        // Kind 45, the contract's example.
        var state = RoundTrip<WindowState>(
            """
            {"gen":812, "full":false, "answering":7,
             "windows":[
               {"id":4711, "app":"com.apple.Safari", "appName":"Safari", "title":"Apple", "role":"window",
                "size":[1280,800], "min":[500,300], "resizable":true, "focused":true, "minimized":false,
                "fullScreen":false, "titleBar":28, "joined":true,
                "controls":{"close":[8,8,12,12], "minimize":[28,8,12,12], "zoom":[48,8,12,12]}},
               {"id":4790, "app":"com.apple.Safari", "role":"menu", "owner":4711, "rect":[40,22,220,310],
                "inCrop":true}],
             "removed":[4650], "order":[4790,4711], "focus":4711, "secureInput":false}
            """, "kind 45, the contract's example");
        var top = state?.Windows?[0];
        var menu = state?.Windows?[1];
        Check.That(state is { Gen: 812, Full: false, Answering: 7, Focus: 4711, SecureInput: false } && state.Removed!.Single() == 4650 && state.Order!.SequenceEqual(new uint[] { 4790, 4711 }),
                   "kind 45: the message's fields");
        Check.That(top is { Id: 4711, App: "com.apple.Safari", AppName: "Safari", Title: "Apple", Role: "window", Resizable: true, Focused: true, Minimized: false, FullScreen: false, TitleBar: 28, Joined: true, Owner: null }
                   && top.Size!.SequenceEqual(new double[] { 1280, 800 }) && top.Min!.SequenceEqual(new double[] { 500, 300 }) && top.Max == null
                   && top.Controls!.Zoom!.SequenceEqual(new double[] { 48, 8, 12, 12 }),
                   "kind 45: a top-level window's fields, its traffic lights");
        Check.That(menu is { Id: 4790, Role: "menu", Owner: 4711, InCrop: true, Size: null } && menu.Rect!.SequenceEqual(new double[] { 40, 22, 220, 310 }), "kind 45: a child menu inside its owner's picture");
        Check.Equal("window", new WindowState.Window { Role = "drawer" }.RoleOrWindow, "a role the device does not know is a window");
        Check.Equal("window", new WindowState.Window().RoleOrWindow, "no role is a window");
        Check.Equal("popover", new WindowState.Window { Role = "popover" }.RoleOrWindow, "a known role is kept");
        Check.That(WindowState.Roles.All.SetEquals(new[] { "window", "sheet", "dialog", "panel", "menu", "popover", "tooltip" }), "the seven roles");
        Check.That(WindowState.Newer(1, null) && WindowState.Newer(813, 812) && !WindowState.Newer(812, 812) && !WindowState.Newer(811, 812),
                   "a kind 45 applies only when its gen is newer than the last applied");
        Check.SameJson("""{"gen":3,"removed":[9]}""", Json.Encode(new WindowState { Gen = 3, Removed = new() { 9 } }), "kind 45 with only a removal: nothing else written");

        // Kind 46, 15 and 41.
        var place = RoundTrip<PlaceWindow>("""{"id":4711, "size":[1100,760], "token":7}""", "kind 46, the contract's example");
        Check.That(place is { Id: 4711, Token: 7 } && place.Size.SequenceEqual(new double[] { 1100, 760 }), "kind 46: its fields");
        Check.Equal(0.05, PlaceWindow.MinInterval, "kind 46 at most about 20 times a second during a resize");
        Check.SameJson("""{"id":4711,"action":"activate"}""", Json.Encode(new WindowCommand { Id = 4711, Action = WindowActions.Activate }), "kind 15's activate");
        Check.SameJson("""{"id":4711,"action":"restore"}""", Json.Encode(new WindowCommand { Id = 4711, Action = WindowActions.Restore }), "kind 15's restore");
        Check.That(WindowActions.Close == "close" && WindowActions.Minimize == "minimize" && WindowActions.FullScreen == "fullScreen", "kind 15's three older actions");
        var back = RoundTrip<TakenBack>("""{"windows":[4711]}""", "kind 41, the contract's example");
        Check.That(back?.Windows.Single() == 4711, "kind 41: the window taken back");

        // Kinds 35 to 38, the contract's examples.
        var drop = RoundTrip<FileDrop>("""{"files":[1,2], "x":0.4, "y":0.6, "window":4711, "folders":[2], "offer":9}""", "kind 35, the contract's example");
        Check.That(drop is { X: 0.4, Y: 0.6, Window: 4711, Offer: 9 } && drop.Files.SequenceEqual(new uint[] { 1, 2 }) && drop.Folders!.Single() == 2, "kind 35: its fields");
        Check.SameJson("""{"files":[3],"x":0,"y":1}""", Json.Encode(new FileDrop { Files = { 3 }, X = 0, Y = 1 }), "kind 35 from #66's device: x and y always, the rest left out");
        var offer = RoundTrip<DragOffer>("""{"id":9, "files":[{"name":"a.pdf","size":1048576,"folder":false}]}""", "kind 36, the contract's example");
        Check.That(offer is { Id: 9 } && offer.Files.Single() is { Name: "a.pdf", Size: 1048576, Folder: false }, "kind 36: its file");
        var req = RoundTrip<FileRequest>("""{"offer":9, "index":0, "transfer":31, "trash":false}""", "kind 37, the contract's example");
        Check.That(req is { Offer: 9, Index: 0, Transfer: 31, Trash: false }, "kind 37: its fields");
        var drag = RoundTrip<LiveDrag>("""{"id":5, "phase":"move", "window":4711, "x":0.5, "y":0.5, "modifiers":0, "offer":9, "files":[1], "folders":[]}""", "kind 38, the contract's example");
        Check.That(drag is { Id: 5, Phase: "move", Window: 4711, X: 0.5, Modifiers: 0, Offer: 9 } && drag.Files!.Single() == 1 && drag.Folders!.Count == 0, "kind 38: its fields");
        Check.That(new[] { LiveDrag.Phases.Start, LiveDrag.Phases.FilesSent, LiveDrag.Phases.Move, LiveDrag.Phases.Drop, LiveDrag.Phases.Cancel }
                   .SequenceEqual(new[] { "start", "files", "move", "drop", "cancel" }), "kind 38's phases");
        Check.That(new LiveDrag { Id = 1, Phase = "cancel" } is var cancel && Encoding.UTF8.GetString(Json.Encode(cancel)) is var cj && !cj.Contains("window") && cj.Contains("\"x\":0"),
                   "kind 38's cancel: no window written, x and y always (#66's non-optional fields)");

        // Kinds 48 and 49.
        var ticket = RoundTrip<DataTicket>("""{"token":"q83vEjRWeJA="}""", "kind 48");
        Check.Equal("q83vEjRWeJA=", ticket?.Token, "kind 48: its token");
        RoundTrip<DataHello>("""{"token":"q83vEjRWeJA="}""", "kind 49");
        Check.That(DataHello.Alpn == "sill-data/1" && DataHello.Within == 4, "the bulk connection: ALPN sill-data/1, its hello within 4 s");

        // Kind 32's generations.
        RoundTrip<ClipboardPayload>("""{"accepts":3}""", "kind 32, a subscription that takes generation 3");
        var rich = RoundTrip<ClipboardPayload>("""{"text":"hi","image":"iVBORw0KGgo=","html":"<b>hi</b> & 'you'","rtf":"e1xydGYx"}""", "kind 32, generation 2");
        Check.That(rich?.Image is { } png && png.AsSpan().SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), "kind 32's image: PNG bytes from base64");
        Check.Equal("{\\rtf1", rich?.Rtf is { } rtf ? Encoding.ASCII.GetString(rtf) : null, "kind 32's rtf: bytes from base64");
        Check.That(Encoding.UTF8.GetString(Json.Encode(new ClipboardPayload { Html = "<b>é</b>" })) == """{"html":"<b>é</b>"}""",
                   "an HTML fragment is written as Swift writes it: < > and é as they are, not six-byte escapes");
        var files = RoundTrip<ClipboardPayload>("""{"files":{"offer":12, "files":[{"name":"a.pdf","size":1048576}]}}""", "kind 32, generation 3 from the host");
        Check.That(files?.Files is { Offer: 12, Transfers: null } f && f.Files!.Single() is { Name: "a.pdf", Size: 1048576, Folder: null }, "kind 32's files: the offer and its file");
        RoundTrip<ClipboardPayload>("""{"files":{"transfers":[31,32]}}""", "kind 32, generation 3 from the device: the transfers it sent");
        Check.SameJson("{}", Json.Encode(new ClipboardPayload()), "a plain subscription is {}");
        Check.That(Capabilities.ClipboardText == 1 && Capabilities.ClipboardRich == 2 && Capabilities.ClipboardFiles == 3, "the clipboard's generations 1, 2, 3");

        // Kind 7's documents, kind 23's hello, kind 2's capabilities.
        RoundTrip<LaunchApp>("""{"bundleID":"com.apple.Preview","documents":["/Users/n/a.pdf"]}""", "kind 7 with documents");
        Check.SameJson("""{"bundleID":"com.apple.TextEdit"}""", Json.Encode(new LaunchApp { BundleID = "com.apple.TextEdit" }), "kind 7 without: as before");
        var hello = RoundTrip<Hello>("""{"appVersion":"0.5.1","build":"1","protocol":1,"device":"PC (Windows)","audio":["pcm-s16le"]}""", "kind 23 as this client sends it");
        Check.That(hello is { Coherence: null, IconSize: null } && hello.Audio!.Single() == "pcm-s16le", "kind 23: the sound's codec listed, no coherence yet");
        RoundTrip<Hello>("""{"coherence":1,"iconSize":256}""", "kind 23 with coherence and an icon size");
        Check.That(Capabilities.Coherence == 1 && Capabilities.WindowInputEvery == 2 && Capabilities.MaxIconSize == 512, "coherence 1, windowInput 2, icons at most 512 px");

        var full = WindowList.Parse(U("""
            {"macName":"Studio","windows":[{"id":1,"title":"t","appName":"a","bundleID":"b","width":10,"height":20}],"active":{"window":{"_0":1}},
             "keyRepeat":1,"sideStreams":4,"coherence":1,"windowInput":2,"fileDrop":1,"liveDrag":1,"data":1,"documents":1,"clipboard":3,"iconSize":512}
            """));
        Check.That(full is { KeyRepeat: 1, SideStreams: 4, Coherence: 1, WindowInput: 2, FileDrop: 1, LiveDrag: 1, Data: 1, Documents: 1, Clipboard: 3, IconSize: 512 },
                   "kind 2: every capability read");
        Check.That(full?.Active == Source.Window(1) && full.Windows.Single().BundleID == "b", "kind 2: the window and the active source as before");
        var older = WindowList.Parse(U("""{"macName":"Old","windows":[],"active":{"none":{}}}"""));
        Check.That(older is { KeyRepeat: null, SideStreams: null, Coherence: null, WindowInput: null, FileDrop: null, LiveDrag: null, Data: null, Documents: null, Clipboard: null, IconSize: null },
                   "kind 2 from an older host: every capability null");
        var pr66 = WindowList.Parse(U("""{"macName":"V","windows":[],"active":{"none":{}},"fileDrop":true,"liveDrag":true,"sideStreams":3}"""));
        Check.That(pr66 is { FileDrop: null, LiveDrag: null, SideStreams: 3 }, "kind 2 with #66's Bool flags: read as nothing (Ints here), the list still read");

        // Kind 8's window.
        Check.SameJson("""{"pointer":{"_0":"move","x":0.5,"y":0.25},"window":4711}""", InputEvents.InWindow(InputEvents.Pointer(PointerAction.Move, 0.5, 0.25), 4711),
                       "kind 8 aimed at a window: `window` beside the event");
        Check.SameJson("""{"key":{"hidUsage":4,"down":true,"modifiers":0},"window":7}""", InputEvents.InWindow(InputEvents.Key(4, true, 0), 7), "a key aimed at a window");

        // Kind 43's format and the codecs; kinds 16 and 17's Send Audio.
        var format = RoundTrip<AudioFormat>("""{"codec":"pcm-s16le","sampleRate":48000,"channels":2,"framesPerPacket":480,"epoch":3,"source":"test"}""", "kind 43's format for PCM");
        Check.That(format is { Codec: "pcm-s16le", SampleRate: 48000, Channels: 2, FramesPerPacket: 480, Epoch: 3, PrimingFrames: null, Cookie: null }, "kind 43's format: its fields");
        var eld = RoundTrip<AudioFormat>("""{"codec":"aac-eld","sampleRate":48000,"channels":2,"framesPerPacket":480,"primingFrames":240,"bitrate":128000,"epoch":0,"cookie":"+Bg=","source":"app","app":"Music"}""",
                                         "kind 43's format for AAC-ELD (read, not played)");
        Check.That(eld?.Cookie is { Length: 2 } c && c[0] == 0xF8 && c[1] == 0x18, "the cookie: bytes from base64");
        Check.Equal("pcm-s16le", AudioCodec.Choose(new[] { "pcm-s16le" }, new[] { "aac-eld", "pcm-s16le" }), "a host that makes both picks the device's pcm-s16le");
        Check.Equal(null, AudioCodec.Choose(new[] { "pcm-s16le" }, new[] { "aac-eld" }), "a host that makes only AAC-ELD sends this client no sound");
        Check.Equal(null, AudioCodec.Choose(null, new[] { "pcm-s16le" }), "no list, no sound");
        Check.That(AudioCodec.DevicePlays.SequenceEqual(new[] { "pcm-s16le" }), "this client lists pcm-s16le alone (no AAC-ELD decoder on Windows)");
        Check.That(Json.Decode<HostSettingsState>(U("""{"settings":{"maxFPS":60,"bitrate":1,"captureScale":2,"prioritizeSpeed":false,"virtualDisplay":true,"sendAudio":true},"persistent":true,"virtualDisplayAvailable":true,"softwareEncoder":false,"audioNote":"n"}"""))
                   is { Settings.SendAudio: true, AudioNote: "n" }, "kind 16's Send Audio and its note");
        Check.SameJson("""{"token":4,"sendAudio":false}""", Json.Encode(new HostSettingsChange { Token = 4, SendAudio = false }), "kind 17's Send Audio");
    }

    public static void Binary()
    {
        // Kinds 30 and 31.
        Check.Bytes(new byte[] { 0, 0, 0x12, 0x67, 0xAA, 0xBB }, SideStream.Pack(4711, new byte[] { 0xAA, 0xBB }), "kinds 30/31: the window ID big endian, then the payload");
        Check.That(SideStream.Unpack(new byte[] { 0, 0, 0x12, 0x67, 0xAA, 0xBB }) is var (w, p) && w == 4711 && p.SequenceEqual(new byte[] { 0xAA, 0xBB }), "unpacked: 4711 and the rest");
        Check.That(SideStream.Unpack(new byte[] { 0, 0, 1 }) == null, "shorter than the ID: null");
        Check.That(SideStream.Unpack(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }) is var (w2, p2) && w2 == uint.MaxValue && p2.Length == 0, "the ID alone: an empty payload (#66's unpack)");

        // Kind 34.
        var part = FileChunk.Pack(31, "a.pdf", 1_048_576, 524_288, new byte[] { 1, 2, 3 });
        Check.Bytes(new byte[] { 0, 0, 0, 31, 0, 5, (byte)'a', (byte)'.', (byte)'p', (byte)'d', (byte)'f', 0, 0, 0, 0, 0, 0x10, 0, 0, 0, 0, 0, 0, 0, 0x08, 0, 0, 1, 2, 3 }, part,
                    "kind 34: ID, name length and UTF-8, whole size and offset as UInt64 BE, the bytes");
        Check.That(FileChunk.Unpack(part) is { Id: 31, Name: "a.pdf", Total: 1_048_576, Offset: 524_288 } back && back.Bytes.SequenceEqual(new byte[] { 1, 2, 3 }), "kind 34 unpacked");
        Check.That(FileChunk.Unpack(part.AsSpan(0, 20)) == null, "kind 34 cut short: null");
        Check.That(FileChunk.Unpack(new byte[] { 0, 0, 0, 1, 0 }) == null, "kind 34 shorter than its name's length: null");
        var bad = (byte[])part.Clone();
        bad[6] = 0xFF;
        Check.That(FileChunk.Unpack(bad) == null, "kind 34 whose name is not UTF-8: null");
        Check.That(FileChunk.Unpack(FileChunk.Pack(1, "", 0, 0, ReadOnlySpan<byte>.Empty)) is { Name: "", Total: 0 } empty && empty.Bytes.Length == 0, "an empty file's one part");
        var longName = new string('a', 1023) + "é";
        var cut = FileChunk.Unpack(FileChunk.Pack(2, longName, 9, 0, new byte[] { 7 }));
        Check.That(cut is { Name.Length: 1023 } && cut.Value.Bytes.Single() == 7, "a name past 1 KB is cut at a character's edge, never half an é");
        Check.Equal(1024, FileChunk.Unpack(FileChunk.Pack(3, new string('b', 2000), 0, 0, ReadOnlySpan<byte>.Empty))?.Name.Length, "an ASCII name is cut to 1024 bytes");
        Check.Equal("Отчёт.pdf", FileChunk.Unpack(FileChunk.Pack(4, "Отчёт.pdf", 1, 0, new byte[] { 0 }))?.Name, "a Cyrillic name round trip");
    }

    public static void AudioPackets()
    {
        var packets = new Sill.Windows.Protocol.AudioPackets(7, 0x01020304, true, new[] { new byte[] { 1, 2 }, new byte[] { 3 } });
        var bytes = packets.Serialize();
        Check.Bytes(new byte[] { 2, 0, 7, 1, 2, 3, 4, 1, 2, 0, 2, 1, 2, 0, 1, 3 }, bytes, "type 2 · epoch · seq · flags · count · (length · bytes)×count, big endian (Audio.swift)");
        var parsed = AudioMessage.Parse(bytes) as AudioMessage.Packets;
        Check.That(parsed?.Value is { Epoch: 7, Seq: 0x01020304, SegmentStart: true } v && v.Packets.Count == 2 && v.Packets[0].SequenceEqual(new byte[] { 1, 2 }) && v.Packets[1].Single() == 3,
                   "parsed back: the epoch, seq, segment start and both packets");
        Check.Bytes(new byte[] { 0, 5 }, new Sill.Windows.Protocol.AudioPackets(65_541, 0, false, new[] { new byte[] { 9 } }).Serialize().AsSpan(1, 2), "the epoch is written modulo 65,536");
        var flags = (byte[])bytes.Clone();
        flags[7] = 0xFE;
        Check.That(AudioMessage.Parse(flags) is AudioMessage.Packets { Value.SegmentStart: false }, "flags' bits 1 to 7 are ignored");

        Check.That(AudioMessage.Parse(ReadOnlySpan<byte>.Empty) == null, "an empty payload: null");
        Check.That(AudioMessage.Parse(new byte[] { 3, 0, 0 }) == null, "a type this build does not know: null (skipped)");
        Check.That(AudioMessage.Parse(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0 }) == null, "packets cut short of their 9-byte header: null");
        Check.That(AudioMessage.Parse(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0 }) == null, "a count of 0: null");
        Check.That(AudioMessage.Parse(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 1, 0, 5, 1, 2 }) == null, "a length past the end: null");
        Check.That(AudioMessage.Parse(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 9 }) == null, "bytes left over: null");
        Check.That(AudioMessage.Parse(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 1, 0 }) == null, "a length cut short: null");
        var huge = new Sill.Windows.Protocol.AudioPackets(0, 0, false, Enumerable.Range(0, 9).Select(_ => new byte[1920]).ToList()).Serialize();
        Check.That(huge.Length > AudioMessage.MaxPacketsPayload && AudioMessage.Parse(huge) == null, $"packets over 16,384 bytes ({huge.Length}): null, dropped whole");
        var eight = new Sill.Windows.Protocol.AudioPackets(0, 0, false, Enumerable.Range(0, 8).Select(_ => new byte[1920]).ToList()).Serialize();
        Check.That(AudioMessage.Parse(eight) is AudioMessage.Packets { Value.Packets.Count: 8 }, $"eight 10 ms PCM packets in one message ({eight.Length} bytes) are within the cap");

        var format = AudioMessage.Serialize(new AudioFormat { Codec = AudioCodec.PcmS16LE, SampleRate = 48000, Channels = 2, FramesPerPacket = 480, Epoch = 1 });
        Check.Equal((byte)1, format[0], "a format's type byte is 1");
        Check.That(AudioMessage.Parse(format) is AudioMessage.Format { Value: { Codec: "pcm-s16le", SampleRate: 48000, Channels: 2, FramesPerPacket: 480, Epoch: 1 } }, "a format parsed back");
        Check.That(AudioMessage.Parse(new byte[] { 1, (byte)'{', (byte)'}' }) is AudioMessage.Format { Value.Codec: null }, "a format of {} decodes, every field optional");
        Check.That(AudioMessage.Parse(new byte[] { 1, (byte)'x' }) == null, "a format that is not JSON: null");
        var bigFormat = new byte[4097];
        bigFormat[0] = 1;
        Check.That(AudioMessage.Parse(bigFormat) == null, "a format over 4,096 bytes: null");
    }

    /// <summary>The older protocol files the checks compile anyway: a few of their edges.</summary>
    public static void PairingAndHevc()
    {
        Check.Equal("-_8", Base64Url.Encode(new byte[] { 0xFB, 0xFF }), "base64url: - and _ for + and /, no padding");
        Check.That(Base64Url.Decode("-_8") is { } d && d.SequenceEqual(new byte[] { 0xFB, 0xFF }), "… and back");
        Check.That(Base64Url.Decode("-_8=") == null && Base64Url.Decode("+/8") == null && Base64Url.Decode("-_9") == null, "padding, + and /, and a non-canonical last digit refused");

        // The 12-digit code ends in a Damm check digit: exactly one last digit of ten completes it.
        var valid = Enumerable.Range(0, 10).Select(c => "35810274996" + c).Where(c => PairingCode.Check(c).code != null).ToList();
        Check.Equal(1, valid.Count, "exactly one check digit of ten makes a code");
        var code = valid.Single();
        Check.Equal(code, PairingCode.Check($"{code[..4]} {code[4..8]}-{code[8..]}").code, "spaces and hyphens are ignored");
        bool everyTypo = true;
        for (int i = 0; i < 12; i++)
            for (char c = '0'; c <= '9'; c++)
                if (c != code[i] && PairingCode.Check(code[..i] + c + code[(i + 1)..]).problem != PairingCode.Problem.Typo) everyTypo = false;
        Check.That(everyTypo, "every single wrong digit is caught as a typo");
        Check.Equal(PairingCode.Problem.Length, PairingCode.Check("12345").problem, "too short: a length problem");

        // Length-prefixed NALs to Annex B, the parameter sets first for a keyframe.
        var sets = new ParameterSets(4, new[] { new byte[] { 0x40, 1 }, new byte[] { 0x42, 2 }, new byte[] { 0x44, 3 } });
        Check.That(ParameterSets.Parse(sets.Encode()) is { NalLengthSize: 4 } back && back.SameAs(sets), "kind 0's parameter sets round trip");
        var frame = new byte[] { 0, 0, 0, 3, 0x26, 0x01, 0xAA, 0, 0, 0, 2, 0x02, 0x01 };
        Check.Bytes(new byte[] { 0, 0, 0, 1, 0x26, 0x01, 0xAA, 0, 0, 0, 1, 0x02, 0x01 }, Hevc.ToAnnexB(frame, 4, null), "two NALs: each length prefix becomes a start code");
        Check.Bytes(new byte[] { 0, 0, 0, 1, 0x40, 1, 0, 0, 0, 1, 0x42, 2, 0, 0, 0, 1, 0x44, 3, 0, 0, 0, 1, 0x26, 0x01, 0xAA, 0, 0, 0, 1, 0x02, 0x01 },
                    Hevc.ToAnnexB(frame, 4, sets), "a keyframe: VPS, SPS and PPS before it");
        Check.Equal(0, Hevc.ToAnnexB(new byte[] { 0, 0, 0, 9, 1 }, 4, null).Length, "a length past the end: nothing made of it");
    }
}
