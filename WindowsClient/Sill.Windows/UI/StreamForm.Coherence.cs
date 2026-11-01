using Sill.Windows.Coherence;
using Sill.Windows.Net;
using Sill.Windows.Protocol;
using Sill.Windows.Shell;

namespace Sill.Windows.UI;

// The stream window's part in Coherence and the Mac's menus: the toolbar's Coherence and Menus buttons,
// View › Coherence, entering and leaving (the window hides while the Mac's windows are windows of
// their own, and Sill lives in the notification area), a Mac app launched from the shell, and the
// keyboard aimed at the window in front. StreamForm.cs calls in at a few places: the constructor's
// end, Attach, the View menu, the hotkeys, the keyboard's send, the Apps menu and the test actions.
public sealed partial class StreamForm
{
    CoherenceHost? coherence;
    MacMenuService? macMenus;
    readonly ToolStripDropDownButton coherenceButton = new("Coherence") { ToolTipText = "Show the Mac’s windows each as a window of this PC", Visible = false };
    readonly ToolStripDropDownButton menusButton = new("Menus") { ToolTipText = "The Mac app’s menus", Visible = false };
    Session? coherenceSession;
    bool coherenceTried, enteredForTest;
    LaunchRequest? pendingLaunch;

    internal CoherenceHost? Coherence => coherence;

    void InitCoherence()
    {
        macMenus = new MacMenuService(this, () => session?.MacName ?? "The Mac");
        coherence = new CoherenceHost(this, keyboard, macMenus);
        int at = bar.Items.IndexOf(apps) + 1;
        bar.Items.Insert(at, menusButton);
        bar.Items.Insert(at + 1, coherenceButton);
        foreach (var b in new[] { menusButton, coherenceButton })
        {
            b.ForeColor = Theme.Text;
            b.DropDownOpened += (_, _) => MenuOpened();
            b.DropDownClosed += (_, _) => MenuClosed();
        }
        coherenceButton.DropDownOpening += (_, _) => BuildCoherenceMenu();
        menusButton.DropDownOpening += (_, _) => BuildMenusMenu();
        macMenus.TopChanged += UpdateMenusButton;
        macMenus.Refused += text => { if (coherence?.Active != true) ShowMessage(text, hint: true); TestArguments.Log($"menus: {text}"); };
        FormClosing += (_, _) =>
        {
            coherence?.Dispose();
            macMenus?.Detach();
        };
        // TEST: `--coherence` enters with every window once the first picture is up.
        Load += (_, _) => { if (TestArguments.Coherence && video != null) video.PictureShown += () => BeginInvoke(EnterForTest); };
    }

    /// <summary>A session (the first or a reconnect): its menus and Coherence's kinds.</summary>
    void AttachCoherence(Session next)
    {
        macMenus?.Attach(next);
        coherence?.Attach(next);
        next.WindowsChanged += () => BeginInvoke(() => { if (ReferenceEquals(session, next)) OnCoherenceWindows(); });
    }

    void OnCoherenceWindows()
    {
        if (session is not { } s || coherence == null) return;
        coherenceButton.Visible = s.CanCoherence;
        if (!ReferenceEquals(coherenceSession, s)) { coherenceSession = s; coherenceTried = false; }
        if (pendingLaunch is { } launch) { LaunchFromShell(launch); return; }
        // View › Coherence on: a Mac that can is shown as it was last, by the apps last shown.
        if (coherenceTried || coherence.Active || !s.CanCoherence || TestArguments.Coherence || !CoherenceStore.Current.Mode) return;
        coherenceTried = true;
        if (CoherenceStore.Current.LastApps.GetValueOrDefault(s.MacKey) is not { Count: > 0 } lastApps || s.Windows is not { } list) return;
        var windows = list.Windows.Where(w => lastApps.Contains(w.BundleID)).Select(w => w.Id).ToList();
        if (windows.Count > 0) EnterCoherence(new CoherenceChoice(windows, lastApps));
    }

    /// <summary>The keyboard hook's events (on its thread), aimed at the window in front in Coherence.</summary>
    void SendKeyboardInput(byte[] json)
    {
        var c = coherence;
        session?.SendInput(c is { Active: true } ? c.AimKey(json) : json);
    }

    bool CoherenceHotkey(Keys key) => coherence?.Hotkey(key) == true;

    /// <summary>A Coherence window came forward: files copied on this PC go to the Mac then, as when
    /// this window comes forward.</summary>
    internal void CoherenceWindowActivated() => Clipboards.ClipboardSync.Shared.WindowActivated();

    /// <summary>The stream window hides and the Mac's windows open as windows of their own.</summary>
    public void EnterCoherence(CoherenceChoice choice)
    {
        if (coherence == null || session == null || coherence.Active) return;
        if (full) ToggleFullScreen();
        LetGo();
        if (!coherence.Enter(choice)) return;
        CoherenceStore.Current.Mode = true;
        CoherenceStore.Save();
        Hide();
    }

    /// <summary>The windows close and the stream window comes back with the window that was in front.</summary>
    public void LeaveCoherence()
    {
        if (coherence is not { Active: true }) return;
        coherence.Leave(pickForStreamWindow: true);
        CoherenceStore.Current.Mode = false;
        CoherenceStore.Save();
        Show();
        Activate();
        panel.Focus();
    }

    /// <summary>`Sill.exe --launch <macKey> <bundleID>` (a pinned Mac app, a Start menu shortcut, the
    /// jump list): that app opens on the Mac (kind 7) and its windows come as windows of their own.
    /// Waits for the session's window list, and for the right Mac.</summary>
    public void LaunchFromShell(LaunchRequest r)
    {
        if (session is not { Windows: { } list } s || s.MacKey != r.MacKey) { pendingLaunch = r; return; }
        pendingLaunch = null;
        var name = s.Apps.FirstOrDefault(a => a.BundleID == r.BundleID)?.Name ?? list.Windows.FirstOrDefault(w => w.BundleID == r.BundleID)?.AppName ?? r.BundleID;
        TestArguments.Log($"launch: {r.BundleID} on {s.MacName}");
        if (coherence != null && s.CanCoherence)
        {
            if (!coherence.Active)
            {
                var windows = list.Windows.Where(w => w.BundleID == r.BundleID).Select(w => w.Id).ToList();
                EnterCoherence(new CoherenceChoice(windows, new[] { r.BundleID }));
            }
            coherence.LaunchApp(r.BundleID, name);
            return;
        }
        s.Launch(r.BundleID);
        coherence?.RememberLaunch(r.BundleID, name);
        if (!Visible) Show();
        Activate();
    }

    /// <summary>Whether this window is the one for a Mac (the shell's `--launch` asks).</summary>
    public bool IsFor(string macKey) => session?.MacKey == macKey;

    // MARK: menus

    void BuildCoherenceMenu()
    {
        coherenceButton.DropDownItems.Clear();
        var s = session;
        var list = s?.Windows;
        ToolStripMenuItem Item(string text, Action? act, bool enabled = true) =>
            new(Menus.MenuShortcut.MenuText(text), null, act == null ? null : (_, _) => act()) { Enabled = enabled, ForeColor = enabled ? Theme.Text : Theme.SecondaryText };
        if (s == null || list == null || !s.CanCoherence)
        {
            coherenceButton.DropDownItems.Add(Item("This Mac can’t show its windows separately (update Sill on the Mac)", null, enabled: false));
            return;
        }
        var active = list.Active.Kind == SourceKind.Window ? list.Windows.FirstOrDefault(w => w.Id == list.Active.WindowID) : null;
        coherenceButton.DropDownItems.Add(Item("This Window", active == null ? null : () => EnterCoherence(new CoherenceChoice(new[] { active.Id }, Array.Empty<string>(), false, active.Id)), enabled: active != null));
        if (active != null && active.BundleID.Length > 0)
            coherenceButton.DropDownItems.Add(Item($"All of {SafeText.Label(active.AppName, 40)}’s Windows", () => EnterApp(active.BundleID, active.Id)));
        coherenceButton.DropDownItems.Add(Item("Choose Windows…", () => ChooseWindows(active?.Id), enabled: list.Windows.Count > 0));
        coherenceButton.DropDownItems.Add(Item("Every Window, and New Ones", () => EnterCoherence(new CoherenceChoice(list.Windows.Select(w => w.Id).ToList(), Array.Empty<string>(), true, active?.Id)), enabled: list.Windows.Count > 0));
        var groups = list.Windows.Where(w => w.BundleID.Length > 0).GroupBy(w => w.BundleID).ToList();
        if (groups.Count > 0) coherenceButton.DropDownItems.Add(new ToolStripSeparator());
        foreach (var g in groups)
        {
            var bundle = g.Key;
            var n = g.Count();
            coherenceButton.DropDownItems.Add(Item($"{SafeText.Label(g.First().AppName, 40)} ({n} window{(n == 1 ? "" : "s")})", () => EnterApp(bundle, null)));
        }
    }

    void EnterApp(string bundleID, uint? focus)
    {
        var windows = session?.Windows?.Windows.Where(w => w.BundleID == bundleID).Select(w => w.Id).ToList() ?? new();
        EnterCoherence(new CoherenceChoice(windows, new[] { bundleID }, false, focus ?? windows.FirstOrDefault()));
    }

    void ChooseWindows(uint? current)
    {
        if (session?.Windows is not { } list) return;
        using var picker = new CoherencePicker(list.Windows, current);
        if (picker.ShowDialog(this) == DialogResult.OK && picker.Choice is { } choice) EnterCoherence(choice);
    }

    void BuildMenusMenu()
    {
        menusButton.DropDownItems.Clear();
        var items = macMenus?.TopItems(null) ?? new();
        if (items.Count == 0) items.Add(new ToolStripMenuItem("The Mac hasn’t sent its menus") { Enabled = false, ForeColor = Theme.SecondaryText });
        menusButton.DropDownItems.AddRange(items.ToArray());
    }

    void UpdateMenusButton()
    {
        var state = macMenus?.State;
        menusButton.Visible = state?.HasMenus == true;
        menusButton.ToolTipText = state?.App is { } app ? $"{app}’s menus on the Mac" : "The Mac app’s menus";
    }

    /// <summary>View › Coherence: on, the Mac's windows are windows of their own (and Sill does so again
    /// when it connects to this Mac, with the apps last shown); off, back to this window.</summary>
    void AddCoherenceViewItems()
    {
        bool can = session?.CanCoherence == true;
        var item = new ToolStripMenuItem("Coherence: Mac Windows as Windows of This PC", null, (_, _) =>
        {
            if (coherence?.Active == true) { LeaveCoherence(); return; }
            var list = session?.Windows;
            var active = list?.Active.Kind == SourceKind.Window ? list.Windows.FirstOrDefault(w => w.Id == list.Active.WindowID) : null;
            if (active != null && active.BundleID.Length > 0) EnterApp(active.BundleID, active.Id);
            else ChooseWindows(null);
        })
        {
            Checked = coherence?.Active == true || CoherenceStore.Current.Mode && can,
            Enabled = can,
            ForeColor = can ? Theme.Text : Theme.SecondaryText,
            ToolTipText = can ? null : "This Mac can’t show its windows separately (update Sill on the Mac)",
        };
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(item);
    }

    // MARK: tests

    /// <summary>TEST (--coherence): every window of the list, the one streaming in front.</summary>
    void EnterForTest()
    {
        if (enteredForTest || session?.Windows is not { } list || session.CanCoherence != true) return;
        enteredForTest = true;
        uint? focus = list.Active.Kind == SourceKind.Window ? list.Active.WindowID : list.Windows.FirstOrDefault()?.Id;
        EnterCoherence(new CoherenceChoice(list.Windows.Select(w => w.Id).ToList(), Array.Empty<string>(), false, focus));
    }

    /// <summary>TEST (--actions): Coherence's actions; true when the name was one.</summary>
    bool CoherenceAction(string name)
    {
        if (name == "coherence") { enteredForTest = false; EnterForTest(); return true; }
        if (name == "launchshell" && session != null) { LaunchFromShell(new LaunchRequest(session.MacKey, "com.apple.TextEdit")); return true; }
        return coherence?.RunAction(name) == true;
    }
}
