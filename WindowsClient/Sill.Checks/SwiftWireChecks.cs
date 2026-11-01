using System.Text.Json;
using System.Text.Json.Nodes;
using Sill.Windows.Protocol;

namespace Sill.Checks;

/// <summary>The contract between the two sides, checked from the Mac's end: every Coherence message as
/// Sill for Mac's own Swift types encode it (Fixtures/swift-wire.jsonl, written by
/// Tests/checks/coherence-fixtures; run.sh there writes it again), read by this client's types and
/// written back, must be the same JSON (keys in any order, nothing lost, nothing added). A field the
/// Swift side has and C# does not, or a name spelled differently, fails here.</summary>
static class SwiftWireChecks
{
    public static void Run()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "swift-wire.jsonl");
        Check.That(File.Exists(path), "the Swift side's fixtures are there");
        if (!File.Exists(path)) return;
        var asm = typeof(Json).Assembly;
        int lines = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            lines++;
            var node = JsonNode.Parse(line)!;
            var typeName = (string)node["type"]!;
            var swift = node["json"]!.ToJsonString();
            var type = asm.GetType("Sill.Windows.Protocol." + typeName);
            Check.That(type != null, $"{typeName}: a C# type reads it");
            if (type == null) continue;
            var value = JsonSerializer.Deserialize(swift, type, Json.Options);
            Check.That(value != null, $"{typeName}: decodes");
            if (value == null) continue;
            var back = JsonSerializer.Serialize(value, type, Json.Options);
            var same = Canonical(JsonNode.Parse(swift)) == Canonical(JsonNode.Parse(back));
            Check.That(same, same ? $"{typeName}: written back as the Mac wrote it ({swift.Length} bytes)"
                                  : $"{typeName}: differs\n     swift {Canonical(JsonNode.Parse(swift))}\n     c#    {Canonical(JsonNode.Parse(back))}");
        }
        Check.That(lines >= 17, $"every fixture line read ({lines})");
    }

    /// <summary>The JSON with every object's keys sorted and numbers as doubles, so key order and 1280
    /// against 1280.0 never count as a difference.</summary>
    static string Canonical(JsonNode? n) => n switch
    {
        null => "null",
        JsonObject o => "{" + string.Join(",", o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => JsonSerializer.Serialize(p.Key) + ":" + Canonical(p.Value))) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(Canonical)) + "]",
        JsonValue v when v.TryGetValue<double>(out var d) => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        JsonValue v => v.ToJsonString(),
        _ => n.ToJsonString(),
    };
}
