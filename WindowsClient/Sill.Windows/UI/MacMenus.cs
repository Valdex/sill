using System.Diagnostics;
using Sill.Windows.Menus;
using Sill.Windows.Net;
using Sill.Windows.Protocol;

namespace Sill.Windows.UI;

/// <summary>The Mac's menus in WinForms menus, for the stream window's Menus button and each Coherence
/// window's menu strip: the top level as menu items, each filled when it opens (kind 27 with a token,
/// "Loading…" meanwhile), an item chosen with kind 25. The rules are <see cref="MacMenuState"/>'s; this
/// only draws what it says and sends what it asks. Shortcuts show as this PC's keys
/// (<see cref="MenuShortcut"/>) and are never accelerators here: the keys go to the Mac, which does
/// the rest. UI thread.
///
/// A Coherence window's strip is its own app's: the Mac's top level is the frontmost app's, which an
/// activation changes. A strip keeps the last top level of its app; a menu of it opened while the Mac's
/// top level is another app's waits for that app's (the activation just sent brings it), then asks by
/// its title (rule 11), and gives up after <see cref="AppWait"/> seconds with "The menus changed…".</summary>
sealed class MacMenuService
{
    /// <summary>How long a menu waits for its app's top level after the window was activated.</summary>
    const double AppWait = 1.5;

    readonly Control ui;
    readonly Func<string> macName;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer expiry = new() { Interval = 250 };
    readonly Dictionary<int, ToolStripMenuItem> waiting = new();
    readonly Dictionary<int, (ToolStripMenuItem item, MacMenuState.Row row, string app, double since)> waitingForApp = new();
    readonly Dictionary<string, List<MacMenuState.Row>> topByApp = new();
    MacMenuState state = new();
    Session? session;
    int nextKey;

    /// <summary>A new top level (the button and the strips rebuild).</summary>
    public event Action? TopChanged;
    /// <summary>A choice that was not made, in words to show.</summary>
    public event Action<string>? Refused;

    public MacMenuState State => state;

    public MacMenuService(Control ui, Func<string> macName)
    {
        this.ui = ui;
        this.macName = macName;
        expiry.Tick += (_, _) => Expire();
    }

    double Now => clock.Elapsed.TotalSeconds;

    /// <summary>A session: what waited on the last one settles "Not connected.", and nothing of it is kept
    /// but the strips' last top levels (the same Mac's apps, most likely).</summary>
    public void Attach(Session next)
    {
        Settle(state.Reset());
        foreach (var (key, w) in waitingForApp.ToList()) Fill(w.item, MacMenuState.Content.Of(MacMenuState.NotConnected), key);
        waitingForApp.Clear();
        session = next;
        next.MacMenuReceived += m =>
        {
            if (!ui.IsHandleCreated || ui.IsDisposed) return;
            try { ui.BeginInvoke(() => { if (ReferenceEquals(session, next)) Receive(m); }); } catch (InvalidOperationException) { }
        };
        expiry.Start();
        TopChanged?.Invoke();
    }

    public void Detach()
    {
        Settle(state.Reset());
        session = null;
        expiry.Stop();
    }

    void Receive(MacMenu m)
    {
        var (done, changed, refusal) = state.Receive(m, Now);
        Settle(done);
        if (refusal != null) Refused?.Invoke(refusal.Announcement);
        if (!changed) return;
        if (state.BundleID is { } app && state.Menus.Count > 0) topByApp[app] = state.Menus;
        TestArguments.Log($"menus: version {state.Version} of {state.App} ({state.BundleID}), {state.Menus.Count} menus{(state.Stale ? ", stale" : "")}");
        TopChanged?.Invoke();
        // Menus that waited for this app's top level ask now.
        foreach (var (key, w) in waitingForApp.ToList())
        {
            if (w.app != state.BundleID) continue;
            waitingForApp.Remove(key);
            Ask(w.item, w.row, top: true, key, w.app);
        }
    }

    /// <summary>The top level's menus as items for <paramref name="app"/>'s window (null: whatever app the
    /// Mac's top level is, as the stream window shows it): the current top level when it is that app's,
    /// else the last one the Mac sent for it; none before any.</summary>
    public List<ToolStripItem> TopItems(string? app)
    {
        List<MacMenuState.Row>? rows = app == null || app == state.BundleID ? state.Menus : topByApp.GetValueOrDefault(app);
        var items = new List<ToolStripItem>();
        if (rows == null) return items;
        foreach (var row in rows) items.Add(SubmenuItem(row, app, top: true));
        if (items.Count == 0 && (app == null || app == state.BundleID) && state.Note is { } note)
            items.Add(new ToolStripMenuItem(MenuShortcut.MenuText(note)) { Enabled = false, ForeColor = Theme.SecondaryText });
        return items;
    }

    ToolStripMenuItem SubmenuItem(MacMenuState.Row row, string? app, bool top)
    {
        var item = new ToolStripMenuItem(MenuShortcut.MenuText(row.Title)) { Enabled = row.Enabled, ForeColor = row.Enabled ? Theme.Text : Theme.SecondaryText };
        if (item.DropDown is ToolStripDropDownMenu dd) dd.Renderer = new DarkRenderer();
        // A placeholder, so the item has its arrow (or opens) and DropDownOpening comes.
        item.DropDownItems.Add(Placeholder("Loading…"));
        item.DropDownOpening += (_, _) => Open(item, row, app, top);
        item.DropDownClosed += (_, _) =>
        {
            if (item.Tag is int key) { state.Abandon(key); waiting.Remove(key); waitingForApp.Remove(key); appOf.Remove(key); }
            item.Tag = null;
        };
        return item;
    }

    static ToolStripMenuItem Placeholder(string text) => new(MenuShortcut.MenuText(text)) { Enabled = false, ForeColor = Theme.SecondaryText };

    void Open(ToolStripMenuItem item, MacMenuState.Row row, string? app, bool top)
    {
        item.DropDownItems.Clear();
        item.DropDownItems.Add(Placeholder("Loading…"));
        int key = ++nextKey;
        item.Tag = key;
        if (top && app != null && state.BundleID != app && state.Version != null)
        {
            // The Mac's top level is another app's: the window's activation brings this one's.
            waitingForApp[key] = (item, row, app, Now);
            return;
        }
        Ask(item, row, top, key, app);
    }

    void Ask(ToolStripMenuItem item, MacMenuState.Row row, bool top, int key, string? app)
    {
        item.Tag = key;
        appOf[key] = app;
        if (session is not { } s) { Fill(item, MacMenuState.Content.Of(MacMenuState.NotConnected), key); return; }
        string? id = top ? (row.Id is { } built ? state.BarMenuID(built, row.Title) : null) : row.Id;
        if (id == null) { Fill(item, MacMenuState.Content.Of(state.Version == null ? MacMenuState.NotConnected : MacMenuState.ChangedNote), key); return; }
        int? version = top ? state.Version : row.Version;
        switch (state.Open(id, row.Title, version, key, s.NextMenuToken(), Now))
        {
            case MacMenuState.Fetch.Send send:
                waiting[key] = item;
                s.Send(Kind.FetchMenu, Json.Encode(send.Message));
                break;
            case MacMenuState.Fetch.Joined:
                waiting[key] = item;
                break;
            case MacMenuState.Fetch.Settled settled:
                Fill(item, settled.Content, key);
                break;
        }
    }

    readonly Dictionary<int, string?> appOf = new();

    void Settle(List<MacMenuState.Done> done)
    {
        foreach (var d in done)
            if (waiting.Remove(d.Key, out var item)) Fill(item, d.Content, d.Key);
    }

    /// <summary>A menu's rows: sections apart by separators, submenus filled when they open, items
    /// chosen with kind 25, marks as checks, shortcuts as this PC's keys.</summary>
    void Fill(ToolStripMenuItem item, MacMenuState.Content content, int key)
    {
        if (item.Tag is not int current || current != key) return;   // closed, or opened again since
        appOf.Remove(key, out var app);
        item.DropDownItems.Clear();
        if (content.Message is { } message)
        {
            item.DropDownItems.Add(Placeholder(message));
            return;
        }
        bool ctrlIsCommand = Net.Settings.Current.CtrlIsCommand;
        bool first = true;
        foreach (var section in content.Sections ?? new())
        {
            if (!first) item.DropDownItems.Add(new ToolStripSeparator());
            first = false;
            foreach (var row in section)
            {
                switch (row.Kind)
                {
                    case MacMenuState.RowKind.Note:
                        item.DropDownItems.Add(Placeholder(row.Title));
                        break;
                    case MacMenuState.RowKind.Submenu:
                        item.DropDownItems.Add(SubmenuItem(row, app, top: false));
                        break;
                    default:
                        var r = row;
                        var choice = new ToolStripMenuItem(MenuShortcut.MenuText(row.Title))
                        {
                            Enabled = row.Enabled,
                            ForeColor = row.Enabled ? Theme.Text : Theme.SecondaryText,
                            ShortcutKeyDisplayString = MenuShortcut.Translate(row.Key, ctrlIsCommand),
                            ShowShortcutKeys = true,
                            CheckState = row.Check switch { MacMenuState.Mark.On => CheckState.Checked, MacMenuState.Mark.Mixed => CheckState.Indeterminate, _ => CheckState.Unchecked },
                        };
                        choice.Click += (_, _) => Choose(r);
                        item.DropDownItems.Add(choice);
                        break;
                }
            }
        }
    }

    void Choose(MacMenuState.Row row)
    {
        if (session is not { } s) return;
        switch (state.Choose(row, s.NextMenuToken()))
        {
            case MacMenuState.Press.Send send:
                TestArguments.Log($"menus: chose {row.Id}");
                s.Send(Kind.PressMenuItem, Json.Encode(send.Message));
                break;
            case MacMenuState.Press.Refused refused:
                Refused?.Invoke(refused.Refusal.Announcement);
                break;
        }
    }

    void Expire()
    {
        double now = Now;
        double timeout = Math.Max(4, 4 * (session?.WorstRttMs ?? 0) / 1000);
        Settle(state.Expire(now, timeout, macName()));
        foreach (var (key, w) in waitingForApp.ToList())
        {
            if (now - w.since < AppWait) continue;
            waitingForApp.Remove(key);
            Fill(w.item, MacMenuState.Content.Of(MacMenuState.ChangedNote), key);
        }
    }

    /// <summary>TEST (--actions "menu"): opens the first menu of a strip, as a click would.</summary>
    public static void OpenFirst(ToolStrip strip)
    {
        if (strip.Items.OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Enabled) is { } first) first.ShowDropDown();
    }
}
