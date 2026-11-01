using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Sill.Windows.Data;
using Sill.Windows.DragDrop;
using Sill.Windows.Net;
using Sill.Windows.Protocol;
using IComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Sill.Windows.Clipboards;

/// <summary>The clipboard both ways, for every connected session whatever window shows it (the classic
/// stream window, the Coherence windows). It lives on a thread of its own: an STA with OLE, a hidden
/// message-only window registered with AddClipboardFormatListener, and its own message loop, from the
/// first session attached to the last one gone. So a copy reaches the Mac as it is made, not when Sill's
/// window next comes forward, and the UI thread never opens the clipboard (another app holding it, or
/// a paste rendering the Mac's files, cannot stall the picture's window).
/// <list type="bullet">
/// <item>This PC → the Mac: at each change, after <see cref="ClipboardRules.Settle"/>, unless it is
/// this PC's own write (its sequence number, or "Sill.Origin"), marked not for monitors (a password
/// manager's), or over the rate (ClipboardLoop); then the text, and for a Mac of generation 2 the HTML
/// (CF_HTML's header stripped), RTF and a PNG (the clipboard's own "PNG", else its DIB made one; never
/// CF_BITMAP alone, which Windows makes a DIB of anyway), within the caps (ClipboardRules.Outgoing).</item>
/// <item>Files copied here (CF_HDROP, or an app's virtual files) to a Mac of generation 3 go on
/// paste intent: the contract has no way for the Mac to ask for a PC's files when its user pastes, so
/// they are sent when a Sill window next comes forward (or at once, if one is in front as they are
/// copied), up to <see cref="FilesCap"/>: kind 34 parts first, then kind 32 with `files.transfers`.
/// Copying files in Explorer to paste them in Explorer sends nothing.</item>
/// <item>The Mac → this PC: what the subscription's generation takes (ClipboardRules.Incoming), text
/// as CF_UNICODETEXT, HTML as "HTML Format", RTF as "Rich Text Format", an image as its own "PNG" (or
/// "JFIF") and CF_DIBV5 with alpha, with "Sill.Origin"; a concealed item also with
/// CanIncludeInClipboardHistory and CanUploadToCloudClipboard 0. The Mac's files (generation 3) as a
/// lazy data object (MacFilesDataObject) through OleSetClipboard, fetched only when pasted.</item>
/// </list>
/// The clipboard is never flushed (OleFlushClipboard would fetch every file of the Mac's at once): when
/// the session that offered them ends, or the last session goes, the Mac's files leave the clipboard and
/// its text stays.</summary>
public sealed class ClipboardSync
{
    public static ClipboardSync Shared { get; } = new();

    /// <summary>The most a copy of files sends at a time (the rest waits for a drag, which takes 512 MB).</summary>
    public const ulong FilesCap = 128UL << 20;
    /// <summary>The most pixels of a DIB made PNG to send (beyond, it would not fit the message anyway).</summary>
    public const long MaxImagePixels = 16L << 20;

    const int WM_CLIPBOARDUPDATE = 0x031D, WM_TIMER = 0x0113, WM_CLOSE = 0x0010, WM_DESTROY = 0x0002, WM_APP = 0x8000;
    const int WorkMessage = WM_APP + 1;
    const uint CfText = 13, CfHdrop = 15, CfDib = 8, CfDibV5 = 17;
    const nint SettleTimer = 1, RateTimer = 2;

    readonly object gate = new();
    readonly List<Session> sessions = new();
    readonly Dictionary<Session, Action<ClipboardPayload>> handlers = new();
    readonly ConcurrentQueue<Action> work = new();
    readonly ClipboardLoop loop = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    Thread? thread;
    Listener? window;
    // The clipboard's thread only.
    uint pendingFiles, sentFiles;
    MacFilesDataObject? offered;
    string? offeredText;
    readonly uint cfOrigin = Native.RegisterClipboardFormatW("Sill.Origin");
    readonly uint cfExclude = Native.RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
    readonly uint cfViewerIgnore = Native.RegisterClipboardFormatW("Clipboard Viewer Ignore");
    readonly uint cfHistory = Native.RegisterClipboardFormatW("CanIncludeInClipboardHistory");
    readonly uint cfCloud = Native.RegisterClipboardFormatW("CanUploadToCloudClipboard");
    readonly uint cfHtml = Native.RegisterClipboardFormatW("HTML Format");
    readonly uint cfRtf = Native.RegisterClipboardFormatW("Rich Text Format");
    readonly uint cfPng = Native.RegisterClipboardFormatW("PNG");
    readonly uint cfJfif = Native.RegisterClipboardFormatW("JFIF");
    readonly uint cfDescriptor = Native.RegisterClipboardFormatW("FileGroupDescriptorW");

    static void Log(string line) => TestArguments.Log($"clipboard: {line}");

    /// <summary>The session's copies go both ways from now until it ends (or <see cref="Detach"/>).</summary>
    public void Attach(Session session)
    {
        lock (gate)
        {
            if (sessions.Contains(session)) return;
            sessions.Add(session);
            Action<ClipboardPayload> handler = clip => Post(() => Write(session, clip));
            handlers[session] = handler;
            session.ClipboardPayloadReceived += handler;
            session.Ended += _ => Detach(session);
            if (thread == null)
            {
                using var ready = new ManualResetEventSlim();
                thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Sill clipboard" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                ready.Wait(3000);
            }
        }
    }

    public void Detach(Session session)
    {
        lock (gate)
        {
            if (!sessions.Remove(session)) return;
            if (handlers.Remove(session, out var handler)) session.ClipboardPayloadReceived -= handler;
            Post(() => { if (offered?.Service.Session == session) Withdraw(); });
            if (sessions.Count == 0) Post(Stop);
        }
    }

    /// <summary>A Sill window came forward: the user is about to work on the Mac, so files copied here
    /// since go now (paste intent). From any thread.</summary>
    public void WindowActivated() => Post(() => { if (pendingFiles != 0 && pendingFiles != sentFiles) _ = SendFiles(pendingFiles); });

    List<Session> Sharing()
    {
        if (!Settings.Current.ShareClipboard) return new();
        lock (gate) return sessions.Where(s => s.IsOpen && ClipboardRules.Shared(s.Windows?.Clipboard) >= Capabilities.ClipboardText).ToList();
    }

    void Post(Action a)
    {
        var w = window;
        if (w == null) return;
        work.Enqueue(a);
        Native.PostMessageW(w.Handle, WorkMessage, IntPtr.Zero, IntPtr.Zero);
    }

    // MARK: the thread

    void Run(ManualResetEventSlim ready)
    {
        Ole.OleInitialize(IntPtr.Zero);
        var w = new Listener(this);
        w.CreateHandle(new CreateParams { Caption = "Sill clipboard", Parent = (IntPtr)(-3) });   // HWND_MESSAGE
        window = w;
        bool listening = Native.AddClipboardFormatListener(w.Handle);
        try { ready.Set(); } catch (ObjectDisposedException) { }
        Log(listening ? "watching the clipboard" : $"AddClipboardFormatListener failed ({Marshal.GetLastPInvokeError()})");
        while (Native.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Native.TranslateMessage(in msg);
            Native.DispatchMessageW(in msg);
        }
        lock (gate) { if (thread == Thread.CurrentThread) { thread = null; window = null; } }
    }

    void Stop()
    {
        Withdraw();
        Listener? w;
        lock (gate)
        {
            if (sessions.Count > 0) return;   // attached again meanwhile
            // Let go of the thread now, so a session attached from here on starts a new one.
            w = window;
            window = null;
            thread = null;
        }
        if (w != null)
        {
            Native.RemoveClipboardFormatListener(w.Handle);
            w.DestroyHandle();
        }
        Native.PostQuitMessage(0);
    }

    sealed class Listener : NativeWindow
    {
        readonly ClipboardSync owner;
        public Listener(ClipboardSync owner) { this.owner = owner; }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_CLIPBOARDUPDATE:
                    Native.SetTimer(Handle, SettleTimer, (uint)(ClipboardRules.Settle * 1000), IntPtr.Zero);
                    return;
                case WM_TIMER when m.WParam == SettleTimer || m.WParam == RateTimer:
                    Native.KillTimer(Handle, m.WParam);
                    owner.Look();
                    return;
                case WorkMessage:
                    while (owner.work.TryDequeue(out var a))
                    {
                        try { a(); }
                        catch (Exception e) when (e is ExternalException or IOException or InvalidOperationException or ArgumentException) { Log($"failed: {e.Message}"); }
                    }
                    return;
            }
            base.WndProc(ref m);
        }
    }

    // MARK: this PC → the Mac

    void Look()
    {
        var targets = Sharing();
        if (targets.Count == 0) return;
        uint seq = Native.GetClipboardSequenceNumber();
        bool marked = Native.IsClipboardFormatAvailable(cfOrigin);
        bool excluded = Native.IsClipboardFormatAvailable(cfExclude) || Native.IsClipboardFormatAvailable(cfViewerIgnore);
        double now = clock.Elapsed.TotalSeconds;
        var d = loop.Decide(seq, marked, excluded, now);
        switch (d.Verdict)
        {
            case ClipboardLoop.Verdict.Later:
                Native.SetTimer(window!.Handle, RateTimer, (uint)Math.Ceiling(d.Wait * 1000), IntPtr.Zero);
                return;
            case ClipboardLoop.Verdict.Excluded:
                Log("a copy marked not for clipboard monitors (a password manager's): not sent");
                return;
            case ClipboardLoop.Verdict.Own:
            case ClipboardLoop.Verdict.Marked:
                return;
        }
        bool files = Native.IsClipboardFormatAvailable(CfHdrop) || Native.IsClipboardFormatAvailable(cfDescriptor);
        if (files && targets.Any(s => ClipboardRules.Shared(s.Windows?.Clipboard) >= Capabilities.ClipboardFiles))
        {
            pendingFiles = seq;
            if (SillInFront()) _ = SendFiles(seq);
            else Log("files copied: they go to the Mac when a Sill window comes forward");
            return;
        }
        int richest = targets.Max(s => ClipboardRules.Shared(s.Windows?.Clipboard));
        var copy = Read(richest >= Capabilities.ClipboardRich);
        if (copy == null) return;
        loop.Sent(now);
        foreach (var s in targets)
        {
            var payload = ClipboardRules.Outgoing(copy, s.Windows?.Clipboard, out var left);
            if (left.Count > 0) Log($"left out of a copy to {s.MacName}: {ClipboardRules.Describe(left)}");
            if (payload == null) continue;
            if (s.Send(Kind.Clipboard, payload))
                Log($"sent a copy to {s.MacName} ({(copy.Text != null ? "text" : "")}{(copy.Html != null ? " html" : "")}{(copy.Rtf != null ? " rtf" : "")}{(copy.Png != null ? $" image {copy.Png.Length:N0} bytes" : "")}; {payload.Length:N0} bytes)");
        }
    }

    /// <summary>The clipboard as it stands: text, and with <paramref name="rich"/> HTML, RTF and a PNG.</summary>
    PcCopy? Read(bool rich)
    {
        if (!Open()) { Log("the clipboard stayed busy: the copy was not read"); return null; }
        try
        {
            string? text = Native.IsClipboardFormatAvailable(CfText) ? Utf16(Data(CfText, 8L << 20)) : null;
            if (!rich) return new PcCopy { Text = text };
            string? html = null;
            if (Data(cfHtml, 16L << 20) is { } h && CfHtml.Parse(h) is { } parsed) html = parsed.html;
            byte[]? rtf = Data(cfRtf, 16L << 20) is { } r ? TrimNul(r) : null;
            byte[]? png = Data(cfPng, 8L << 20) is { } p && ClipboardRules.IsPng(p) ? p : null;
            if (png == null)
            {
                var dib = Data(CfDibV5, 256L << 20) ?? Data(CfDib, 256L << 20);
                if (dib != null && Dib.ToBgra(dib) is { } img)
                {
                    if ((long)img.Width * img.Height > MaxImagePixels) Log($"an image of {img.Width}×{img.Height} is too large to send");
                    else png = Png.Encode(img);
                }
            }
            return new PcCopy { Text = text, Html = html, Rtf = rtf, Png = png };
        }
        finally { Native.CloseClipboard(); }
    }

    /// <summary>Files copied here, to every Mac of generation 3: parts first, then kind 32.</summary>
    async Task SendFiles(uint seq)
    {
        sentFiles = seq;
        var targets = Sharing().Where(s => ClipboardRules.Shared(s.Windows?.Clipboard) >= Capabilities.ClipboardFiles).ToList();
        if (targets.Count == 0) return;
        if (Ole.OleGetClipboard(out var data) < 0 || data == null) return;
        (List<string> paths, HashSet<string> temporary)? files;
        string? why, text = null;
        try
        {
            if (DataObjectReader.HasOrigin(data)) return;
            files = DataObjectReader.Files(data, FilesCap, out why);
        }
        finally { Marshal.ReleaseComObject(data); }
        if (files is not { } f) { Log($"files copied but not sent: {why}"); return; }
        if (Open())
        {
            try { if (Native.IsClipboardFormatAvailable(CfText)) text = Utf16(Data(CfText, 1L << 20)); }
            finally { Native.CloseClipboard(); }
        }
        foreach (var s in targets)
        {
            var service = FileService.Attach(s);
            var batch = service.Upload(f.paths, out var refusal, f.temporary);
            if (batch == null) { Log($"files not sent to {s.MacName}: {refusal}"); continue; }
            Log($"sending {batch.Files.Count} copied file(s) to {s.MacName} ({batch.TotalBytes:N0} bytes)");
            try { await batch.Completion; }
            catch (Exception e) when (e is IOException or OperationCanceledException or UnauthorizedAccessException) { Log($"copied files to {s.MacName} failed: {e.Message}"); continue; }
            var clip = new ClipboardPayload
            {
                Text = text is { Length: > 0 and <= ClipboardRules.MaxTextBytes } ? text : null,
                Files = new ClipboardFiles
                {
                    Files = batch.Files.Select(o => new OfferedFile { Name = WindowsNames.ToMac(o.Name), Size = (ulong)o.Size, Folder = o.Folder ? true : null }).ToList(),
                    Transfers = batch.Ids,
                },
            };
            if (service.Send(Kind.Clipboard, clip)) Log($"copied files are on {s.MacName}'s clipboard (transfers {string.Join(", ", batch.Ids)})");
        }
    }

    static bool SillInFront()
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(fg, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    // MARK: the Mac → this PC

    /// <summary>A kind 32 from <paramref name="session"/>'s Mac, on the clipboard's thread.</summary>
    void Write(Session session, ClipboardPayload clip)
    {
        if (!Settings.Current.ShareClipboard || !session.IsOpen) return;
        // What this PC subscribed with: the generation the session's list says (Subscription's rule).
        var mac = ClipboardRules.Incoming(clip, ClipboardRules.Shared(session.Windows?.Clipboard));
        if (mac.IsEmpty) return;
        if (mac.Files is { } files)
        {
            Withdraw();
            var obj = new MacFilesDataObject(FileService.Attach(session), files.offer, files.files, mac.Text);
            int hr = Ole.OleSetClipboard(obj);
            if (hr < 0) { Log($"OleSetClipboard failed (0x{hr:X8})"); return; }
            offered = obj;
            offeredText = mac.Text;
            loop.WroteOwn(Native.GetClipboardSequenceNumber());
            Log($"the Mac's {files.files.Count} file(s) are on the clipboard (offer {files.offer}), fetched when pasted");
            return;
        }
        WritePlain(mac);
    }

    void WritePlain(MacCopy mac)
    {
        if (!Open()) { Log("the clipboard stayed busy: the Mac's copy was not written"); return; }
        try
        {
            if (!Native.EmptyClipboard()) return;
            offered = null;
            if (mac.Text != null) Set(CfText, Encoding.Unicode.GetBytes(mac.Text + "\0"));
            if (mac.Html != null) Set(cfHtml, CfHtml.Build(mac.Html));
            if (mac.Rtf != null) Set(cfRtf, mac.Rtf.Concat(new byte[] { 0 }).ToArray());
            if (mac.Image != null)
            {
                bool png = ClipboardRules.IsPng(mac.Image);
                Set(png ? cfPng : cfJfif, mac.Image);
                if (Decode(mac.Image) is { } bgra) Set(CfDibV5, Dib.ToDibV5(bgra));
            }
            Set(cfOrigin, Encoding.UTF8.GetBytes($"Sill for Windows {Environment.ProcessId}"));
            if (mac.Concealed)
            {
                Set(cfHistory, BitConverter.GetBytes(0));
                Set(cfCloud, BitConverter.GetBytes(0));
            }
        }
        finally { Native.CloseClipboard(); }
        loop.WroteOwn(Native.GetClipboardSequenceNumber());
        Log($"wrote the Mac's copy ({(mac.Text != null ? "text" : "")}{(mac.Html != null ? " html" : "")}{(mac.Rtf != null ? " rtf" : "")}{(mac.Image != null ? " image" : "")}{(mac.Concealed ? ", concealed" : "")})");
    }

    /// <summary>The Mac's files off the clipboard (their session ended, or Sill is closing), its text
    /// kept: never a flush, which would fetch them all.</summary>
    void Withdraw()
    {
        var obj = offered;
        offered = null;
        if (obj == null || Ole.OleIsCurrentClipboard(obj) != Ole.S_OK) return;
        Ole.OleSetClipboard(null);
        if (offeredText != null) WritePlain(new MacCopy { Text = offeredText });
        Log("the Mac's files left the clipboard");
    }

    /// <summary>A PNG or JPEG as BGRA (GDI+ decodes; its 32bppArgb is straight alpha, BGRA in memory).</summary>
    static Bgra? Decode(byte[] image)
    {
        try
        {
            using var bmp = new Bitmap(new MemoryStream(image));
            if ((long)bmp.Width * bmp.Height > Dib.MaxPixels) return null;
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var px = new byte[bmp.Width * bmp.Height * 4];
                for (int y = 0; y < bmp.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, px, y * bmp.Width * 4, bmp.Width * 4);
                return new Bgra(bmp.Width, bmp.Height, px);
            }
            finally { bmp.UnlockBits(data); }
        }
        catch (Exception e) when (e is ArgumentException or ExternalException) { return null; }
    }

    // MARK: the clipboard's calls

    bool Open()
    {
        // Another app may hold it for a moment (a copy being written, a clipboard tool reading it).
        for (int i = 0; i < 10; i++)
        {
            if (Native.OpenClipboard(window!.Handle)) return true;
            Thread.Sleep(15);
        }
        return false;
    }

    static byte[]? Data(uint format, long cap)
    {
        if (!Native.IsClipboardFormatAvailable(format)) return null;
        return Ole.FromHGlobal(Native.GetClipboardData(format), cap);
    }

    static void Set(uint format, byte[] bytes)
    {
        var h = Ole.ToHGlobal(bytes);
        // The system owns the memory once SetClipboardData takes it; not when it refuses.
        if (Native.SetClipboardData(format, h) == IntPtr.Zero) Ole.GlobalFree(h);
    }

    static string? Utf16(byte[]? b)
    {
        if (b == null) return null;
        var s = Encoding.Unicode.GetString(b, 0, b.Length / 2 * 2);
        int nul = s.IndexOf('\0');
        return nul >= 0 ? s[..nul] : s;
    }

    static byte[] TrimNul(byte[] b)
    {
        int n = b.Length;
        while (n > 0 && b[n - 1] == 0) n--;
        return n == b.Length ? b : b[..n];
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; public uint lPrivate; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern uint RegisterClipboardFormatW(string name);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool OpenClipboard(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)] public static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetClipboardData(uint format, IntPtr data);
        [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] public static extern nint SetTimer(IntPtr hwnd, nint id, uint ms, IntPtr proc);
        [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr hwnd, nint id);
        [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(in MSG msg);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessageW(in MSG msg);
        [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    }
}
