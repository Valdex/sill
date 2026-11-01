using System.Globalization;
using System.Text;

namespace Sill.Windows.Clipboards;

/// <summary>Windows' "HTML Format" (CF_HTML): UTF-8 HTML behind a header of byte offsets, which kind
/// 32's `html` never carries (docs/coherence-protocol.md: the Windows side adds and strips it).
/// <code>
/// Version:0.9
/// StartHTML:0000000105
/// EndHTML:0000000199
/// StartFragment:0000000141
/// EndFragment:0000000163
/// &lt;html&gt;&lt;body&gt;&lt;!--StartFragment--&gt;…&lt;!--EndFragment--&gt;&lt;/body&gt;&lt;/html&gt;
/// </code>
/// Every offset counts UTF-8 bytes from the header's first byte, never characters: a page with any
/// letter beyond ASCII has more bytes than characters, and an offset in characters cuts it in the
/// wrong place (a reader then pastes half a tag). Pure; WindowsClient/Sill.Checks checks it.</summary>
public static class CfHtml
{
    const string StartMarker = "<!--StartFragment-->", EndMarker = "<!--EndFragment-->";
    // Ten digits each, so the header's length is known before the offsets are.
    const string HeaderTemplate = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
    static readonly int HeaderLength = string.Format(CultureInfo.InvariantCulture, HeaderTemplate, 0, 0, 0, 0).Length;

    /// <summary>The Mac's HTML as "HTML Format", NUL-terminated: its own fragment markers when it has
    /// them, else the inside of its body, else all of it, marked as the fragment (a document is wrapped
    /// in html and body when it has neither).</summary>
    public static byte[] Build(string html)
    {
        string doc, before, fragment, after;
        int s = html.IndexOf(StartMarker, StringComparison.OrdinalIgnoreCase);
        int e = s >= 0 ? html.IndexOf(EndMarker, s + StartMarker.Length, StringComparison.OrdinalIgnoreCase) : -1;
        if (s >= 0 && e >= 0)
        {
            before = html[..(s + StartMarker.Length)];
            fragment = html[(s + StartMarker.Length)..e];
            after = html[e..];
        }
        else if (BodyRange(html) is var (bodyStart, bodyEnd))
        {
            before = html[..bodyStart] + StartMarker;
            fragment = html[bodyStart..bodyEnd];
            after = EndMarker + html[bodyEnd..];
        }
        else
        {
            before = "<html><body>" + StartMarker;
            fragment = html;
            after = EndMarker + "</body></html>";
        }
        doc = before + fragment + after;
        int startHtml = HeaderLength;
        int startFragment = startHtml + Encoding.UTF8.GetByteCount(before);
        int endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        int endHtml = endFragment + Encoding.UTF8.GetByteCount(after);
        var header = string.Format(CultureInfo.InvariantCulture, HeaderTemplate, startHtml, endHtml, startFragment, endFragment);
        var bytes = new byte[endHtml + 1];   // the NUL after the HTML: readers that take it as a C string stop there
        Encoding.ASCII.GetBytes(header, bytes);
        Encoding.UTF8.GetBytes(doc, bytes.AsSpan(startHtml));
        return bytes;
    }

    /// <summary>"HTML Format" as an app put it on the clipboard: the HTML its offsets name (its
    /// StartHTML to EndHTML, the context a fragment needs, such as the table a row sits in) and the
    /// fragment alone; null when the header does not read. Offsets past the end (some apps count the
    /// NUL, some write -1 for an absent StartHTML) are held to what is there; an EndHTML before its
    /// start, or a fragment outside the HTML, makes the markers decide.</summary>
    public static (string html, string fragment)? Parse(ReadOnlySpan<byte> data)
    {
        // The clipboard's block often ends in NULs (its size rounded up): they are not HTML.
        int length = data.IndexOf((byte)0);
        if (length < 0) length = data.Length;
        data = data[..length];
        var fields = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        int pos = 0, headerEnd = -1;
        while (pos < data.Length)
        {
            int nl = data[pos..].IndexOfAny((byte)'\r', (byte)'\n');
            var line = nl < 0 ? data[pos..] : data.Slice(pos, nl);
            int colon = line.IndexOf((byte)':');
            if (line.IsEmpty || line[0] == (byte)'<' || colon <= 0) { headerEnd = pos; break; }
            var key = Encoding.ASCII.GetString(line[..colon]);
            var value = Encoding.ASCII.GetString(line[(colon + 1)..]).Trim();
            if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) fields[key] = n;
            else if (!key.Equals("Version", StringComparison.OrdinalIgnoreCase) && !key.Equals("SourceURL", StringComparison.OrdinalIgnoreCase)) { headerEnd = pos; break; }
            if (nl < 0) { pos = data.Length; break; }
            pos += nl + 1;
            if (pos < data.Length && data[pos - 1] == (byte)'\r' && data[pos] == (byte)'\n') pos++;
        }
        if (headerEnd < 0) headerEnd = pos;
        if (!fields.ContainsKey("StartFragment") && !fields.ContainsKey("StartHTML")) return null;
        int end = data.Length;
        int Clamp(long v) => (int)Math.Clamp(v, 0, end);
        int sh = fields.TryGetValue("StartHTML", out var a) && a >= 0 ? Clamp(a) : -1;
        int eh = fields.TryGetValue("EndHTML", out var b) && b >= 0 ? Clamp(b) : data.Length;
        // The fragment's offsets are taken only as they are: one held to the end would pass for a
        // fragment of nothing.
        long sf = fields.TryGetValue("StartFragment", out var c) ? c : -1;
        long ef = fields.TryGetValue("EndFragment", out var d) ? d : -1;
        if (sh < headerEnd) sh = headerEnd;   // absent (-1), or pointing into the header: the HTML starts after it
        if (eh < sh) eh = data.Length;
        string html = Utf8(data[sh..eh]);
        string fragment;
        if (sf >= sh && ef >= sf && ef <= eh) fragment = Utf8(data[(int)sf..(int)ef]);
        else
        {
            int ms = html.IndexOf(StartMarker, StringComparison.OrdinalIgnoreCase);
            int me = ms >= 0 ? html.IndexOf(EndMarker, ms, StringComparison.OrdinalIgnoreCase) : -1;
            fragment = ms >= 0 && me >= 0 ? html[(ms + StartMarker.Length)..me] : html;
        }
        return (html, fragment);
    }

    static string Utf8(ReadOnlySpan<byte> b) => Encoding.UTF8.GetString(b);

    /// <summary>The inside of the document's body, from after its opening tag to its closing one.</summary>
    static (int start, int end)? BodyRange(string html)
    {
        int open = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return null;
        int gt = html.IndexOf('>', open);
        int close = html.LastIndexOf("</body", StringComparison.OrdinalIgnoreCase);
        if (gt < 0 || close <= gt) return null;
        return (gt + 1, close);
    }
}
