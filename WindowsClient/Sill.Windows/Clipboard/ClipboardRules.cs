using System.Text;
using Sill.Windows.Protocol;

namespace Sill.Windows.Clipboards;

// The clipboard's rules on this side (kind 32, docs/coherence-protocol.md's generations, upstream's
// ClipboardPolicy.swift for generation 1): what this PC subscribes with, what of a copy goes to the Mac
// and within which caps, what of the Mac's item is written here, when a change of this PC's clipboard
// is this PC's own write coming back, and how often one goes. Pure (no WinForms, no Win32): the
// clipboard's thread (ClipboardSync.cs) only reads and writes formats, and WindowsClient/Sill.Checks
// checks this file.

/// <summary>A copy made on this PC, as the clipboard's thread read it: each field null when the
/// clipboard does not hold it.</summary>
public sealed class PcCopy
{
    public string? Text { get; init; }
    /// <summary>PNG bytes: the clipboard's own "PNG", else its DIB made PNG here.</summary>
    public byte[]? Png { get; init; }
    /// <summary>The HTML of "HTML Format" with its header stripped (CfHtml.Parse).</summary>
    public string? Html { get; init; }
    public byte[]? Rtf { get; init; }
}

public static class ClipboardRules
{
    /// <summary>The most text either way, in UTF-8 bytes (Clipboard.maxTextBytes).</summary>
    public const int MaxTextBytes = 256 * 1024;
    /// <summary>The most a device's kind 32 may be: the Mac's cap on a device's message (1 MiB).</summary>
    public const int MaxPayload = Wire.MaxClientPayload;
    /// <summary>Copies a second this PC sends at most: the host takes ClipboardPolicy.perSecond (4) and
    /// refuses the rest, so a burst is held here and its last copy goes once the second is over.</summary>
    public const int PerSecond = 4;
    /// <summary>How long after a change the clipboard is read: an app that copies often writes in steps
    /// (empty, text, then its richer formats), each a change.</summary>
    public const double Settle = 0.15;
    /// <summary>The highest generation this PC takes and sends.</summary>
    public const int Generation = Capabilities.ClipboardFiles;

    /// <summary>What this PC and the Mac share: the lower of the two, 0 when the Mac takes no clipboard.</summary>
    public static int Shared(int? hostGeneration) => Math.Clamp(hostGeneration ?? 0, 0, Generation);

    /// <summary>The subscription (the first kind 32 once the switch is on): `{}` to a Mac of text alone,
    /// else `{"accepts":N}`, so the Mac sends what both take and never a field this PC would drop.</summary>
    public static byte[] Subscription(int? hostGeneration)
    {
        int g = Shared(hostGeneration);
        return g >= Capabilities.ClipboardRich ? Json.Encode(new ClipboardPayload { Accepts = g }) : "{}"u8.ToArray();
    }

    /// <summary>What of a copy is left out, and why, for the log.</summary>
    public enum Left { TextTooLong, Html, Rtf, Image, ImageTooLarge }

    /// <summary>A copy as a kind 32 for a Mac of <paramref name="hostGeneration"/>: the text always
    /// (when there is some, at most 256 KB), then what the generation adds. The whole stays within
    /// <paramref name="cap"/>: the image goes first (it is the largest and the Mac has the text or
    /// HTML of most copies that carry one), then the RTF, then the HTML. Null when nothing is left to
    /// send; <c>left</c> names what was dropped.</summary>
    public static byte[]? Outgoing(PcCopy copy, int? hostGeneration, out List<Left> left, int cap = MaxPayload)
    {
        left = new();
        int g = Shared(hostGeneration);
        if (g < Capabilities.ClipboardText) return null;
        string? text = copy.Text is { Length: > 0 } t ? t : null;
        if (text != null && Encoding.UTF8.GetByteCount(text) > MaxTextBytes) { text = null; left.Add(Left.TextTooLong); }
        string? html = null; byte[]? rtf = null, png = null;
        if (g >= Capabilities.ClipboardRich)
        {
            html = copy.Html is { Length: > 0 } h ? h : null;
            rtf = copy.Rtf is { Length: > 0 } r ? r : null;
            png = copy.Png is { Length: > 0 } p ? p : null;
            // Base64 is 4/3 of the bytes: an image that cannot fit even alone is not tried.
            if (png != null && (png.Length + 2) / 3 * 4 + 64 > cap) { png = null; left.Add(Left.ImageTooLarge); }
        }
        while (true)
        {
            if (text == null && html == null && rtf == null && png == null) return null;
            var payload = Json.Encode(new ClipboardPayload { Text = text, Html = html, Rtf = rtf, Image = png });
            if (Wire.Fits(payload.Length, cap)) return payload;
            if (png != null) { png = null; left.Add(Left.Image); }
            else if (rtf != null) { rtf = null; left.Add(Left.Rtf); }
            else if (html != null) { html = null; left.Add(Left.Html); }
            else { text = null; left.Add(Left.TextTooLong); }   // control characters made the JSON larger than the text
        }
    }

    /// <summary>The Mac's item, as this PC writes it: only what the generation it subscribed with takes
    /// (a field of a later one is dropped, as a generation 1 reader would), the image only when it is
    /// a PNG or JPEG.</summary>
    public static MacCopy Incoming(ClipboardPayload clip, int accepted)
    {
        bool rich = accepted >= Capabilities.ClipboardRich, files = accepted >= Capabilities.ClipboardFiles;
        byte[]? image = rich && clip.Image is { Length: > 8 } img && (IsPng(img) || IsJpeg(img)) ? img : null;
        return new MacCopy
        {
            Text = clip.Text is { Length: > 0 } t ? t : null,
            Html = rich && clip.Html is { Length: > 0 } h ? h : null,
            Rtf = rich && clip.Rtf is { Length: > 0 } r ? r : null,
            Image = image,
            Files = files && clip.Files is { Offer: { } offer, Files: { Count: > 0 } list } ? (offer, list) : null,
            Concealed = clip.Concealed == true,
        };
    }

    public static bool IsPng(ReadOnlySpan<byte> b) => b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    public static bool IsJpeg(ReadOnlySpan<byte> b) => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    /// <summary>The words of a copy's dropped parts, for the log (never what was copied).</summary>
    public static string Describe(IEnumerable<Left> left) => string.Join(", ", left.Select(l => l switch
    {
        Left.TextTooLong => $"text over {MaxTextBytes / 1024} KB",
        Left.Html => "HTML (too large with the rest)",
        Left.Rtf => "RTF (too large with the rest)",
        Left.Image => "an image (too large with the rest)",
        _ => $"an image over what a message carries ({MaxPayload / 1024} KB as base64)",
    }));
}

/// <summary>The Mac's item as this PC writes it (ClipboardRules.Incoming).</summary>
public sealed class MacCopy
{
    public string? Text { get; init; }
    public string? Html { get; init; }
    public byte[]? Rtf { get; init; }
    /// <summary>PNG or JPEG bytes.</summary>
    public byte[]? Image { get; init; }
    /// <summary>Generation 3: the Mac's offer and its files, fetched with kind 37 when pasted.</summary>
    public (uint offer, List<OfferedFile> files)? Files { get; init; }
    /// <summary>Kept out of Windows' clipboard history and cloud clipboard.</summary>
    public bool Concealed { get; init; }
    public bool IsEmpty => Text == null && Html == null && Rtf == null && Image == null && Files == null;
}

/// <summary>Which change of this PC's clipboard goes to the Mac, and when: never this PC's own write of
/// the Mac's item coming back (the sequence number Windows gave it, or the "Sill.Origin" format on it,
/// which another Sill on this PC also writes), never what a password manager or a clipboard tool marks
/// as not for monitors, and at most <see cref="ClipboardRules.PerSecond"/> copies in any second, a
/// change past that held until the second is over (only the clipboard as it is then goes). Times in
/// seconds on any steady clock. Not thread-safe: the clipboard's thread alone owns one.</summary>
public sealed class ClipboardLoop
{
    uint? ownSequence;
    readonly List<double> sent = new();

    public enum Verdict
    {
        /// <summary>Read it and send it.</summary>
        Send,
        /// <summary>This PC's own write of the Mac's item (its sequence number).</summary>
        Own,
        /// <summary>Carries "Sill.Origin": a Sill wrote it.</summary>
        Marked,
        /// <summary>Marked not for clipboard monitors (ExcludeClipboardContentFromMonitorProcessing,
        /// Clipboard Viewer Ignore): a password manager's, as today's client skips.</summary>
        Excluded,
        /// <summary>Over the rate: look again after <see cref="Decision.Wait"/> seconds.</summary>
        Later,
    }

    public readonly record struct Decision(Verdict Verdict, double Wait = 0);

    /// <summary>This PC wrote the Mac's item; <paramref name="sequence"/> is the clipboard's sequence
    /// number after the write (GetClipboardSequenceNumber).</summary>
    public void WroteOwn(uint sequence) => ownSequence = sequence;

    public uint? OwnSequence => ownSequence;

    /// <summary>A change, at <paramref name="now"/>, read after the settle: whether it goes.</summary>
    public Decision Decide(uint sequence, bool marked, bool excluded, double now)
    {
        if (ownSequence == sequence) return new(Verdict.Own);
        if (marked) return new(Verdict.Marked);
        if (excluded) return new(Verdict.Excluded);
        sent.RemoveAll(t => now - t >= 1);
        if (sent.Count >= ClipboardRules.PerSecond) return new(Verdict.Later, Math.Max(0.01, sent.Min() + 1 - now));
        return new(Verdict.Send);
    }

    /// <summary>A copy went to the Mac at <paramref name="now"/> (it counts against the rate).</summary>
    public void Sent(double now) => sent.Add(now);
}
