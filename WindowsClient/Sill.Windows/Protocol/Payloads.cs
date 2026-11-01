using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sill.Windows.Protocol;

// The JSON payloads, as Sources/StreamProtocol encodes and decodes them (Swift's synthesized
// Codable: property names are the keys; an enum case with values is {"case":{"_0":…,"label":…}}).
// HostSettings.swift's rules hold here too: every field this client reads may be missing (a field
// the Mac added later), and unknown keys are ignored.

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);
    public static byte[] Encode(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());

    public static T? Decode<T>(ReadOnlySpan<byte> data) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(data, Options); }
        catch (JsonException) { return null; }
    }
}

// MARK: what the Mac streams

/// <summary>StreamSource: nothing, one window (its CGWindowID), or the Mac's main display.</summary>
public readonly record struct Source(SourceKind Kind, uint WindowID = 0)
{
    public static readonly Source None = new(SourceKind.None);
    public static readonly Source Desktop = new(SourceKind.Desktop);
    public static Source Window(uint id) => new(SourceKind.Window, id);

    public JsonNode ToJson() => Kind switch
    {
        SourceKind.Desktop => new JsonObject { ["desktop"] = new JsonObject() },
        SourceKind.Window => new JsonObject { ["window"] = new JsonObject { ["_0"] = WindowID } },
        _ => new JsonObject { ["none"] = new JsonObject() },
    };

    public static Source Parse(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return None;
        if (e.TryGetProperty("desktop", out _)) return Desktop;
        if (e.TryGetProperty("window", out var w) && w.ValueKind == JsonValueKind.Object &&
            w.TryGetProperty("_0", out var id) && id.TryGetUInt32(out var v)) return Window(v);
        return None;
    }

    public override string ToString() => Kind switch { SourceKind.Desktop => "Desktop", SourceKind.Window => $"window {WindowID}", _ => "nothing" };
}

public enum SourceKind { None, Desktop, Window }

public sealed class WindowInfo
{
    public uint Id { get; set; }
    public string Title { get; set; } = "";
    public string AppName { get; set; } = "";
    public string BundleID { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class AppInfo
{
    public string Name { get; set; } = "";
    public string BundleID { get; set; } = "";
}

/// <summary>Kind 2. `active` is a StreamSource, read by hand.</summary>
public sealed class WindowList
{
    public string MacName { get; init; } = "Mac";
    public List<WindowInfo> Windows { get; init; } = new();
    public Source Active { get; init; }
    public string? LaunchID { get; init; }
    public string? HostVersion { get; init; }
    public int? Protocol { get; init; }
    public int? Gestures { get; init; }
    public int? Clipboard { get; init; }

    public static WindowList? Parse(ReadOnlySpan<byte> data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data.ToArray());
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            var windows = r.TryGetProperty("windows", out var w) ? w.Deserialize<List<WindowInfo>>(Json.Options) ?? new() : new();
            return new WindowList
            {
                MacName = Str(r, "macName") ?? "Mac",
                Windows = windows,
                Active = r.TryGetProperty("active", out var a) ? Source.Parse(a) : Source.None,
                LaunchID = Str(r, "launchID"),
                HostVersion = Str(r, "hostVersion"),
                Protocol = Int(r, "protocol"),
                Gestures = Int(r, "gestures"),
                Clipboard = Int(r, "clipboard"),
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return null; }
    }

    static string? Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static int? Int(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
}

// MARK: the Mac's settings (kinds 16 and 17)

public sealed class StreamSettings
{
    public int MaxFPS { get; set; }
    public long Bitrate { get; set; }
    public double CaptureScale { get; set; }
    public bool PrioritizeSpeed { get; set; }
    public bool VirtualDisplay { get; set; }
    public bool? DirectWireless { get; set; }
}

public sealed class RunningStream
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int Fps { get; set; }
    public int Mbps { get; set; }
    public bool OnVirtualDisplay { get; set; }
}

public sealed class AwayQuality
{
    public long HomeBitrate { get; set; }
    public double HomeCaptureScale { get; set; }
    public long AwayBitrate { get; set; }
    public double AwayCaptureScale { get; set; }
    public bool ThisConnectionAway { get; set; }
    public bool AwayRunning { get; set; }
}

public sealed class LinkReport
{
    public string State { get; set; } = "";
    public int WithheldPerSecond { get; set; }
    public long Bitrate { get; set; }
    public int? CarriedKbps { get; set; }
    public long? SuggestedBitrate { get; set; }
    public double? SuggestedCaptureScale { get; set; }
}

public sealed class HostSettingsState
{
    public StreamSettings? Settings { get; set; }
    public bool Persistent { get; set; }
    public bool VirtualDisplayAvailable { get; set; }
    public string? VirtualDisplayNote { get; set; }
    public bool SoftwareEncoder { get; set; }
    public RunningStream? Stream { get; set; }
    public int? Answering { get; set; }
    public AwayQuality? Away { get; set; }
    public LinkReport? Link { get; set; }
}

/// <summary>Kind 17: only the fields one control changed, plus a token.</summary>
public sealed class HostSettingsChange
{
    public int? Token { get; set; }
    public int? MaxFPS { get; set; }
    public long? Bitrate { get; set; }
    public double? CaptureScale { get; set; }
    public bool? PrioritizeSpeed { get; set; }
    public bool? VirtualDisplay { get; set; }
    public bool? DirectWireless { get; set; }
}

/// <summary>The Mac's presets (QualityPreset), per 60 fps, ascending.</summary>
public static class QualityPresets
{
    public static readonly (string Name, long Bitrate)[] All =
    {
        ("Low", 4_000_000), ("Efficient", 8_000_000), ("Balanced", 15_000_000), ("High", 25_000_000),
        ("Pro", 40_000_000), ("Ultra", 80_000_000), ("Extreme", 150_000_000),
    };

    public static string Name(long bitrate) =>
        All.FirstOrDefault(p => p.Bitrate == bitrate).Name ?? $"{(bitrate % 1_000_000 == 0 ? (bitrate / 1_000_000).ToString(CultureInfo.InvariantCulture) : (bitrate / 1e6).ToString("0.0", CultureInfo.InvariantCulture))} Mbps";

    public static string Title(long bitrate) => All.Any(p => p.Bitrate == bitrate) ? $"{Name(bitrate)} — {bitrate / 1_000_000} Mbps" : $"Custom — {Name(bitrate)}";
}

// MARK: who the device is, and how a session ends

/// <summary>Kind 23, the first message of every session connection.</summary>
public sealed class Hello
{
    public string? AppVersion { get; set; }
    public string? Build { get; set; }
    public int? Protocol { get; set; }
    public string? Device { get; set; }
}

/// <summary>Kind 22.</summary>
public sealed class Goodbye
{
    public string Reason { get; set; } = "";
    public string? Message { get; set; }
    public string? MinimumVersion { get; set; }
    public bool? Reconnect { get; set; }
}

/// <summary>Kind 12, once a second.</summary>
public sealed class ClientStats
{
    public int Fps { get; set; }
    public int FrameAgeMs { get; set; }
    public int RttMs { get; set; }
    public string Device { get; set; } = "";
    public int? FrameAgeMaxMs { get; set; }
    public int? RttMaxMs { get; set; }
}

/// <summary>Kind 9: the panel's size in the device's points (here: DIPs) and the wanted rate.</summary>
public sealed class Viewport
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double? Scale { get; set; }
    public int? Fps { get; set; }
}

public sealed class MacPointer
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public bool? Inside { get; set; }
    public int? Seen { get; set; }
}

public sealed class ClipboardPayload
{
    public string? Text { get; set; }
    public bool? Stop { get; set; }
}

public sealed class LaunchApp
{
    public string BundleID { get; set; } = "";
}

public sealed class WindowCommand
{
    public uint Id { get; set; }
    public string Action { get; set; } = "";   // close, minimize, fullScreen
}

// MARK: pairing (kinds 18–20)

public sealed class PairRequest
{
    public int V { get; set; } = 1;
    public string Method { get; set; } = "";
    public string Proof { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Model { get; set; }
    public bool? Cable { get; set; }
}

public sealed class PairResult
{
    public bool Ok { get; set; }
    public string? Proof { get; set; }
    public string? MacID { get; set; }
    public string? Name { get; set; }
    public string? RecognitionKey { get; set; }
    public string? Reason { get; set; }
    public int? TriesLeft { get; set; }
    public double? RetryAfter { get; set; }
    public string? Method { get; set; }
    public string? Message { get; set; }
}

public sealed class SignedMacInfo
{
    public string Info { get; set; } = "";
    public string Key { get; set; } = "";
    public string Sig { get; set; } = "";
}

public sealed class MacInfo
{
    public int V { get; set; }
    public string MacID { get; set; } = "";
    public string Name { get; set; } = "";
    public double IssuedAt { get; set; }
    public bool RemoteAccess { get; set; }
    public int RemotePort { get; set; }
    public bool Internet { get; set; }
}

// MARK: input (kind 8), as Input.swift's InputEvent encodes it

public enum PointerAction { Move, LeftDown, LeftUp, RightDown, RightUp }

public static class InputEvents
{
    static string Name(PointerAction a) => a switch
    {
        PointerAction.Move => "move", PointerAction.LeftDown => "leftDown", PointerAction.LeftUp => "leftUp",
        PointerAction.RightDown => "rightDown", _ => "rightUp",
    };

    static string F(double v) => (double.IsFinite(v) ? Math.Round(v, 5) : 0).ToString("R", CultureInfo.InvariantCulture);

    // Written by hand: small, frequent, and the shapes are fixed.
    public static byte[] Pointer(PointerAction action, double x, double y) =>
        Encoding.UTF8.GetBytes($"{{\"pointer\":{{\"_0\":\"{Name(action)}\",\"x\":{F(x)},\"y\":{F(y)}}}}}");

    public static byte[] Scroll(double x, double y, double dx, double dy) =>
        Encoding.UTF8.GetBytes($"{{\"scroll\":{{\"x\":{F(x)},\"y\":{F(y)},\"dx\":{F(dx)},\"dy\":{F(dy)}}}}}");

    public static byte[] Key(ushort hidUsage, bool down, ulong modifiers) =>
        Encoding.UTF8.GetBytes($"{{\"key\":{{\"hidUsage\":{hidUsage},\"down\":{(down ? "true" : "false")},\"modifiers\":{modifiers}}}}}");

    public static byte[] Text(string text) =>
        Json.Encode(new JsonObject { ["text"] = new JsonObject { ["_0"] = text } });
}

/// <summary>UIKeyModifierFlags bits, which the Mac reads as CGEventFlags' positions.</summary>
[Flags]
public enum MacModifiers : ulong
{
    None = 0,
    CapsLock = 1 << 16,
    Shift = 1 << 17,
    Control = 1 << 18,
    Option = 1 << 19,
    Command = 1 << 20,
}
