using System.Globalization;
using System.Text;

namespace Sill.Windows.Protocol;

/// <summary>Sources/StreamProtocol/SafeText.swift: text from the other side (a Mac's menu titles,
/// its notes, a window's title) made one line before it reaches a menu, a title bar or a log. A peer
/// is free to send newlines that forge log lines, bidirectional overrides that make one word read as
/// another, or a megabyte of title. Pure.</summary>
public static class SafeText
{
    /// <summary>The longest label kept, in characters (grapheme clusters).</summary>
    public const int LabelLimit = 64;

    /// <summary>`text` as a one-line label, as Swift's `SafeText.label`: whitespace of every kind
    /// becomes one space, other control and format characters (the bidirectional overrides and
    /// isolates, zero-width characters), surrogates, private-use and unassigned code points are
    /// removed, runs of spaces collapse to one, the ends are trimmed, and at most
    /// <paramref name="limit"/> characters are kept, cut between characters so a letter and its
    /// marks or an emoji stay whole. Empty when nothing printable was sent.</summary>
    public static string Label(string? text, int limit = LabelLimit)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(Math.Min(text.Length, limit * 4 + 8));
        bool pendingSpace = false;
        foreach (var r in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(r))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            switch (Rune.GetUnicodeCategory(r))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.OtherNotAssigned:
                    continue;
            }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(r.ToString());
        }
        var s = sb.ToString();
        var info = new StringInfo(s);
        if (info.LengthInTextElements > limit)
            s = info.SubstringByTextElements(0, Math.Max(0, limit)).TrimEnd(' ');
        return s;
    }

    /// <summary><see cref="Label"/>, or null when nothing is left.</summary>
    public static string? Clean(string? text, int limit) => Label(text, limit) is { Length: > 0 } s ? s : null;
}
