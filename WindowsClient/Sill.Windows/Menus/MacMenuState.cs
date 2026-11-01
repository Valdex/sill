using Sill.Windows.Protocol;

namespace Sill.Windows.Menus;

/// <summary>One connection's view of the Mac's menus (kinds 24, 25, 27), the iOS client's
/// MacMenuState (iOSClient/MacMenuState.swift, checked there by Tests/checks/menu-state) in C#, rule
/// for rule. The UI thread owns one per session; each opened menu is one completion known by a key,
/// settled exactly once: by the answer to its fetch, at once when nothing may be asked, by the
/// timeout, or by the end of the connection.
/// <list type="number">
/// <item>Nothing before the first top level (a Mac from before the menus never sends one): a menu
///   opened then is "Not connected.".</item>
/// <item>A top level replaces the one before; a new version settles every waiting fetch of another
///   version with "The menus changed. Open the menu again.".</item>
/// <item>One fetch per menu: opening a menu whose fetch is waiting joins it; one kind 27 goes, with
///   the title the menu was shown under.</item>
/// <item>An answer settles only the fetch with its token; an unknown token (a late answer) is dropped.</item>
/// <item>An answer of another version than its fetch's settles it with "The menus changed…"; a menu
///   whose row came from another version than the current one is settled so at once, unasked.</item>
/// <item><see cref="Expire"/> settles a fetch older than the caller's timeout (max(4 s, 4 × the worst
///   round trip)) with "‹Mac› didn’t answer. Open the menu again.".</item>
/// <item>Stale (the app not answering): nothing is fetched; each menu settles with the note alone, and
///   a stale answer shows its rows, every one disabled, under the note.</item>
/// <item>Presses: never for a disabled row nor before a top level; while stale, refused here, unsent;
///   `pressed: false` is a refusal to show, `pressed: true` nothing (the Mac shows the result).</item>
/// <item><see cref="Reset"/> (every session's end) settles every waiting completion with "Not
///   connected." and empties everything.</item>
/// <item><see cref="Sections"/>: separators split the items, leading, trailing and doubled ones
///   dropped; an item without a title dropped; a mark "-" or "–" mixed, any other on; `more` adds a
///   last disabled row "‹n› more on the Mac"; nothing at all gives one disabled row, "No items".</item>
/// <item>A menu of the top level built from an older top level asks by its title
///   (<see cref="BarMenuID"/>): its own id while the current top level's menu there has that title,
///   else the current top level's first menu of that title, else none. On Windows a proxy's menu strip
///   is built from the top level of its own app, which may not be the Mac's current one.</item>
/// </list>
/// What the Mac sends is cleaned as any text from it is (<see cref="SafeText"/>): titles to 100
/// characters, shortcuts to 16, notes to 300; the top level keeps at most 32 menus, each with an id
/// of one part (1–4 digits, no leading zero) no earlier menu has. Pure.</summary>
public sealed class MacMenuState
{
    public enum RowKind { Item, Submenu, Note }
    public enum Mark { Off, On, Mixed }

    /// <summary>One row of a menu as this PC draws it.</summary>
    public sealed record Row(RowKind Kind, string? Id, string Title, string? Key = null, bool Enabled = true, Mark Check = Mark.Off, int? Version = null)
    {
        public static Row NoteRow(string text) => new(RowKind.Note, null, text, Enabled: false);
    }

    /// <summary>What an opened menu gets: its sections, or words of this PC's own.</summary>
    public sealed record Content(List<List<Row>>? Sections, string? Message, int More = 0)
    {
        public static Content Of(string message) => new(null, message);
        public bool SameAs(Content o) =>
            Message == o.Message && More == o.More && (Sections == null ? o.Sections == null
                : o.Sections != null && Sections.Count == o.Sections.Count && Sections.Zip(o.Sections).All(p => p.First.SequenceEqual(p.Second)));
    }

    public readonly record struct Done(int Key, Content Content);

    public sealed record Refusal(string Title, string Id, string Note, bool ByMac)
    {
        public string Announcement => $"Couldn’t choose {Title}. {Note}";
    }

    public abstract record Fetch
    {
        public sealed record Send(FetchMenu Message) : Fetch;
        public sealed record Joined : Fetch;
        public sealed record Settled(Content Content) : Fetch;
    }

    public abstract record Press
    {
        public sealed record Send(PressMenuItem Message) : Press;
        public sealed record Refused(Refusal Refusal) : Press;
    }

    public const string ChangedNote = "The menus changed. Open the menu again.";
    public const string NotConnected = "Not connected.";
    public const string NoItems = "No items";
    public static string NoAnswer(string mac) => $"{(string.IsNullOrEmpty(mac) ? "The Mac" : mac)} didn’t answer. Open the menu again.";
    public static string MoreOnTheMac(int count) => $"{count} more on the Mac";
    public const int MaxMenus = 32, TitleLimit = 100, KeyLimit = 16, NoteLimit = 300, MaxChoices = 16;

    public int? Version { get; private set; }
    public string? App { get; private set; }
    public string? BundleID { get; private set; }
    /// <summary>The top level, each a submenu row, in the Mac's order.</summary>
    public List<Row> Menus { get; private set; } = new();
    public bool Stale { get; private set; }
    public string? Note { get; private set; }
    /// <summary>Whether there is something to show: menus, or a note (no Accessibility on the Mac).</summary>
    public bool HasMenus => Menus.Count > 0 || Note != null;

    sealed record Waiting(string Id, string Title, int Version, int Token, List<int> Keys, double SentAt);
    sealed record Choice(int Token, string Id, string Title);
    readonly List<Waiting> waiting = new();
    readonly List<Choice> choices = new();

    public double? OldestWait => waiting.Count > 0 ? waiting.Min(w => w.SentAt) : null;
    public IEnumerable<int> WaitingKeys => waiting.SelectMany(w => w.Keys);

    // MARK: from the Mac

    /// <summary>A kind 24: a top level (it has `menus`), or the answer to a fetch or a choice of this
    /// connection's (by `answering`).</summary>
    public (List<Done> done, bool topChanged, Refusal? refusal) Receive(MacMenu m, double now)
    {
        var done = new List<Done>();
        if (m.Menus is { } items)
        {
            if (m.Version is not { } v) return (done, false, null);
            var before = (Version, App, BundleID, Stale, Note, Menus);
            Version = v;
            App = SafeText.Clean(m.App, SafeText.LabelLimit);
            BundleID = SafeText.Clean(m.BundleID, 255);
            Stale = m.Stale == true;
            Note = SafeText.Clean(m.Note, NoteLimit);
            if (Stale && Note == null) Note = $"{App ?? "The app"} isn’t responding.";
            Menus = TopRows(items, v);
            waiting.RemoveAll(w =>
            {
                if (w.Version == v) return false;
                done.AddRange(w.Keys.Select(k => new Done(k, Content.Of(ChangedNote))));
                return true;
            });
            bool changed = before.Version != Version || before.App != App || before.BundleID != BundleID || before.Stale != Stale
                           || before.Note != Note || !before.Menus.SequenceEqual(Menus);
            return (done, changed, null);
        }
        if (m.Answering is not { } token) return (done, false, null);
        int i = waiting.FindIndex(w => w.Token == token);
        if (i >= 0)
        {
            var w = waiting[i];
            waiting.RemoveAt(i);
            Content content;
            if (m.Version != w.Version) content = Content.Of(ChangedNote);
            else
            {
                int more = Math.Max(0, m.More ?? 0);
                content = new Content(Sections(m.Items ?? new(), m.Stale == true, m.Note, more, w.Version), null, more);
            }
            done.AddRange(w.Keys.Select(k => new Done(k, content)));
            return (done, false, null);
        }
        int c = choices.FindIndex(x => x.Token == token);
        if (c >= 0)
        {
            var choice = choices[c];
            choices.RemoveAt(c);
            if (m.Pressed != false) return (done, false, null);
            var note = SafeText.Clean(m.Note, NoteLimit) ?? ChangedNote;
            return (done, false, new Refusal(choice.Title, choice.Id, note, true));
        }
        return (done, false, null);
    }

    // MARK: from this PC

    /// <summary>A menu opened: the row's id and title in the version the row came from.</summary>
    public Fetch Open(string id, string title, int? version, int key, int token, double now)
    {
        if (Version is not { } current) return new Fetch.Settled(Content.Of(NotConnected));
        if (version != current) return new Fetch.Settled(Content.Of(ChangedNote));
        if (Stale) return new Fetch.Settled(new Content(Sections(new(), true, Note, 0, current), null));
        if (waiting.Find(w => w.Id == id && w.Title == title && w.Version == current) is { } same)
        {
            same.Keys.Add(key);
            return new Fetch.Joined();
        }
        waiting.Add(new Waiting(id, title, current, token, new List<int> { key }, now));
        return new Fetch.Send(new FetchMenu { Version = current, Id = id, Title = title, Token = token });
    }

    /// <summary>Rule 11: the id a menu of the top level built as <paramref name="builtID"/> under
    /// <paramref name="title"/> asks for now.</summary>
    public string? BarMenuID(string builtID, string title)
    {
        if (Version == null) return null;
        if (Menus.Any(r => r.Id == builtID && r.Title == title)) return builtID;
        return Menus.FirstOrDefault(r => r.Title == title)?.Id;
    }

    /// <summary>A row chosen: the kind 25 to send, a refusal made here (stale), or null (nothing to
    /// do: disabled, not an item, before a top level).</summary>
    public Press? Choose(Row row, int token)
    {
        if (Version == null || row.Kind != RowKind.Item || !row.Enabled || row.Id is not { } id || row.Version is not { } v) return null;
        if (Stale) return new Press.Refused(new Refusal(row.Title, id, Note ?? ChangedNote, false));
        choices.Add(new Choice(token, id, row.Title));
        if (choices.Count > MaxChoices) choices.RemoveRange(0, choices.Count - MaxChoices);
        return new Press.Send(new PressMenuItem { Version = v, Id = id, Title = row.Title, Token = token });
    }

    /// <summary>Rule 6: every fetch that has waited <paramref name="timeout"/> seconds or more.</summary>
    public List<Done> Expire(double now, double timeout, string mac)
    {
        var done = new List<Done>();
        waiting.RemoveAll(w =>
        {
            if (now - w.SentAt < timeout) return false;
            done.AddRange(w.Keys.Select(k => new Done(k, Content.Of(NoAnswer(mac)))));
            return true;
        });
        return done;
    }

    /// <summary>A completion given up by its menu (closed before the answer): it is no longer settled.</summary>
    public void Abandon(int key)
    {
        foreach (var w in waiting) w.Keys.Remove(key);
    }

    /// <summary>The session ended: every waiting completion settles "Not connected.", nothing kept.</summary>
    public List<Done> Reset()
    {
        var done = WaitingKeys.Select(k => new Done(k, Content.Of(NotConnected))).ToList();
        waiting.Clear();
        choices.Clear();
        Version = null; App = null; BundleID = null; Menus = new(); Stale = false; Note = null;
        return done;
    }

    // MARK: rows

    public static List<List<Row>> Sections(List<MacMenuItem> items, bool stale, string? note, int more, int? version)
    {
        var output = new List<List<Row>>();
        if (SafeText.Clean(note, NoteLimit) is { } n) output.Add(new List<Row> { Row.NoteRow(n) });
        var section = new List<Row>();
        foreach (var item in items)
        {
            if (item.Separator == true)
            {
                if (section.Count > 0) output.Add(section);
                section = new List<Row>();
                continue;
            }
            if (SafeText.Clean(item.Title, TitleLimit) is not { } title) continue;
            var kind = item.Submenu == true ? RowKind.Submenu : RowKind.Item;
            section.Add(new Row(kind, item.Id, title,
                                kind == RowKind.Item ? SafeText.Clean(item.Key, KeyLimit) : null,
                                item.Enabled != false && !stale && item.Id != null,
                                MarkOf(item.Mark), version));
        }
        if (section.Count > 0) output.Add(section);
        if (more > 0) output.Add(new List<Row> { Row.NoteRow(MoreOnTheMac(more)) });
        if (output.Count == 0) output.Add(new List<Row> { Row.NoteRow(NoItems) });
        return output;
    }

    public static Mark MarkOf(string? raw) => SafeText.Clean(raw, 1) is not { } m ? Mark.Off : m is "-" or "–" ? Mark.Mixed : Mark.On;

    public static List<Row> TopRows(List<MacMenuItem> items, int? version)
    {
        var rows = new List<Row>();
        var ids = new HashSet<string>();
        foreach (var item in items)
        {
            if (rows.Count >= MaxMenus) break;
            if (item.Id is not { } id || !IsTopID(id) || ids.Contains(id) || SafeText.Clean(item.Title, TitleLimit) is not { } title) continue;
            ids.Add(id);
            rows.Add(new Row(RowKind.Submenu, id, title, null, item.Enabled != false, Mark.Off, version));
        }
        return rows;
    }

    /// <summary>One of the bar's menus: 1–4 ASCII digits, no leading zero (the Apple menu, 0, is never sent).</summary>
    public static bool IsTopID(string id) => id.Length is >= 1 and <= 4 && id[0] != '0' && id.All(c => c is >= '0' and <= '9');
}
