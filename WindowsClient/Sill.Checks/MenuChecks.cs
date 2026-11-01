using System.Text;
using Sill.Windows.Coherence;
using Sill.Windows.Menus;
using Sill.Windows.Protocol;

namespace Sill.Checks;

// The Mac's menus (Menus/MacMenuState.cs, the iOS client's rules; MenuShortcut.cs), and the names the
// shell knows a Mac app by (Coherence/ShellNames.cs): AppUserModelIDs, `--launch`, the .ico, the
// recent apps.
static class MenuChecks
{
    static byte[] U(string s) => Encoding.UTF8.GetBytes(s);
    static MacMenu M(string json) => Json.Decode<MacMenu>(U(json))!;
    static string Of(MacMenuState.Content c) => c.Message ?? string.Join(" | ", c.Sections!.Select(s => string.Join(", ", s.Select(r => r.Title))));

    public static void Menus()
    {
        // The wire.
        var top = M("""{"version":3,"app":"Safari","bundleID":"com.apple.Safari","menus":[{"id":"1","title":"Safari","submenu":true},{"id":"2","title":"File","submenu":true},{"id":"3","title":"Edit","submenu":true,"enabled":false}]}""");
        Check.That(top.Version == 3 && top.Menus!.Count == 3 && top.BundleID == "com.apple.Safari", "kind 24's top level decodes");
        Check.SameJson("""{"version":3,"id":"2","title":"File","token":7}""", Json.Encode(new FetchMenu { Version = 3, Id = "2", Title = "File", Token = 7 }), "kind 27 with an id, as MacMenu.swift writes it");
        Check.SameJson("""{"token":1}""", Json.Encode(new FetchMenu { Token = 1 }), "kind 27's subscription: a token alone");
        Check.SameJson("""{"version":3,"id":"2.1","title":"New Window","token":8}""", Json.Encode(new PressMenuItem { Version = 3, Id = "2.1", Title = "New Window", Token = 8 }), "kind 25");

        var s = new MacMenuState();
        Check.That(s.Open("2", "File", 3, key: 1, token: 1, now: 0) is MacMenuState.Fetch.Settled { Content.Message: MacMenuState.NotConnected }, "rule 1: nothing before a top level: Not connected.");
        var (done, changed, _) = s.Receive(top, 0);
        Check.That(changed && done.Count == 0 && s.Version == 3 && s.BundleID == "com.apple.Safari" && s.Menus.Count == 3, "a top level: version, app and menus");
        Check.That(!s.Menus[2].Enabled, "a disabled menu of the top level stays disabled");

        var f = s.Open("2", "File", 3, key: 10, token: 5, now: 1);
        Check.That(f is MacMenuState.Fetch.Send { Message: { Id: "2", Title: "File", Version: 3, Token: 5 } }, "rule 3: opening a menu sends kind 27 with its id, title, version and token");
        Check.That(s.Open("2", "File", 3, key: 11, token: 6, now: 1.1) is MacMenuState.Fetch.Joined, "the same menu opened again joins the waiting fetch");
        (done, _, _) = s.Receive(M("""
            {"version":3,"answering":5,"menu":"2","items":[
            {"id":"2.1","title":"New Window","key":"⌘N"},{"separator":true},{"separator":true},
            {"id":"2.3","title":"Open Recent","submenu":true},{"title":""},{"id":"2.5","title":"Close","enabled":false,"key":"⌘W"},
            {"id":"2.6","title":"Show Tabs","mark":"✓"},{"id":"2.7","title":"Mixed","mark":"-"},{"separator":true}],"more":4}
            """), 1.2);
        Check.That(done.Select(d => d.Key).SequenceEqual(new[] { 10, 11 }), "rule 4: the answer settles both completions of its fetch");
        var content = done[0].Content;
        Check.Equal("New Window | Open Recent, Close, Show Tabs, Mixed | 4 more on the Mac", Of(content), "rule 10: sections, doubled and trailing separators and untitled items dropped, the more row last");
        var rows = content.Sections!.SelectMany(x => x).ToList();
        Check.That(rows[0].Key == "⌘N" && rows[0].Version == 3 && rows[0].Enabled, "an item keeps its shortcut and the version it came in");
        Check.That(rows[1].Kind == MacMenuState.RowKind.Submenu && rows[1].Key == null, "a submenu row, no shortcut");
        Check.That(!rows[2].Enabled && rows[3].Check == MacMenuState.Mark.On && rows[4].Check == MacMenuState.Mark.Mixed, "disabled, marked on, marked mixed");
        Check.That(rows[^1].Kind == MacMenuState.RowKind.Note && !rows[^1].Enabled, "the more row is a disabled note");

        (done, _, _) = s.Receive(M("""{"version":3,"answering":5,"items":[]}"""), 1.3);
        Check.That(done.Count == 0, "rule 4: a second answer to the same token is dropped");

        Check.That(s.Open("2.3", "Open Recent", 2, 12, 7, 2) is MacMenuState.Fetch.Settled { Content.Message: MacMenuState.ChangedNote }, "rule 5: a row from another version is settled unasked");
        s.Open("2.3", "Open Recent", 3, 13, 8, 2);
        (done, _, _) = s.Receive(M("""{"version":4,"answering":8,"items":[{"id":"2.3.1","title":"a.txt"}]}"""), 2.1);
        Check.That(done.Single().Content.Message == MacMenuState.ChangedNote, "rule 5: an answer of another version than its fetch");

        s.Open("2", "File", 3, 14, 9, 3);
        (done, changed, _) = s.Receive(M("""{"version":4,"app":"TextEdit","bundleID":"com.apple.TextEdit","menus":[{"id":"1","title":"TextEdit"},{"id":"2","title":"File"},{"id":"3","title":"Format"}]}"""), 3.1);
        Check.That(changed && done.Single().Content.Message == MacMenuState.ChangedNote, "rule 2: a new version settles fetches of the old one");
        Check.That(s.BarMenuID("2", "File") == "2" && s.BarMenuID("3", "Edit") == null && s.BarMenuID("9", "Format") == "3", "rule 11: a bar menu asks by its title in the current top level");

        s.Open("2", "File", 4, 15, 10, 4);
        Check.That(s.Expire(7.9, 4, "Mac").Count == 0, "rule 6: not yet");
        var expired = s.Expire(8, 4, "Studio");
        Check.That(expired.Single().Content.Message == "Studio didn’t answer. Open the menu again.", "rule 6: a fetch that waited its timeout");

        // A menu closed before its answer: its completion is given up, a joined one still settles.
        s.Open("3", "Format", 4, 30, 20, 8.5);
        s.Open("3", "Format", 4, 31, 21, 8.6);
        s.Abandon(30);
        var (late, _, _) = s.Receive(M("""{"version":4,"answering":20,"items":[{"id":"3.0","title":"Font"}]}"""), 8.7);
        Check.That(late.Select(d => d.Key).SequenceEqual(new[] { 31 }), "a menu closed before its answer is not settled; the one that joined it is");
        s.Open("3", "Format", 4, 32, 22, 8.8);
        s.Abandon(32);
        Check.That(s.Expire(20, 4, "Mac").Count == 0 && !s.WaitingKeys.Any(), "an abandoned fetch expires with nothing to settle");

        // Presses.
        var item = new MacMenuState.Row(MacMenuState.RowKind.Item, "2.1", "New Window", "⌘N", true, MacMenuState.Mark.Off, 4);
        Check.That(s.Choose(item, 11) is MacMenuState.Press.Send { Message: { Id: "2.1", Title: "New Window", Version: 4, Token: 11 } }, "rule 8: a chosen item sends kind 25");
        Check.That(s.Choose(item with { Enabled = false }, 12) == null, "a disabled row sends nothing");
        Check.That(s.Choose(item with { Kind = MacMenuState.RowKind.Submenu }, 13) == null, "nor a submenu row");
        (_, _, var refusal) = s.Receive(M("""{"version":4,"answering":11,"pressed":false,"note":"The app didn’t take it."}"""), 9);
        Check.That(refusal is { Title: "New Window", ByMac: true } && refusal.Announcement == "Couldn’t choose New Window. The app didn’t take it.", "a pressed:false answer is a refusal to show");
        s.Choose(item, 14);
        (_, _, refusal) = s.Receive(M("""{"version":4,"answering":14,"pressed":true}"""), 9.1);
        Check.That(refusal == null, "pressed:true is nothing");

        // Stale.
        s.Receive(M("""{"version":5,"app":"Xcode","stale":true,"menus":[{"id":"1","title":"Xcode"}]}"""), 10);
        Check.That(s.Stale && s.Note == "Xcode isn’t responding.", "rule 7: a stale top level, with a note of its own");
        Check.That(s.Open("1", "Xcode", 5, 16, 15, 10) is MacMenuState.Fetch.Settled { Content: var sc } && Of(sc) == "Xcode isn’t responding.", "rule 7: no fetch while stale: the note alone");
        Check.That(s.Choose(item with { Version = 5 }, 16) is MacMenuState.Press.Refused { Refusal.ByMac: false }, "rule 8: a press while stale is refused here, unsent");

        // Reset.
        s.Receive(M("""{"version":6,"menus":[{"id":"1","title":"Finder"}]}"""), 11);
        s.Open("1", "Finder", 6, 17, 17, 11);
        var reset = s.Reset();
        Check.That(reset.Single().Content.Message == MacMenuState.NotConnected && s.Version == null && !s.HasMenus, "rule 9: the end of the session settles what waits, and keeps nothing");

        // Cleaning.
        var rowsTop = MacMenuState.TopRows(Enumerable.Range(0, 40).Select(i => new MacMenuItem { Id = (i + 1).ToString(), Title = $"M{i}" })
                                           .Append(new MacMenuItem { Id = "01", Title = "Lead" }).Append(new MacMenuItem { Id = "1.2", Title = "Deep" }).ToList(), 1);
        Check.That(rowsTop.Count == MacMenuState.MaxMenus, "the top level keeps at most 32 menus");
        Check.That(!MacMenuState.IsTopID("01") && !MacMenuState.IsTopID("1.2") && !MacMenuState.IsTopID("12345") && MacMenuState.IsTopID("9999") && !MacMenuState.IsTopID("٣"), "a top id: 1–4 ASCII digits, no leading zero");
        var dup = MacMenuState.TopRows(new() { new() { Id = "2", Title = "File" }, new() { Id = "2", Title = "Again" }, new() { Id = "3", Title = "\u202Eevil\n" } }, 1);
        Check.That(dup.Count == 2 && dup[1].Title == "evil", "a repeated id is dropped; a title is cleaned to one line, overrides removed");
        Check.Equal(MacMenuState.NoItems, Of(new MacMenuState.Content(MacMenuState.Sections(new(), false, null, 0, 1), null)), "nothing at all: No items");
        Check.Equal(100, MacMenuState.Sections(new() { new() { Id = "1.1", Title = new string('x', 300) } }, false, null, 0, 1)[0][0].Title.Length, "titles to 100 characters");

        // SafeText as Swift's.
        Check.Equal("a b c", SafeText.Label("  a\t\nb\u2028 \u00A0c  "), "every whitespace one space, the ends trimmed");
        Check.Equal("ab", SafeText.Label("a\u200Bb\u202E"), "zero-width and bidirectional characters removed");
        Check.Equal("👍🏽x", SafeText.Label("👍🏽xyz", 2), "cut between characters: an emoji with its modifier stays whole");
        Check.Equal("", SafeText.Label("\u0000\u0007"), "nothing printable: empty");

        // Shortcuts in this PC's words.
        Check.Equal("Ctrl+Shift+S", MenuShortcut.Translate("⇧⌘S", ctrlIsCommand: true), "⇧⌘S with Ctrl as ⌘");
        Check.Equal("Win+Shift+S", MenuShortcut.Translate("⇧⌘S", ctrlIsCommand: false), "⇧⌘S with each key itself: the Windows key is ⌘");
        Check.Equal("Win+Ctrl+Left", MenuShortcut.Translate("⌃⌘←", true), "⌃⌘← : Windows' order, the arrow named");
        Check.Equal("Ctrl+Alt+I", MenuShortcut.Translate("⌥⌘i", true), "⌥ is Alt, a letter in capitals");
        Check.Equal("Fn+Win+F", MenuShortcut.Translate("fn ⌃F", true), "fn and ⌃");
        Check.Equal("F5", MenuShortcut.Translate("F5", true), "a function key alone");
        Check.Equal("Ctrl+Backspace", MenuShortcut.Translate("⌘⌫", true), "⌫ is Backspace");
        Check.Equal("Ctrl+Shift+Tab", MenuShortcut.Translate("⌘⇤", true), "⇤ is Shift+Tab");
        Check.Equal("Ctrl+,", MenuShortcut.Translate("⌘,", true), "punctuation as it is");
        Check.Equal(null, MenuShortcut.Translate(" ", true), "nothing: none");
        Check.Equal("Save && Close", MenuShortcut.MenuText("Save & Close"), "an ampersand never becomes a mnemonic");
    }

    public static void ShellNames()
    {
        Check.Equal("Sill.0123456789ABCDEF.com.apple.Safari", Aumid.For("0123456789ABCDEF", "com.apple.Safari"), "the AppUserModelID: Sill.<Mac ID>.<bundle ID>");
        Check.Equal("Sill.0123456789ABCDEF.com.my-app_x", Aumid.For("0123456789ABCDEF", "com.my app_x"), "no spaces: anything else becomes '-'");
        var longId = Aumid.For("0123456789ABCDEF", "com." + new string('a', 300));
        Check.That(longId.Length == Aumid.MaxLength && longId.StartsWith("Sill.0123456789ABCDEF.com.aaa", StringComparison.Ordinal), "cut to 128 characters");
        Check.Equal("Sill.Mac.app", Aumid.For("", ""), "empty parts get names of their own");
        Check.Equal("0123456789ABCDEF", Aumid.MacKey("0123456789ABCDEF", "10.0.0.2:47100"), "a Mac's key: its Mac ID");
        var plain = Aumid.MacKey(null, "10.0.0.2:47100");
        Check.That(plain.Length == 13 && plain[0] == 'A' && LaunchRequest.ValidMacKey(plain) && plain == Aumid.MacKey("", "10.0.0.2:47100"), $"a plain door's: A and its address's hash ({plain})");
        Check.That(plain != Aumid.MacKey(null, "10.0.0.3:47100"), "another address, another key");
        Check.Equal("\"C:\\Program Files\\Sill\\Sill.exe\" --launch 0123456789ABCDEF com.apple.Safari", Aumid.RelaunchCommand(@"C:\Program Files\Sill\Sill.exe", "0123456789ABCDEF", "com.apple.Safari"), "the relaunch command quotes the path");
        Check.Equal("Safari on Noah’s Mac", Aumid.DisplayName("Safari", "Noah’s Mac\n"), "the display name: the app on the Mac, cleaned");
        Check.Equal("a-b- c", Aumid.FileName("a/b: c."), "a file name without the characters Windows refuses");

        var args = new[] { "--launch", "0123456789ABCDEF", "com.apple.Safari" };
        Check.Equal(new LaunchRequest("0123456789ABCDEF", "com.apple.Safari"), LaunchRequest.FromArgs(args), "--launch read from the command line");
        Check.Equal(null, LaunchRequest.FromArgs(new[] { "--launch", "0123456789abcdef", "com.apple.Safari" }), "a Mac ID in lower case is not one");
        Check.Equal(null, LaunchRequest.FromArgs(new[] { "--launch", "0123456789ABCDEI", "com.apple.Safari" }), "nor one with Crockford's left-out I");
        Check.Equal(null, LaunchRequest.FromArgs(new[] { "--launch", "0123456789ABCDEF", "com.apple.Safari;calc" }), "a bundle ID with other characters is refused");
        Check.Equal(null, LaunchRequest.FromArgs(new[] { "--launch", "0123456789ABCDEF" }), "a missing bundle ID");
        Check.That(LaunchRequest.ValidBundleID("com.microsoft.VSCode") && LaunchRequest.ValidBundleID("org.mozilla.firefox") && !LaunchRequest.ValidBundleID(".hidden") && !LaunchRequest.ValidBundleID(new string('a', 256)),
                   "bundle IDs: reverse DNS, at most 255, not starting with a dot");
        var line = new LaunchRequest("0123456789ABCDEF", "com.apple.Safari").Encode();
        Check.That(line == "launch\t0123456789ABCDEF\tcom.apple.Safari" && LaunchRequest.Decode(line) == (false, new LaunchRequest("0123456789ABCDEF", "com.apple.Safari")), "the pipe's line, both ways");
        Check.That(LaunchRequest.Decode("show") == (true, null), "\"show\" brings the running Sill forward");
        Check.That(LaunchRequest.Decode("launch\tX\ty") == null && LaunchRequest.Decode(new string('a', 600)) == null && LaunchRequest.Decode(null) == null, "anything else on the pipe is dropped");
        Check.That(LaunchRequest.HandsOff(Array.Empty<string>()) && LaunchRequest.HandsOff(args), "opened again, or with --launch: handed to the running Sill");
        Check.That(!LaunchRequest.HandsOff(new[] { "--connect", "127.0.0.1:47100" }) && !LaunchRequest.HandsOff(new[] { "--diag" }) && !LaunchRequest.HandsOff(args.Append("--log").ToArray()),
                   "a test run or --diag runs on its own");

        // The .ico.
        var png16 = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3 };
        var png256 = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();
        var ico = IcoFile.Write(new[] { (16, png16), (256, png256) });
        Check.Bytes(new byte[] { 0, 0, 1, 0, 2, 0 }, ico.AsSpan(0, 6), "ICONDIR: reserved, type 1 (icon), two images");
        Check.Bytes(new byte[] { 16, 16, 0, 0, 1, 0, 32, 0, 7, 0, 0, 0, 38, 0, 0, 0 }, ico.AsSpan(6, 16), "the 16 px entry: its size, one plane, 32 bits, its length, its offset after the entries");
        Check.Bytes(new byte[] { 0, 0, 0, 0, 1, 0, 32, 0, 0xE8, 3, 0, 0, 45, 0, 0, 0 }, ico.AsSpan(22, 16), "the 256 px entry: 0 for 256, its data after the first's");
        var entries = IcoFile.Entries(ico);
        Check.That(entries != null && entries.Count == 2 && entries[1] == (256, 45, 1000) && ico.AsSpan(45, 1000).SequenceEqual(png256), "read back: every image where its entry says");
        Check.That(IcoFile.Entries(new byte[] { 0, 0, 2, 0, 0, 0 }) == null && IcoFile.Entries(ico.AsSpan(0, 30)) == null, "a cursor or a cut file is no icon");
        Check.That(IcoFile.Sizes.Contains(256) && IcoFile.Sizes.Contains(16) && IcoFile.Sizes.Contains(32) && IcoFile.Sizes.Contains(48), "the sizes written include 16, 32, 48 and 256");
        var n1 = IcoFile.CacheName("com.apple.Safari", png16);
        Check.That(n1.StartsWith("com.apple.Safari-", StringComparison.Ordinal) && n1.EndsWith(".ico", StringComparison.Ordinal) && n1 != IcoFile.CacheName("com.apple.Safari", png256),
                   "the cached icon's name: the bundle ID and the PNG's hash (a new icon, a new file)");
        Check.That(!IcoFile.CacheName("../../x", png16).Contains('/'), "a bundle ID never makes a path");

        // Recent apps.
        var t0 = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
        var list = new List<RecentApp>();
        for (int i = 0; i < 12; i++) list = RecentApp.Touch(list, new RecentApp { MacKey = "M", BundleID = $"app{i}", AppName = $"App {i}", LastUsed = t0.AddMinutes(i) });
        Check.That(list.Count == 10 && list[0].BundleID == "app11" && list[^1].BundleID == "app2", "the recent apps: newest first, at most ten");
        list = RecentApp.Touch(list, new RecentApp { MacKey = "M", BundleID = "app5", AppName = "App Five", LastUsed = t0.AddHours(1) });
        Check.That(list.Count == 10 && list[0].AppName == "App Five" && list.Count(a => a.BundleID == "app5") == 1, "a launch again moves it first, once, with its new name");
        list = RecentApp.Touch(list, new RecentApp { MacKey = "N", BundleID = "app5", LastUsed = t0.AddHours(2) });
        Check.That(list.Count(a => a.BundleID == "app5") == 2, "the same app on another Mac is another entry");
    }
}
