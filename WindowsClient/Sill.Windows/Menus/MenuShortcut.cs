using System.Text;

namespace Sill.Windows.Menus;

/// <summary>A Mac menu item's shortcut as the Mac draws it ("⇧⌘S", "⌃⌘←", "fn ⌃F", "F5") in this
/// PC's words, as its keys reach the Mac: ⌘ is Ctrl and ⌃ the Windows key while Ctrl acts as ⌘ (the
/// client's setting), else each is itself; ⌥ is Alt, ⇧ Shift. Display only: the menu shows it beside
/// the item and makes no accelerator of it, so the keys go to the Mac and the Mac does the rest. In
/// Windows' order (Win, Ctrl, Alt, Shift, then the key). Pure.</summary>
public static class MenuShortcut
{
    static readonly Dictionary<string, string> Keys = new()
    {
        ["←"] = "Left", ["→"] = "Right", ["↑"] = "Up", ["↓"] = "Down",
        ["⌫"] = "Backspace", ["⌦"] = "Del", ["↩"] = "Enter", ["⏎"] = "Enter", ["⌤"] = "Enter", ["⌅"] = "Enter",
        ["⎋"] = "Esc", ["⇥"] = "Tab", ["⇤"] = "Tab", ["⇞"] = "PgUp", ["⇟"] = "PgDn", ["↖"] = "Home", ["↘"] = "End",
        ["␣"] = "Space", ["⌧"] = "Clear", ["⏏"] = "Eject",
    };

    public static string? Translate(string? mac, bool ctrlIsCommand)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;
        bool command = false, control = false, option = false, shift = false, fn = false;
        var s = mac.Trim();
        if (s.StartsWith("fn", StringComparison.OrdinalIgnoreCase) && s.Length > 2 && !char.IsLetterOrDigit(s[2]))
        {
            fn = true;
            s = s[2..].TrimStart();
        }
        int i = 0;
        for (; i < s.Length; i++)
        {
            switch (s[i])
            {
                case '⌘': command = true; continue;
                case '⌃': control = true; continue;
                case '⌥': option = true; continue;
                case '⇧': shift = true; continue;
                case ' ': continue;
            }
            break;
        }
        var key = s[i..].Trim();
        // Shift's glyph after the modifiers is ⇤ (back-tab), which is ⇧⇥ on a keyboard.
        if (key == "⇤") shift = true;
        key = Keys.TryGetValue(key, out var named) ? named : key.Length == 1 ? key.ToUpperInvariant() : key;
        bool win = ctrlIsCommand ? control : command;
        bool ctrl = ctrlIsCommand ? command : control;
        var sb = new StringBuilder();
        if (fn) sb.Append("Fn+");
        if (win) sb.Append("Win+");
        if (ctrl) sb.Append("Ctrl+");
        if (option) sb.Append("Alt+");
        if (shift) sb.Append("Shift+");
        if (key.Length == 0) return sb.Length > 0 ? sb.ToString().TrimEnd('+') : null;
        sb.Append(key);
        return sb.ToString();
    }

    /// <summary>A title as a Windows menu shows it without making a mnemonic of an ampersand.</summary>
    public static string MenuText(string title) => title.Replace("&", "&&");
}
