using Sill.Windows.Input;
using Sill.Windows.Protocol;

namespace Sill.Checks;

// The keys this PC holds on the Mac (Input/MacKeys.cs) and the Mac's key repeat (WindowList.keyRepeat:
// a host that repeats a held key gets a key's first down and its up, never Windows' auto-repeats), with
// KeyMap's translation of what the hook sees.
static class KeyChecks
{
    const ushort A = 0x04, B = 0x05, Left = 0x50;

    static string Show(IEnumerable<KeyEvent> events) => string.Join(" ", events.Select(e => $"{e.Usage:X2}{(e.Down ? "↓" : "↑")}{(ulong)e.Modifiers >> 16:X}"));

    public static void Run()
    {
        Check.That(!MacKeys.MacRepeats(null) && !MacKeys.MacRepeats(0), "a Mac whose list says no keyRepeat (or 0) repeats nothing itself");
        Check.That(MacKeys.MacRepeats(1) && MacKeys.MacRepeats(2), "keyRepeat 1 (or a later generation): the Mac repeats a held key");

        // A key held: its first down, then Windows' auto-repeat (a key-down for a key already down) every 33 ms.
        foreach (var macRepeats in new[] { false, true })
        {
            var keys = new MacKeys();
            var sent = new List<KeyEvent>();
            void Add(KeyEvent? e) { if (e is { } k) sent.Add(k); }
            Add(keys.Press(Left, macRepeats));
            for (int i = 0; i < 30; i++) Add(keys.Press(Left, macRepeats));
            Add(keys.Release(Left));
            if (macRepeats)
                Check.That(sent.Count == 2 && sent[0] == new KeyEvent(Left, true, MacModifiers.None) && sent[1] == new KeyEvent(Left, false, MacModifiers.None),
                           $"← held, a Mac that repeats: its down and its up alone ({Show(sent)})");
            else
                Check.That(sent.Count == 32 && sent.Take(31).All(e => e == new KeyEvent(Left, true, MacModifiers.None)) && !sent[31].Down,
                           $"← held, a Mac that repeats nothing: each of Windows' repeats a down again, as before ({sent.Count} events)");
        }

        var k = new MacKeys();
        Check.That(k.Press(A, true) is { Down: true } && k.IsDown(A), "a's first down goes, and the Mac holds it");
        Check.That(k.Press(A, true) == null, "its repeat, to a Mac that repeats: nothing");
        Check.That(k.Press(A, false) is { Down: true }, "its repeat once the Mac says it repeats nothing: a down again (the rule follows the list as it is)");
        Check.That(k.Release(A) is { Down: false } && !k.IsDown(A), "its up goes");
        Check.That(k.Release(A) == null, "an up for a key the Mac does not hold goes nowhere");
        Check.That(k.Press(A, true) is { Down: true }, "pressed again: a new first down");
        k.ReleaseAll();
        Check.That(k.Press(A, true) is { Down: true }, "a key let go on the Mac (the window lost the keyboard) and still held here: its next repeat is the Mac's first down");

        // Modifiers held, and their own repeats.
        var m = new MacKeys();
        var shift = m.Press(KeyMap.LeftShift, true);
        Check.Equal(new KeyEvent(KeyMap.LeftShift, true, MacModifiers.Shift), shift, "⇧ down carries shift");
        Check.That(m.Press(KeyMap.LeftShift, true) == null, "⇧'s own auto-repeat, to a Mac that repeats: nothing");
        Check.Equal(new KeyEvent(A, true, MacModifiers.Shift), m.Press(A, true), "a with ⇧ held carries shift");
        Check.That(m.Press(A, true) == null, "its repeats: nothing (the Mac's repeats carry ⇧ themselves)");
        Check.Equal(new KeyEvent(KeyMap.LeftShift, false, MacModifiers.None), m.Release(KeyMap.LeftShift), "⇧ up: nothing held but a");
        Check.Equal(new KeyEvent(A, false, MacModifiers.None), m.Release(A), "a up");

        // Ctrl as ⌘, through KeyMap as the hook translates it.
        var (ctrl, _) = KeyMap.Translate(0x1D, false, 0xA2, ctrlIsCommand: true);
        Check.Equal(KeyMap.LeftCommand, ctrl, "left Ctrl is ⌘ while Ctrl acts as ⌘");
        Check.Equal(KeyMap.LeftControl, KeyMap.Translate(0x1D, false, 0xA2, ctrlIsCommand: false).usage, "… and ⌃ otherwise");
        Check.Equal(KeyMap.LeftControl, KeyMap.Translate(0x5B, true, 0x5B, ctrlIsCommand: true).usage, "the Windows key is ⌃ while Ctrl acts as ⌘");
        Check.Equal((ushort)0x04, KeyMap.Translate(0x1E, false, 0x41, true).usage, "the key at A's place is usage 4 (the Mac's layout decides the letter)");
        Check.Equal((ushort)0x52, KeyMap.Translate(0x48, true, 0x26, true).usage, "↑ (extended) is usage 0x52");
        Check.Equal((ushort)0x1E, KeyMap.Translate(0x4F, false, 0x61, true).usage, "the keypad's 1 with NumLock is the main block's 1");
        Check.Equal((ushort)0x4D, KeyMap.Translate(0x4F, false, 0x23, true).usage, "… and End without it");
        Check.Equal("*", KeyMap.Translate(0x37, false, 0x6A, true).text, "the keypad's * is text");
        Check.Equal((ushort)0, KeyMap.Translate(0x52, true, 0x2D, true).usage, "Insert has no answer on the Mac");
        var c = new MacKeys();
        c.Press(ctrl, true);
        Check.Equal(new KeyEvent(0x06, true, MacModifiers.Command), c.Press(0x06, true), "Ctrl+C reaches the Mac as ⌘C");

        // Letting go: the others first, then the modifiers, highest first, each with what is still held.
        var r = new MacKeys();
        r.Press(KeyMap.LeftShift, false); r.Press(KeyMap.LeftCommand, false); r.Press(A, false); r.Press(B, false);
        var ups = r.ReleaseAll();
        Check.That(ups.Count == 4 && ups.All(e => !e.Down) && ups.Take(2).Select(e => e.Usage).OrderBy(u => u).SequenceEqual(new ushort[] { A, B })
                   && ups[0].Modifiers == (MacModifiers.Shift | MacModifiers.Command) && ups[2] == new KeyEvent(KeyMap.LeftCommand, false, MacModifiers.Shift)
                   && ups[3] == new KeyEvent(KeyMap.LeftShift, false, MacModifiers.None),
                   $"let go: a and b, then ⌘ (⇧ still held), then ⇧ ({Show(ups)})");
        Check.That(r.Count == 0 && r.Held == MacModifiers.None && r.ReleaseAll().Count == 0, "nothing held after, and a second let-go sends nothing");

        // A shortcut pressed as a keyboard presses it.
        var s = new MacKeys();
        var back = s.Shortcut(KeyMap.LeftBracket, MacModifiers.Command);
        Check.That(back.SequenceEqual(new[]
        {
            new KeyEvent(KeyMap.LeftCommand, true, MacModifiers.Command), new KeyEvent(KeyMap.LeftBracket, true, MacModifiers.Command),
            new KeyEvent(KeyMap.LeftBracket, false, MacModifiers.Command), new KeyEvent(KeyMap.LeftCommand, false, MacModifiers.None),
        }), $"⌘[ with nothing held: ⌘ down, [ down and up, ⌘ up ({Show(back)})");
        Check.That(s.Count == 0, "and nothing left held");
        var held = new MacKeys();
        held.Press(KeyMap.LeftCommand, true);
        var fwd = held.Shortcut(KeyMap.RightBracket, MacModifiers.Command);
        Check.That(fwd.Count == 2 && fwd.All(e => e.Usage == KeyMap.RightBracket && e.Modifiers == MacModifiers.Command) && held.IsDown(KeyMap.LeftCommand),
                   "⌘] with ⌘ already held: only ] down and up, ⌘ still held");

        Check.Equal("""{"key":{"hidUsage":80,"down":true,"modifiers":131072}}""", System.Text.Encoding.UTF8.GetString(new KeyEvent(Left, true, MacModifiers.Shift).Payload()),
                    "a key event's kind 8 payload");

        // Random typing against the rule's invariants.
        var random = new Random(162);
        int doubleDowns = 0, strayUps = 0, repeatsSent = 0, repeatsHeld = 0, lostRepeats = 0;
        ushort[] pool = { A, B, Left, KeyMap.LeftShift, KeyMap.LeftCommand, KeyMap.RightOption };
        for (int run = 0; run < 2000; run++)
        {
            bool macRepeats = run % 2 == 0;
            var keys = new MacKeys();
            var here = new HashSet<ushort>();      // keys down on this PC
            var mac = new HashSet<ushort>();       // keys down on the Mac, from the events sent
            for (int step = 0; step < 60; step++)
            {
                var u = pool[random.Next(pool.Length)];
                int roll = random.Next(10);
                KeyEvent? e;
                if (roll < 5) { bool repeat = !here.Add(u); e = keys.Press(u, macRepeats); if (repeat) { if (e != null) repeatsSent++; else repeatsHeld++; if (!macRepeats && e == null) lostRepeats++; } }
                else if (roll < 9) { here.Remove(u); e = keys.Release(u); }
                else { foreach (var up in keys.ReleaseAll()) { if (!mac.Remove(up.Usage)) strayUps++; } continue; }
                if (e is not { } ev) continue;
                if (ev.Down) { if (!mac.Add(ev.Usage) && macRepeats) doubleDowns++; }
                else if (!mac.Remove(ev.Usage)) strayUps++;
            }
        }
        Check.Equal(0, doubleDowns, "2,000 random runs: to a Mac that repeats, never a second down without an up between");
        Check.Equal(0, strayUps, "… and never an up for a key the Mac does not hold");
        Check.Equal(0, lostRepeats, $"… and to a Mac that repeats nothing, every repeat sent as before ({repeatsSent} sent, {repeatsHeld} held back for the Mac that repeats)");
    }
}
