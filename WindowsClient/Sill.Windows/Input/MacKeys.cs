using Sill.Windows.Protocol;

namespace Sill.Windows.Input;

/// <summary>A key event for the Mac: kind 8's `key`, with the modifiers held after it.</summary>
public readonly record struct KeyEvent(ushort Usage, bool Down, MacModifiers Modifiers)
{
    public byte[] Payload() => InputEvents.Key(Usage, Down, (ulong)Modifiers);
}

/// <summary>The keys this PC holds down on the Mac (usages sent down and not yet up), the modifiers
/// they hold, and what each key going down or up here sends. The rules:
/// <list type="bullet">
/// <item>A key's down goes to the Mac with the modifiers held, its up only for a key the Mac holds.</item>
/// <item>The Mac's key repeat (KeyRepeat.swift, `WindowList.keyRepeat` 1): a host that repeats a held
///   key itself, after its own Delay Until Repeat and at its own Key Repeat Rate, gets only a key's
///   first down and its up. Windows' auto-repeat (a key-down for a key already down) then sends
///   nothing: forwarded too, each would be another down for the host, which starts its delay again at
///   every down, so the Mac's timing would be lost to Windows'. A host without it gets Windows'
///   repeats as before, each a down again, as the only repeat it has.</item>
/// <item>Let go (the window lost the keyboard): every key comes up, the others first, then the
///   modifiers, each up carrying what is still held.</item>
/// <item>A shortcut is pressed as a keyboard presses it: the modifiers' own keys not held already go
///   down first and come up after (KeyStrokes on the Mac then leaves nothing held).</item>
/// </list>
/// Pure: one thread at a time (the forwarder locks around it). Checked by Sill.Checks.</summary>
public sealed class MacKeys
{
    readonly HashSet<ushort> down = new();

    /// <summary>Whether a Mac repeats a held key itself: its window list says `keyRepeat` 1 or more.</summary>
    public static bool MacRepeats(int? keyRepeat) => (keyRepeat ?? 0) >= Capabilities.KeyRepeat;

    public MacModifiers Held
    {
        get
        {
            var m = MacModifiers.None;
            foreach (var u in down) m |= KeyMap.Flag(u);
            return m;
        }
    }

    public bool IsDown(ushort usage) => down.Contains(usage);
    public int Count => down.Count;

    /// <summary>A key went down here (its first down, or Windows' auto-repeat of it): what goes to the
    /// Mac, if anything.</summary>
    public KeyEvent? Press(ushort usage, bool macRepeats)
    {
        bool first = down.Add(usage);
        if (!first && macRepeats) return null;
        return new KeyEvent(usage, true, Held);
    }

    /// <summary>A key came up here: its up, for a key the Mac holds.</summary>
    public KeyEvent? Release(ushort usage) => down.Remove(usage) ? new KeyEvent(usage, false, Held) : null;

    /// <summary>Every key held comes up: the others first, then the modifiers (highest usage first).</summary>
    public List<KeyEvent> ReleaseAll()
    {
        var order = down.Where(u => !KeyMap.IsModifier(u)).ToList();
        order.AddRange(down.Where(KeyMap.IsModifier).OrderByDescending(u => u));
        var events = new List<KeyEvent>();
        foreach (var u in order)
        {
            down.Remove(u);
            events.Add(new KeyEvent(u, false, Held));
        }
        return events;
    }

    /// <summary>A shortcut (⌘[ for a mouse's back button): the modifiers' own keys that are not held
    /// go down, the key down and up, then those modifier keys up again, last first.</summary>
    public List<KeyEvent> Shortcut(ushort usage, MacModifiers modifiers)
    {
        var events = new List<KeyEvent>();
        var added = new List<ushort>();
        foreach (var (flag, key) in new[] { (MacModifiers.Command, KeyMap.LeftCommand), (MacModifiers.Shift, KeyMap.LeftShift), (MacModifiers.Option, KeyMap.LeftOption), (MacModifiers.Control, KeyMap.LeftControl) })
        {
            if (!modifiers.HasFlag(flag) || Held.HasFlag(flag)) continue;
            down.Add(key);
            added.Add(key);
            events.Add(new KeyEvent(key, true, Held));
        }
        events.Add(new KeyEvent(usage, true, Held));
        events.Add(new KeyEvent(usage, false, Held));
        foreach (var key in Enumerable.Reverse(added))
        {
            down.Remove(key);
            events.Add(new KeyEvent(key, false, Held));
        }
        return events;
    }
}
