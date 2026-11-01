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
///   both its down and its up; the Mac sees ⌘⌥⇧ come and go with no key;
/// • a Mac that repeats a held key itself gets a key's first down and its up, never Windows' repeats
///   (<see cref="MacKeys"/>).
/// Every key still down is let go when the stream loses the keyboard, so nothing stays held on the Mac.
///
/// The hook lives on a thread of its own with its own message loop, never the UI thread: Windows
/// calls a low-level hook through its thread's message queue and drops a hook that does not answer
/// within LowLevelHooksTimeout, so a UI thread busy for a moment (a menu built, a screenshot saved, a
/// layout) would stall every key on the PC, or lose the hook for good. The hook decides alone, from
/// what it can read on its own thread (the window in front, flags the UI keeps for it), and hands the
/// UI only its actions (<see cref="ToggleFullScreen"/>, <see cref="Hotkey"/>), posted.</summary>
public sealed class KeyboardForwarder : IDisposable
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104, WM_QUIT = 0x12, WM_USER = 0x400, PM_NOREMOVE = 0;
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

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; public uint lPrivate; }

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(in MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(in MSG msg);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    readonly HookProc proc;   // kept alive while hooked
    IntPtr hook;              // the hook's thread only
    Thread? thread;
    uint threadId;
    SynchronizationContext? ui;
    // The keys down on the Mac: the hook's thread and the UI's (a let-go, a shortcut), under `gate`.
    readonly object gate = new();
    readonly MacKeys keys = new();
    // The hook's thread only.
    readonly HashSet<int> physical = new();       // virtual keys down here, as this hook saw them
    readonly HashSet<int> swallowed = new();      // a hotkey's letter, whose up goes nowhere either
    bool windowsHasAlt;                           // an Alt+Tab (or Esc, F4) handed to Windows, until Alt comes up
    readonly Action<byte[]> send;
    volatile bool hasKeyboard;
    long window;

    /// <summary>Whether the stream has the keyboard (its window active, the picture focused, no menu
    /// open), as the UI thread last worked it out: set at every change of those. The hook also needs
    /// <see cref="Window"/> in front, which it reads itself, so a window that took the foreground while
    /// the UI thread was busy gets its keys at once.</summary>
    public bool HasKeyboard { get => hasKeyboard; set => hasKeyboard = value; }
    /// <summary>The stream window's handle (it changes if the form's handle is made again).</summary>
    public IntPtr Window { get => (IntPtr)Interlocked.Read(ref window); set => Interlocked.Exchange(ref window, (long)value); }
    // Read on the hook's thread at each key: each must only read a field or a setting, never a control.
    public Func<bool> IsFullScreen { get; set; } = () => false;
    public Func<bool> CtrlIsCommand { get; set; } = () => true;
    public Func<bool> WindowsKeyToMac { get; set; } = () => true;
    /// <summary>The session's Mac repeats a held key itself (WindowList.keyRepeat).</summary>
    public Func<bool> MacRepeats { get; set; } = () => false;
    /// <summary>Ctrl+Alt+Enter: the client's own (full screen), never sent. Posted to the UI thread.</summary>
    public event Action? ToggleFullScreen;
    /// <summary>Ctrl+Alt+Shift and a letter of <see cref="Hotkeys"/>, never sent. Posted to the UI thread.</summary>
    public event Action<Keys>? Hotkey;

    public KeyboardForwarder(Action<byte[]> send)
    {
        this.send = send;
        proc = Hook;
    }

    /// <summary>Starts the hook's thread. From the UI thread, whose context the actions are posted to.</summary>
    public void Install()
    {
        if (thread != null) return;
        ui = SynchronizationContext.Current;
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Sill keyboard hook", Priority = ThreadPriority.Highest };
        thread.Start();
        ready.Wait(2000);
    }

    void Run(ManualResetEventSlim ready)
    {
        threadId = GetCurrentThreadId();
        // Makes this thread's message queue now, so Dispose's WM_QUIT can reach it whenever it comes.
        PeekMessage(out _, IntPtr.Zero, WM_USER, WM_USER, PM_NOREMOVE);
        using (var p = Process.GetCurrentProcess())
            hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(p.MainModule?.ModuleName), 0);
        try { ready.Set(); } catch (ObjectDisposedException) { }
        if (hook == IntPtr.Zero) return;
        // Windows calls the hook from inside this loop's wait.
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(msg);
            DispatchMessage(msg);
        }
        UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    /// <summary>An action for the UI thread, off the hook's call.</summary>
    void Post(Action action)
    {
        if (ui == null) { action(); return; }
        try { ui.Post(_ => action(), null); }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { }   // the UI is going away
    }

    void Send(KeyEvent? e)
    {
        if (e is { } k) send(k.Payload());
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
        // The stream's window must be in front now, whatever the UI thread last worked out.
        var w = Window;
        if (!hasKeyboard || (w != IntPtr.Zero && GetForegroundWindow() != w)) return CallNextHookEx(hook, code, wParam, lParam);
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
            if (ToggleFullScreen is { } toggle) Post(toggle);
            return 1;
        }
        if (isDown && Ctrl && Alt && Shift && Hotkeys.ContainsKey((Keys)k.vkCode))
        {
            // A repeat while it is held does it once.
            var key = (Keys)k.vkCode;
            if (swallowed.Add(k.vkCode) && Hotkey is { } hotkey) Post(() => hotkey(key));
            return 1;
        }
        var (usage, text) = KeyMap.Translate(k.scanCode, (k.flags & LLKHF_EXTENDED) != 0, k.vkCode, CtrlIsCommand());
        if (text != null)
        {
            // Typed text, which the Mac never repeats (its repeat is of keys): Windows' repeats go as before.
            if (isDown) send(InputEvents.Text(text));
            return 1;
        }
        if (usage == 0) return 1;
        bool macRepeats = isDown && MacRepeats();
        lock (gate) Send(isDown ? keys.Press(usage, macRepeats) : keys.Release(usage));
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

    /// <summary>A shortcut pressed as a keyboard presses it (<see cref="MacKeys.Shortcut"/>). From the
    /// UI thread; the keys' state is shared with the hook's thread under a lock, and sent in order.</summary>
    public void SendShortcut(ushort usage, MacModifiers modifiers)
    {
        lock (gate) foreach (var e in keys.Shortcut(usage, modifiers)) send(e.Payload());
    }

    /// <summary>The stream lost the keyboard: every key it holds on the Mac comes up, others first.
    /// From either thread.</summary>
    public void ReleaseAll()
    {
        lock (gate) foreach (var e in keys.ReleaseAll()) send(e.Payload());
    }

    /// <summary>Ends the hook's loop, which unhooks it.</summary>
    public void Dispose()
    {
        var t = thread;
        if (t == null) return;
        thread = null;
        if (threadId != 0) PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        t.Join(1000);
    }
}
