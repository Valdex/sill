using System.Diagnostics;
using System.Runtime.InteropServices;
using Sill.Windows.Protocol;

namespace Sill.Windows.Input;

/// <summary>The PC's keyboard while the stream has it: a low-level keyboard hook, so every key reaches
/// the Mac and none reaches Windows' own shortcuts or the form's menu handling, each sent as the
/// Mac's HID usage with the modifiers held, as a hardware keyboard on the device sends them (an up
/// follows its down). The rules:
/// • the Windows key goes to the Mac (⌃ while Ctrl acts as ⌘, else ⌘) unless the user keeps it for Windows;
/// • in a window, Alt+Tab, Alt+Esc and Alt+F4 stay Windows' (the app switcher, closing Sill's window):
///   the Mac's ⌥ is let go and Windows gets Alt and the key, until Alt comes up; in full screen they go
///   to the Mac;
/// • Ctrl+Alt+Enter toggles full screen and goes nowhere; Ctrl+Alt+Del is always Windows';
/// • Ctrl+Alt+Shift and one of Sill's letters (<see cref="Hotkeys"/>) is Sill's own and goes nowhere,
///   both its down and its up; the Mac sees ⌘⌥⇧ come and go with no key.
/// Every key still down is let go when the stream loses the keyboard, so nothing stays held on the Mac.</summary>
public sealed class KeyboardForwarder : IDisposable
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
    const int LLKHF_EXTENDED = 0x01, LLKHF_INJECTED = 0x10;
    const int VK_TAB = 0x09, VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_F4 = 0x73, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    const int VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5, VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1;

    /// <summary>The letters Sill takes with Ctrl+Alt+Shift (virtual keys), and what each does; the
    /// stream window's View menu shows them.</summary>
    public static readonly IReadOnlyDictionary<Keys, string> Hotkeys = new Dictionary<Keys, string>
    {
        [Keys.F] = "Full Screen",
        [Keys.D] = "the Mac’s Desktop",
        [Keys.W] = "the Mac’s windows",
        [Keys.P] = "Screenshot",
        [Keys.V] = "Type the Clipboard’s Text",
        [Keys.S] = "Show Stats",
        [Keys.T] = "Always on Top",
        [Keys.M] = "Minimize",
        [Keys.Q] = "Disconnect",
    };

    public static string HotkeyText(Keys k) => $"Ctrl+Alt+Shift+{k}";
    static readonly IntPtr Ours = (IntPtr)0x5111_5111;

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public int vkCode, scanCode, flags, time; public IntPtr extra; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? name);

    readonly HookProc proc;   // kept alive while hooked
    IntPtr hook;
    readonly HashSet<ushort> down = new();       // usages down on the Mac, sent by this PC
    readonly HashSet<int> physical = new();       // virtual keys down here, as this hook saw them
    readonly HashSet<int> swallowed = new();      // a hotkey's letter, whose up goes nowhere either
    bool windowsHasAlt;                           // an Alt+Tab (or Esc, F4) handed to Windows, until Alt comes up
    readonly Action<byte[]> send;

    /// <summary>Whether the stream has the keyboard now (its window in front, the picture focused).</summary>
    public Func<bool> HasKeyboard { get; set; } = () => false;
    public Func<bool> IsFullScreen { get; set; } = () => false;
    public Func<bool> CtrlIsCommand { get; set; } = () => true;
    public Func<bool> WindowsKeyToMac { get; set; } = () => true;
    /// <summary>Ctrl+Alt+Enter: the client's own (full screen), never sent. On the UI thread (the hook's).</summary>
    public event Action? ToggleFullScreen;
    /// <summary>Ctrl+Alt+Shift and a letter of <see cref="Hotkeys"/>, never sent. On the UI thread.</summary>
    public event Action<Keys>? Hotkey;

    public KeyboardForwarder(Action<byte[]> send)
    {
        this.send = send;
        proc = Hook;
    }

    public void Install()
    {
        if (hook != IntPtr.Zero) return;
        using var p = Process.GetCurrentProcess();
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(p.MainModule?.ModuleName), 0);
    }

    MacModifiers Held()
    {
        var m = MacModifiers.None;
        foreach (var u in down) m |= KeyMap.Flag(u);
        return m;
    }

    bool Alt => physical.Contains(VK_LMENU) || physical.Contains(VK_RMENU);
    bool Ctrl => physical.Contains(VK_LCONTROL) || physical.Contains(VK_RCONTROL);
    bool Shift => physical.Contains(VK_LSHIFT) || physical.Contains(VK_RSHIFT);

    IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(hook, code, wParam, lParam);
        var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        bool isDown = (int)wParam is WM_KEYDOWN or WM_SYSKEYDOWN;
        // What this hook injected for Windows passes; anyone else's injected keys are input like any.
        if ((k.flags & LLKHF_INJECTED) != 0 && k.extra == Ours) return CallNextHookEx(hook, code, wParam, lParam);
        if (isDown) physical.Add(k.vkCode); else physical.Remove(k.vkCode);
        // A hotkey's letter coming up (wherever the keyboard is by then: its action may have opened a menu).
        if (!isDown && swallowed.Remove(k.vkCode)) return 1;

        if (windowsHasAlt)
        {
            if (!isDown && k.vkCode is VK_LMENU or VK_RMENU) windowsHasAlt = false;
            return CallNextHookEx(hook, code, wParam, lParam);
        }
        if (!HasKeyboard()) return CallNextHookEx(hook, code, wParam, lParam);
        // AltGr sends a made-up left Ctrl with it (scan code 0x21D): the right Alt alone is the Mac's right Option.
        if (k.scanCode == 0x21D) return 1;

        if (k.vkCode is VK_LWIN or VK_RWIN && !WindowsKeyToMac()) return CallNextHookEx(hook, code, wParam, lParam);
        if (isDown && Alt && k.vkCode is VK_TAB or VK_ESCAPE or VK_F4 && !IsFullScreen())
        {
            // Windows' app switcher (and Alt+F4): the Mac lets go of ⌥, Windows gets Alt and this key.
            ReleaseAll();
            windowsHasAlt = true;
            Inject(VK_LMENU, 0x38, false, false);
            Inject((ushort)k.vkCode, (ushort)k.scanCode, false, (k.flags & LLKHF_EXTENDED) != 0);
            return 1;
        }
        if (isDown && k.vkCode == VK_RETURN && Ctrl && Alt)
        {
            ToggleFullScreen?.Invoke();
            return 1;
        }
        if (isDown && Ctrl && Alt && Shift && Hotkeys.ContainsKey((Keys)k.vkCode))
        {
            // A repeat while it is held does it once.
            if (swallowed.Add(k.vkCode)) Hotkey?.Invoke((Keys)k.vkCode);
            return 1;
        }
        var (usage, text) = KeyMap.Translate(k.scanCode, (k.flags & LLKHF_EXTENDED) != 0, k.vkCode, CtrlIsCommand());
        if (text != null)
        {
            if (isDown) send(InputEvents.Text(text));
            return 1;
        }
        if (usage == 0) return 1;
        if (isDown)
        {
            down.Add(usage);
            send(InputEvents.Key(usage, true, (ulong)Held()));
        }
        else if (down.Remove(usage))
        {
            send(InputEvents.Key(usage, false, (ulong)Held()));
        }
        return 1;
    }

    static void Inject(ushort vk, ushort scan, bool up, bool extended)
    {
        var input = new INPUT
        {
            type = 1,   // INPUT_KEYBOARD
            ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = (up ? 2u : 0u) | (extended ? 1u : 0u), dwExtraInfo = Ours },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>A shortcut pressed as a keyboard presses it: the modifiers' own keys that are not down
    /// already, the key down and up, then those modifier keys up again (the Mac's KeyStrokes then
    /// leaves nothing held for the next click). From the UI thread, as the hook runs.</summary>
    public void SendShortcut(ushort usage, MacModifiers modifiers)
    {
        var added = new List<ushort>();
        foreach (var (flag, key) in new[] { (MacModifiers.Command, KeyMap.LeftCommand), (MacModifiers.Shift, KeyMap.LeftShift), (MacModifiers.Option, KeyMap.LeftOption), (MacModifiers.Control, KeyMap.LeftControl) })
        {
            if (!modifiers.HasFlag(flag) || Held().HasFlag(flag)) continue;
            down.Add(key);
            added.Add(key);
            send(InputEvents.Key(key, true, (ulong)Held()));
        }
        send(InputEvents.Key(usage, true, (ulong)Held()));
        send(InputEvents.Key(usage, false, (ulong)Held()));
        foreach (var key in Enumerable.Reverse(added))
        {
            down.Remove(key);
            send(InputEvents.Key(key, false, (ulong)Held()));
        }
    }

    /// <summary>The stream lost the keyboard: every key it holds on the Mac comes up, others first.</summary>
    public void ReleaseAll()
    {
        foreach (var u in down.Where(u => !KeyMap.IsModifier(u)).ToList()) { down.Remove(u); send(InputEvents.Key(u, false, (ulong)Held())); }
        foreach (var u in down.OrderByDescending(u => u).ToList()) { down.Remove(u); send(InputEvents.Key(u, false, (ulong)Held())); }
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
    }
}
