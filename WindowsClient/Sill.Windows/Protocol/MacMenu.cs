using System.Text.Json.Serialization;

namespace Sill.Windows.Protocol;

// Sources/StreamProtocol/MacMenu.swift: the streamed app's menu bar, read on the Mac over
// Accessibility one menu at a time when the device opens it (kinds 24, 25 and 27; docs/menu-bar-plan.md).
// HostSettings.swift's rules: JSON only, every field optional, strings not enums, never renamed.
// Who is sent what (MacMenu.swift's header, kept here because the client's rules follow from it):
// • a device asks once per connection, with a kind 27 without an id, after its first window list;
//   only such a connection is ever sent a top level, and the Mac sends every later one by itself;
// • the version is the host's; an id is good only within its version, and a fetch or a choice with
//   another version is answered at once, never read or pressed;
// • a fetch and a choice carry the title the device showed, and the Mac acts only while the item at
//   the id still has it;
// • tokens are the device's, strictly increasing per connection; the Mac only echoes them.
// The JSON names are written out on each property, as Coherence.cs does. Pure.

/// <summary>Kind 24, host → device: a top level (<see cref="Menus"/>), the answer to a kind 27
/// (<see cref="Answering"/>, <see cref="Menu"/>, <see cref="Items"/>), or the answer to a kind 25
/// (<see cref="Answering"/>, <see cref="Pressed"/>).</summary>
public sealed class MacMenu
{
    /// <summary>The tree this belongs to; ids are good only within one version.</summary>
    [JsonPropertyName("version")] public int? Version { get; set; }
    [JsonPropertyName("app")] public string? App { get; set; }
    [JsonPropertyName("bundleID")] public string? BundleID { get; set; }
    /// <summary>The top level in the Mac's order, the Apple menu left out: each a submenu.</summary>
    [JsonPropertyName("menus")] public List<MacMenuItem>? Menus { get; set; }
    [JsonPropertyName("answering")] public int? Answering { get; set; }
    [JsonPropertyName("menu")] public string? Menu { get; set; }
    [JsonPropertyName("items")] public List<MacMenuItem>? Items { get; set; }
    /// <summary>How many items the Mac left out of a long menu.</summary>
    [JsonPropertyName("more")] public int? More { get; set; }
    [JsonPropertyName("pressed")] public bool? Pressed { get; set; }
    /// <summary>The app did not answer the last read: every item shows disabled, presses are refused.</summary>
    [JsonPropertyName("stale")] public bool? Stale { get; set; }
    /// <summary>Words to show as they are (why there are no menus, why a press was refused).</summary>
    [JsonPropertyName("note")] public string? Note { get; set; }
}

/// <summary>One menu or item, as the Mac draws it.</summary>
public sealed class MacMenuItem
{
    /// <summary>"3.4": the item's indexes from the menu bar (the Apple menu is 0).</summary>
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("separator")] public bool? Separator { get; set; }
    /// <summary>Null counts as true.</summary>
    [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
    /// <summary>"✓" on, "-" mixed, another single character an app set; null unmarked.</summary>
    [JsonPropertyName("mark")] public string? Mark { get; set; }
    /// <summary>The shortcut as the Mac draws it ("⇧⌘S"): display only, never a key command here.</summary>
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("submenu")] public bool? Submenu { get; set; }
}

/// <summary>Kind 27, device → host. No id: the top level and every later one (the subscription).
/// With an id: that menu's items.</summary>
public sealed class FetchMenu
{
    [JsonPropertyName("version")] public int? Version { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("token")] public int? Token { get; set; }
}

/// <summary>Kind 25, device → host: choose one item, while its title is still the one shown.</summary>
public sealed class PressMenuItem
{
    [JsonPropertyName("version")] public int? Version { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("token")] public int? Token { get; set; }
}
