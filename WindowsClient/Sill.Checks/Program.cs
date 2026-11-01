using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sill.Checks;

// WindowsClient/Sill.Checks: the Windows client's pure logic, checked on any system the .NET SDK
// runs on (Linux included), as upstream's Tests/checks checks the Swift side. Each group prints a
// line per check ("ok" or "FAIL"), then the count; the exit code is 1 when anything failed.
//
//   dotnet run --project WindowsClient/Sill.Checks            every group
//   dotnet run --project WindowsClient/Sill.Checks -- audio   only the groups whose names contain it
static class Program
{
    static readonly (string name, Action run)[] Groups =
    {
        ("header", ProtocolChecks.Header),
        ("json", ProtocolChecks.JsonPayloads),
        ("binary", ProtocolChecks.Binary),
        ("audio-packets", ProtocolChecks.AudioPackets),
        ("pairing-hevc", ProtocolChecks.PairingAndHevc),
        ("keyframe-recovery", KeyframeChecks.Run),
        ("keys", KeyChecks.Run),
        ("audio-receiver", AudioChecks.Receiver),
        ("jitter-buffer", AudioChecks.Buffer),
        ("window-state", CoherenceChecks.Kind45),
        ("side-streams", CoherenceChecks.SideStreams),
        ("geometry", CoherenceChecks.Geometry),
        ("coherence-controller", CoherenceChecks.Controller),
    };

    static int Main(string[] args)
    {
        foreach (var (name, run) in Groups)
        {
            if (args.Length > 0 && !args.Any(a => name.Contains(a, StringComparison.OrdinalIgnoreCase))) continue;
            Console.WriteLine($"== {name}");
            run();
        }
        return Check.Finish();
    }
}

/// <summary>Plain assertions with a count.</summary>
static class Check
{
    static int checks, failures;

    public static void That(bool ok, string what)
    {
        checks++;
        if (ok) Console.WriteLine($"ok   {what}");
        else { failures++; Console.WriteLine($"FAIL {what}"); }
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
        That(ok, ok ? what : $"{what}: expected {expected}, got {actual}");
    }

    public static void Bytes(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, string what)
    {
        bool ok = expected.SequenceEqual(actual);
        That(ok, ok ? what : $"{what}: expected {Convert.ToHexString(expected)}, got {Convert.ToHexString(actual)}");
    }

    /// <summary>The same JSON value: the same keys (none more, none fewer), numbers by value, any order of keys.</summary>
    public static void SameJson(string expected, ReadOnlySpan<byte> actual, string what)
    {
        string got = Encoding.UTF8.GetString(actual);
        bool ok;
        try { ok = Same(JsonNode.Parse(expected), JsonNode.Parse(got)); }
        catch (JsonException) { ok = false; }
        That(ok, ok ? what : $"{what}: expected {expected}, got {got}");
    }

    static bool Same(JsonNode? a, JsonNode? b)
    {
        if (a == null || b == null) return a == null && b == null;
        switch (a)
        {
            case JsonObject oa:
                if (b is not JsonObject ob || oa.Count != ob.Count) return false;
                foreach (var (key, value) in oa)
                    if (!ob.TryGetPropertyValue(key, out var other) || !Same(value, other)) return false;
                return true;
            case JsonArray aa:
                if (b is not JsonArray ab || aa.Count != ab.Count) return false;
                for (int i = 0; i < aa.Count; i++) if (!Same(aa[i], ab[i])) return false;
                return true;
            default:
                var ea = a.GetValue<JsonElement>();
                var eb = b.GetValue<JsonElement>();
                if (ea.ValueKind != eb.ValueKind) return false;
                return ea.ValueKind switch
                {
                    JsonValueKind.Number => ea.GetDouble() == eb.GetDouble(),
                    JsonValueKind.String => ea.GetString() == eb.GetString(),
                    _ => true,   // true, false, null: the kind says it
                };
        }
    }

    public static int Finish()
    {
        Console.WriteLine($"{checks} checks, {failures} failed");
        return failures == 0 ? 0 : 1;
    }
}
