using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sill.Windows.Protocol;

// The messages this fork adds so that a PC shows several Mac windows at once, each a window of its
// own, with files and a richer clipboard (docs/coherence-protocol.md, the wire contract; why and in
// what order: docs/coherence-plan.md). Kinds 29–31 and 34–38, 41 keep the meanings upstream's pull
// request #66 gives them (its SideStreams.swift); 44's payload and 45 onwards are this fork's.
//
// Compatibility.swift's rules hold: JSON for control messages, every field optional and left out
// when null (Json.Options), strings rather than enums, a field never renamed or retyped, a kind an
// older peer does not know skipped by it, and a kind or field sent only to a peer that said it takes
// it (WindowList's generations from the host, Hello's from the device). The JSON names are written
// out on each property, exactly as the contract has them, whatever the naming policy. Binary layouts
// are big endian, as the 14-byte header.
//
// Types only, for the milestones after M0: nothing here sends or acts. Pure (no WinForms, no Win32),
// so WindowsClient/Sill.Checks compiles it on any system.

/// <summary>What a host or a device says it takes, by the generations the contract names
/// (`WindowList`'s fields from the host, `Hello`'s from the device).</summary>
public static class Capabilities
{
    /// <summary>`WindowList.coherence` and `Hello.coherence`: kinds 45 and 46, kind 29's coherence
    /// fields, kind 15's `activate` and `restore`, kind 44's `window`.</summary>
    public const int Coherence = 1;
    /// <summary>`WindowList.windowInput`: every kind 8 event may carry `window` (1, #66's, was moves
    /// and scrolls only).</summary>
    public const int WindowInputEvery = 2;
    /// <summary>`WindowList.fileDrop`: kinds 34–37.</summary>
    public const int FileDrop = 1;
    /// <summary>`WindowList.liveDrag`: kind 38.</summary>
    public const int LiveDrag = 1;
    /// <summary>`WindowList.data`: kind 48 and the `sill-data/1` connection.</summary>
    public const int Data = 1;
    /// <summary>`WindowList.documents`: `LaunchApp.documents`.</summary>
    public const int Documents = 1;
    /// <summary>`WindowList.clipboard`: 1 text (main's), 2 adds image, html and rtf, 3 adds files.</summary>
    public const int ClipboardText = 1, ClipboardRich = 2, ClipboardFiles = 3;
    /// <summary>`WindowList.keyRepeat`: the host repeats a held key itself (KeyRepeat.generation).</summary>
    public const int KeyRepeat = 1;
    /// <summary>The largest `Hello.iconSize` a device asks for, in pixels.</summary>
    public const int MaxIconSize = 512;
}

// MARK: video: side streams (kinds 29, 30, 31)

/// <summary>Kind 29, device → host: the windows to stream besides (or, with `coherence`, instead of)
/// the picked one, each with its own capture and encoder on the host. The whole set each time; a
/// window left out stops. Only to a host whose list says `sideStreams`.</summary>
public sealed class SideStreamRequest
{
    [JsonPropertyName("windows")] public List<uint> Windows { get; set; } = new();
    /// <summary>Frames a second for each window but the focused one (<see cref="DefaultFPS"/>, at most <see cref="MaxFPS"/>).</summary>
    [JsonPropertyName("fps")] public int? Fps { get; set; }
    /// <summary>Capture each window with what belongs to it (menus, popovers, sheets, the system's panels over it).</summary>
    [JsonPropertyName("wholeApp")] public bool? WholeApp { get; set; }
    /// <summary>The device's largest screen in points, [width, height]: the least room the host keeps for each window.</summary>
    [JsonPropertyName("screen")] public double[]? Screen { get; set; }
    /// <summary>Every listed window is a peer: none has to be picked with kind 6.</summary>
    [JsonPropertyName("coherence")] public bool? Coherence { get; set; }
    /// <summary>The window the device has in front, streamed at <see cref="FocusFPS"/>.</summary>
    [JsonPropertyName("focus")] public uint? Focus { get; set; }
    [JsonPropertyName("focusFPS")] public int? FocusFPS { get; set; }
    /// <summary>Bundle IDs whose new windows join the set by themselves (kind 45's `joined`).</summary>
    [JsonPropertyName("apps")] public List<string>? Apps { get; set; }
    /// <summary>Every app's new windows join.</summary>
    [JsonPropertyName("all")] public bool? All { get; set; }
    /// <summary>Windows minimized or covered here: the host drops their frames and keeps their capture.</summary>
    [JsonPropertyName("hidden")] public List<uint>? Hidden { get; set; }
    /// <summary>The device's pixels per point, so the host captures at the device's size.</summary>
    [JsonPropertyName("scale")] public double? Scale { get; set; }

    public const int DefaultFPS = 15, MaxFPS = 30, DefaultFocusFPS = 60, MaxFocusFPS = 120;
}

/// <summary>Kinds 30 and 31, host → device: the window ID (UInt32 BE), then exactly what kinds 0 and
/// 1 carry for that window (#66's SideStreamRequest.pack and unpack).</summary>
public static class SideStream
{
    public static byte[] Pack(uint window, ReadOnlySpan<byte> payload)
    {
        var d = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(d, window);
        payload.CopyTo(d.AsSpan(4));
        return d;
    }

    /// <summary>The window and the rest; null when shorter than the ID.</summary>
    public static (uint window, byte[] payload)? Unpack(ReadOnlySpan<byte> d) =>
        d.Length >= 4 ? (BinaryPrimitives.ReadUInt32BigEndian(d), d[4..].ToArray()) : null;
}

/// <summary>Kind 44, device → host: the picture was interrupted and waits for a keyframe
/// (KeyframeRecovery decides when). Empty: the picked stream's, to every host (one from before kind
/// 44 skips it). With `window`, only that window's encoder, and only to a host whose list says
/// `coherence` 1.</summary>
public sealed class KeyframeRequest
{
    [JsonPropertyName("window")] public uint? Window { get; set; }

    /// <summary>The payload to send: `{"window":…}` for a window of a host that takes it, else empty.</summary>
    public static byte[] Payload(uint? window, int? hostCoherence) =>
        window is { } w && (hostCoherence ?? 0) >= Capabilities.Coherence ? Json.Encode(new KeyframeRequest { Window = w }) : Array.Empty<byte>();

    /// <summary>What a host reads: an empty payload is the picked stream's; null for JSON that does not decode.</summary>
    public static KeyframeRequest? Parse(ReadOnlySpan<byte> payload) =>
        payload.IsEmpty ? new KeyframeRequest() : Json.Decode<KeyframeRequest>(payload);
}

// MARK: window state (kinds 45, 46, and 15's and 8's additions)

/// <summary>Kind 45, host → device (to a device whose hello says `coherence` 1): the Mac's windows,
/// modelled on RDP RemoteApp's window orders. `full`: every window the device should have; else only
/// those that changed, and `removed`. A device drops a message whose `gen` is older than the last it
/// applied.</summary>
public sealed class WindowState
{
    [JsonPropertyName("gen")] public long Gen { get; set; }
    [JsonPropertyName("full")] public bool? Full { get; set; }
    /// <summary>The `token` of the kind 46 this answers.</summary>
    [JsonPropertyName("answering")] public int? Answering { get; set; }
    [JsonPropertyName("windows")] public List<Window>? Windows { get; set; }
    [JsonPropertyName("removed")] public List<uint>? Removed { get; set; }
    /// <summary>The Mac's stacking, front to back, for information.</summary>
    [JsonPropertyName("order")] public List<uint>? Order { get; set; }
    /// <summary>The Mac's key window, for information.</summary>
    [JsonPropertyName("focus")] public uint? Focus { get; set; }
    /// <summary>macOS's Secure Event Input is on (a password field): typed keys may not land.</summary>
    [JsonPropertyName("secureInput")] public bool? SecureInput { get; set; }

    /// <summary>Whether a message with this generation is newer than the last one applied (none yet: any is).</summary>
    public static bool Newer(long gen, long? lastApplied) => lastApplied is not { } last || gen > last;

    /// <summary>One window. Sizes and rects in points.</summary>
    public sealed class Window
    {
        [JsonPropertyName("id")] public uint Id { get; set; }
        [JsonPropertyName("app")] public string? App { get; set; }
        [JsonPropertyName("appName")] public string? AppName { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        /// <summary><see cref="Roles"/>; one a device does not know is a window (<see cref="RoleOrWindow"/>).</summary>
        [JsonPropertyName("role")] public string? Role { get; set; }
        /// <summary>A top-level window's size; its place on Sill's desk means nothing to the device.</summary>
        [JsonPropertyName("size")] public double[]? Size { get; set; }
        [JsonPropertyName("min")] public double[]? Min { get; set; }
        [JsonPropertyName("max")] public double[]? Max { get; set; }
        [JsonPropertyName("resizable")] public bool? Resizable { get; set; }
        [JsonPropertyName("focused")] public bool? Focused { get; set; }
        [JsonPropertyName("minimized")] public bool? Minimized { get; set; }
        [JsonPropertyName("fullScreen")] public bool? FullScreen { get; set; }
        [JsonPropertyName("modal")] public bool? Modal { get; set; }
        /// <summary>The height of a plain title bar the device may crop and replace (0 or absent: keep it).</summary>
        [JsonPropertyName("titleBar")] public double? TitleBar { get; set; }
        /// <summary>The traffic lights, from the window's top-left.</summary>
        [JsonPropertyName("controls")] public TrafficLights? Controls { get; set; }
        /// <summary>The window came by itself (kind 29's `apps` or `all`).</summary>
        [JsonPropertyName("joined")] public bool? Joined { get; set; }
        /// <summary>A child's owner; then <see cref="Rect"/> is relative to the owner's top-left.</summary>
        [JsonPropertyName("owner")] public uint? Owner { get; set; }
        /// <summary>[x, y, width, height].</summary>
        [JsonPropertyName("rect")] public double[]? Rect { get; set; }
        /// <summary>The child shows inside its owner's picture (`wholeApp`); false: it spills out and
        /// needs a side stream of its own.</summary>
        [JsonPropertyName("inCrop")] public bool? InCrop { get; set; }

        public string RoleOrWindow => Role is { } r && Roles.All.Contains(r) ? r : Roles.Window;
    }

    /// <summary>Each [x, y, width, height] from the window's top-left.</summary>
    public sealed class TrafficLights
    {
        [JsonPropertyName("close")] public double[]? Close { get; set; }
        [JsonPropertyName("minimize")] public double[]? Minimize { get; set; }
        [JsonPropertyName("zoom")] public double[]? Zoom { get; set; }
    }

    public static class Roles
    {
        public const string Window = "window", Sheet = "sheet", Dialog = "dialog", Panel = "panel", Menu = "menu", Popover = "popover", Tooltip = "tooltip";
        public static readonly IReadOnlySet<string> All = new HashSet<string> { Window, Sheet, Dialog, Panel, Menu, Popover, Tooltip };
    }
}

/// <summary>Kind 46, device → host: size a window, in points, through Accessibility; moves are the
/// device's alone. Sent at the end of a resize and at most about 20 times a second during one,
/// answered by a kind 45 with the size taken and `answering`.</summary>
public sealed class PlaceWindow
{
    [JsonPropertyName("id")] public uint Id { get; set; }
    [JsonPropertyName("size")] public double[] Size { get; set; } = Array.Empty<double>();
    [JsonPropertyName("token")] public int? Token { get; set; }

    /// <summary>The least time between two during one resize, in seconds.</summary>
    public const double MinInterval = 0.05;
}

/// <summary>Kind 15's actions (WindowCommand.action). `activate` and `restore` only to a host whose
/// list says `coherence` 1.</summary>
public static class WindowActions
{
    public const string Close = "close", Minimize = "minimize", FullScreen = "fullScreen";
    /// <summary>Bring the window's app forward and make the window key, as a click would.</summary>
    public const string Activate = "activate";
    /// <summary>Un-minimize on the Mac.</summary>
    public const string Restore = "restore";
}

/// <summary>Kind 41, host → device: the person at the Mac took these windows back; the device closes
/// its windows of them.</summary>
public sealed class TakenBack
{
    [JsonPropertyName("windows")] public List<uint> Windows { get; set; } = new();
}

// MARK: files and drag and drop (kinds 34–38)

/// <summary>Kind 34, either way: a part of a file. Transfer ID (UInt32 BE), name (UInt16 BE length,
/// UTF-8), the whole size and this part's offset (UInt64 BE each), the bytes (#66's FileChunk).</summary>
public static class FileChunk
{
    /// <summary>The most bytes a part carries: 512 KB, well within the Mac's 1 MiB a device's message.</summary>
    public const int MaxBytes = 512 * 1024;
    /// <summary>The most of a name a part carries, in UTF-8 bytes.</summary>
    public const int MaxNameBytes = 1024;
    /// <summary>What comes before the name and after it: the ID and its length, the size and offset.</summary>
    public const int FixedBytes = 22;

    public readonly record struct Part(uint Id, string Name, ulong Total, ulong Offset, byte[] Bytes);

    /// <summary>The name is cut to <see cref="MaxNameBytes"/> at a character's edge, so it still
    /// reads as UTF-8 (#66 cuts at a byte, which can leave half a character).</summary>
    public static byte[] Pack(uint id, string name, ulong total, ulong offset, ReadOnlySpan<byte> bytes)
    {
        var n = Utf8Prefix(name, MaxNameBytes);
        var d = new byte[FixedBytes + n.Length + bytes.Length];
        BinaryPrimitives.WriteUInt32BigEndian(d, id);
        BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(4), (ushort)n.Length);
        n.CopyTo(d.AsSpan(6));
        BinaryPrimitives.WriteUInt64BigEndian(d.AsSpan(6 + n.Length), total);
        BinaryPrimitives.WriteUInt64BigEndian(d.AsSpan(14 + n.Length), offset);
        bytes.CopyTo(d.AsSpan(FixedBytes + n.Length));
        return d;
    }

    /// <summary>Null when cut short or the name is not UTF-8.</summary>
    public static Part? Unpack(ReadOnlySpan<byte> d)
    {
        if (d.Length < 6) return null;
        uint id = BinaryPrimitives.ReadUInt32BigEndian(d);
        int n = BinaryPrimitives.ReadUInt16BigEndian(d[4..]);
        if (d.Length < FixedBytes + n) return null;
        string name;
        try { name = new UTF8Encoding(false, true).GetString(d.Slice(6, n)); }
        catch (DecoderFallbackException) { return null; }
        return new Part(id, name, BinaryPrimitives.ReadUInt64BigEndian(d[(6 + n)..]), BinaryPrimitives.ReadUInt64BigEndian(d[(14 + n)..]),
                        d[(FixedBytes + n)..].ToArray());
    }

    static byte[] Utf8Prefix(string s, int max)
    {
        var all = Encoding.UTF8.GetBytes(s);
        if (all.Length <= max) return all;
        int end = max;
        while (end > 0 && (all[end] & 0xC0) == 0x80) end--;   // back to the start of the cut character
        return all[..end];
    }
}

/// <summary>Kind 35, device → host: those transfers, all sent, dropped at a point (fractions) of the
/// window's picture. With `offer`, `files` index that drag offer's files.</summary>
public sealed class FileDrop
{
    [JsonPropertyName("files")] public List<uint> Files { get; set; } = new();
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("offer")] public uint? Offer { get; set; }
    [JsonPropertyName("window")] public uint? Window { get; set; }
    /// <summary>The files that are folders sent zipped, unpacked before the drop.</summary>
    [JsonPropertyName("folders")] public List<uint>? Folders { get; set; }
}

/// <summary>A file by name and size: in kind 36's offer, and in kind 32's clipboard files.</summary>
public sealed class OfferedFile
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public ulong Size { get; set; }
    /// <summary>A folder, sent zipped when asked for (its size then the zip's).</summary>
    [JsonPropertyName("folder")] public bool? Folder { get; set; }
}

/// <summary>Kind 36, host → device: an app on the Mac is dragging these files under the device's held button.</summary>
public sealed class DragOffer
{
    [JsonPropertyName("id")] public uint Id { get; set; }
    [JsonPropertyName("files")] public List<OfferedFile> Files { get; set; } = new();
}

/// <summary>Kind 37, device → host: send one offered file back as kind 34 parts with `transfer` as
/// their ID (also for clipboard files, whose offers share the ID space).</summary>
public sealed class FileRequest
{
    [JsonPropertyName("offer")] public uint Offer { get; set; }
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("transfer")] public uint Transfer { get; set; }
    /// <summary>Dragged to the device's bin: the host moves it to its own Trash instead.</summary>
    [JsonPropertyName("trash")] public bool? Trash { get; set; }
}

/// <summary>Kind 38, device → host: a drag of files over the Mac's windows followed live, one `id`
/// in <see cref="Phases"/>. Only to a host whose list says `liveDrag` 1.</summary>
public sealed class LiveDrag
{
    [JsonPropertyName("id")] public uint Id { get; set; }
    [JsonPropertyName("phase")] public string Phase { get; set; } = "";
    [JsonPropertyName("window")] public uint? Window { get; set; }
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    /// <summary>MacModifiers' bits, as kind 8's.</summary>
    [JsonPropertyName("modifiers")] public ulong? Modifiers { get; set; }
    [JsonPropertyName("offer")] public uint? Offer { get; set; }
    [JsonPropertyName("files")] public List<uint>? Files { get; set; }
    [JsonPropertyName("folders")] public List<uint>? Folders { get; set; }

    public static class Phases
    {
        public const string Start = "start", FilesSent = "files", Move = "move", Drop = "drop", Cancel = "cancel";
    }
}

// MARK: the bulk connection (kinds 48, 49)

/// <summary>Kind 48, host → device on the session, once after the window list, to a device whose
/// hello says `coherence` 1 from a host whose list says `data` 1: the key to a second connection.</summary>
public sealed class DataTicket
{
    /// <summary>Base64 of 32 random bytes.</summary>
    [JsonPropertyName("token")] public string Token { get; set; } = "";
}

/// <summary>Kind 49, device → host: the first message of the `sill-data/1` connection, within
/// <see cref="Within"/> seconds, with the session's ticket.</summary>
public sealed class DataHello
{
    [JsonPropertyName("token")] public string Token { get; set; } = "";

    /// <summary>The bulk connection's ALPN, beside `sill/1` and `sill-pair/1`.</summary>
    public const string Alpn = "sill-data/1";
    public const double Within = 4;
}

// MARK: kind 32's later generations

/// <summary>Kind 32's `files` (generation 3): files on the sender's clipboard, fetched only when
/// pasted. From the host: an `offer` and its `files`, fetched with kind 37. From the device: the
/// `transfers` it sent as kind 34 parts first, which the host puts on the Mac's pasteboard.</summary>
public sealed class ClipboardFiles
{
    [JsonPropertyName("offer")] public uint? Offer { get; set; }
    [JsonPropertyName("files")] public List<OfferedFile>? Files { get; set; }
    [JsonPropertyName("transfers")] public List<uint>? Transfers { get; set; }
}

// MARK: kind 8's window

public static partial class InputEvents
{
    /// <summary>An event (one of this class's payloads) aimed at a window, its coordinates fractions
    /// of that window's picture: the event's JSON with `window` beside its one key, as #66's
    /// WindowInput writes it. Only to a host whose list says `windowInput` 2 (1 takes moves and
    /// scrolls only).</summary>
    public static byte[] InWindow(byte[] eventJson, uint window)
    {
        if (JsonNode.Parse(eventJson) is not JsonObject o) return eventJson;
        o["window"] = window;
        return Json.Encode(o);
    }
}
