using Sill.Windows.Protocol;

namespace Sill.Windows.Input;

/// <summary>A PC key as the Mac's: USB HID usages (what a device sends in kind 8's `key`, which the
/// Mac's KeyStrokes turns into its virtual keys), by physical position, so the Mac's own keyboard
/// layout decides the character, as with a keyboard plugged into it. Only the keys the Mac takes
/// (KeyStrokes.virtualKeys); a numeric keypad's keys become the main block's, and its * and + text.</summary>
public static class KeyMap
{
    public const ushort LeftControl = 0xE0, LeftShift = 0xE1, LeftOption = 0xE2, LeftCommand = 0xE3;
    public const ushort RightControl = 0xE4, RightShift = 0xE5, RightOption = 0xE6, RightCommand = 0xE7;
    /// <summary>[ and ] (the US layout's names: ⌘[ and ⌘] are Back and Forward in the Mac's apps).</summary>
    public const ushort LeftBracket = 0x2F, RightBracket = 0x30;

    // Scan code set 1 (what Windows reports), no E0 prefix.
    static readonly Dictionary<int, ushort> Plain = new()
    {
        [0x01] = 0x29, // Esc
        [0x02] = 0x1E, [0x03] = 0x1F, [0x04] = 0x20, [0x05] = 0x21, [0x06] = 0x22,
        [0x07] = 0x23, [0x08] = 0x24, [0x09] = 0x25, [0x0A] = 0x26, [0x0B] = 0x27,
        [0x0C] = 0x2D, [0x0D] = 0x2E, [0x0E] = 0x2A, [0x0F] = 0x2B,
        [0x10] = 0x14, [0x11] = 0x1A, [0x12] = 0x08, [0x13] = 0x15, [0x14] = 0x17,
        [0x15] = 0x1C, [0x16] = 0x18, [0x17] = 0x0C, [0x18] = 0x12, [0x19] = 0x13,
        [0x1A] = 0x2F, [0x1B] = 0x30, [0x1C] = 0x28, [0x1D] = LeftControl,
        [0x1E] = 0x04, [0x1F] = 0x16, [0x20] = 0x07, [0x21] = 0x09, [0x22] = 0x0A,
        [0x23] = 0x0B, [0x24] = 0x0D, [0x25] = 0x0E, [0x26] = 0x0F,
        [0x27] = 0x33, [0x28] = 0x34, [0x29] = 0x35, [0x2A] = LeftShift, [0x2B] = 0x31,
        [0x2C] = 0x1D, [0x2D] = 0x1B, [0x2E] = 0x06, [0x2F] = 0x19, [0x30] = 0x05,
        [0x31] = 0x11, [0x32] = 0x10, [0x33] = 0x36, [0x34] = 0x37, [0x35] = 0x38,
        [0x36] = RightShift, [0x38] = LeftOption, [0x39] = 0x2C, [0x3A] = 0x39,
        [0x3B] = 0x3A, [0x3C] = 0x3B, [0x3D] = 0x3C, [0x3E] = 0x3D, [0x3F] = 0x3E,
        [0x40] = 0x3F, [0x41] = 0x40, [0x42] = 0x41, [0x43] = 0x42, [0x44] = 0x43,
        [0x57] = 0x44, [0x58] = 0x45,
    };

    // With the E0 prefix (LLKHF_EXTENDED).
    static readonly Dictionary<int, ushort> Extended = new()
    {
        [0x1C] = 0x28,            // keypad Enter: Return
        [0x1D] = RightControl,
        [0x35] = 0x38,            // keypad /
        [0x38] = RightOption,     // right Alt (AltGr)
        [0x47] = 0x4A, [0x48] = 0x52, [0x49] = 0x4B, [0x4B] = 0x50, [0x4D] = 0x4F,
        [0x4F] = 0x4D, [0x50] = 0x51, [0x51] = 0x4E, [0x53] = 0x4C,
        [0x5B] = LeftCommand,     // left Windows key
        [0x5C] = RightCommand,    // right Windows key
    };

    /// <summary>The keypad's keys by virtual key (NumLock decides digits or navigation; Windows says
    /// which in the virtual key): the main block's usage, or text the Mac types.</summary>
    static (ushort usage, string? text) Keypad(int vk) => vk switch
    {
        >= 0x61 and <= 0x69 => ((ushort)(0x1E + vk - 0x61), null),   // VK_NUMPAD1…9
        0x60 => (0x27, null),                                        // VK_NUMPAD0
        0x6E => (0x37, null),                                        // VK_DECIMAL
        0x6D => (0x2D, null),                                        // VK_SUBTRACT
        0x6A => (0, "*"),                                            // VK_MULTIPLY
        0x6B => (0, "+"),                                            // VK_ADD
        0x24 => (0x4A, null), 0x26 => (0x52, null), 0x21 => (0x4B, null), 0x25 => (0x50, null),
        0x27 => (0x4F, null), 0x23 => (0x4D, null), 0x28 => (0x51, null), 0x22 => (0x4E, null),
        0x2E => (0x4C, null),
        _ => (0, null),
    };

    /// <summary>The Mac's usage for a key Windows reports, or text, or nothing (a key the Mac has no
    /// answer for: Insert, Print Screen, NumLock…). The Windows key and Ctrl swap places when Ctrl
    /// acts as ⌘.</summary>
    public static (ushort usage, string? text) Translate(int scanCode, bool extended, int vk, bool ctrlIsCommand)
    {
        ushort usage;
        string? text = null;
        bool keypad = !extended && (scanCode is >= 0x47 and <= 0x53 || scanCode == 0x37);
        if (keypad)
            (usage, text) = Keypad(vk);
        else if (!(extended ? Extended : Plain).TryGetValue(scanCode, out usage))
            usage = 0;
        if (ctrlIsCommand)
            usage = usage switch
            {
                LeftControl => LeftCommand, RightControl => RightCommand,
                LeftCommand => LeftControl, RightCommand => RightControl,
                _ => usage,
            };
        return (usage, text);
    }

    public static bool IsModifier(ushort usage) => usage is >= 0xE0 and <= 0xE7;

    public static MacModifiers Flag(ushort usage) => usage switch
    {
        LeftControl or RightControl => MacModifiers.Control,
        LeftShift or RightShift => MacModifiers.Shift,
        LeftOption or RightOption => MacModifiers.Option,
        LeftCommand or RightCommand => MacModifiers.Command,
        _ => MacModifiers.None,
    };
}
