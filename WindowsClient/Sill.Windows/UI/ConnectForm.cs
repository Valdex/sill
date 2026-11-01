using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net;
using Sill.Windows.Net;
using Sill.Windows.Protocol;

namespace Sill.Windows.UI;

/// <summary>One row of the connect screen: a Mac Bonjour lists, a Mac this PC paired with, or both.</summary>
public sealed class MacRow
{
    public required string Name { get; init; }
    public MacService? Service { get; init; }
    public SavedMac? Saved { get; init; }
    public Door Door => Service?.Door ?? Door.PairingRequired;
    public string BonjourName => Service?.Name ?? Saved?.BonjourName ?? Name;
    public string Key => Saved?.MacID ?? Service?.FullName ?? Name;

    public string Detail
    {
        get
        {
            var where = Service?.Addresses.FirstOrDefault()?.ToString() ?? Saved?.LastAddress;
            string state = Service == null ? "Not on this network now"
                : Door switch
                {
                    Door.Plain => "Development build · not encrypted",
                    Door.Open => Saved != null ? "Paired" : "Any device may connect",
                    _ => Saved != null ? "Paired" : "Not paired · you’ll type a code from the Mac",
                };
            return where == null ? state : $"{state} · {where}";
        }
    }

    public IReadOnlyList<IPEndPoint> Endpoints =>
        Service != null ? Transport.Endpoints(Service)
        : Saved?.LastAddress != null && IPAddress.TryParse(Saved.LastAddress, out var a) && Saved.LastPort > 0 ? new[] { new IPEndPoint(a, Saved.LastPort) } : Array.Empty<IPEndPoint>();
}

/// <summary>The Macs, drawn as cards.</summary>
sealed class MacList : Control
{
    List<MacRow> rows = new();
    int hover = -1, selected = -1, scroll;
    const int RowHeight = 66, Gap = 8;

    public event Action<MacRow>? Activated;
    public event Action<MacRow, Point>? ContextRequested;

    public MacList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Background;
        TabStop = true;
    }

    int Scaled(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    public MacRow? Selected => selected >= 0 && selected < rows.Count ? rows[selected] : null;

    public void SetRows(List<MacRow> newRows)
    {
        var key = Selected?.Key;
        rows = newRows;
        selected = key == null ? (rows.Count > 0 ? 0 : -1) : rows.FindIndex(r => r.Key == key);
        if (selected < 0 && rows.Count > 0) selected = 0;
        Invalidate();
    }

    public int Count => rows.Count;

    Rectangle RowRect(int i) => new(0, i * Scaled(RowHeight + Gap) - scroll, Width - 1, Scaled(RowHeight));

    int RowAt(Point p)
    {
        for (int i = 0; i < rows.Count; i++) if (RowRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = RowRect(i);
            if (r.Bottom < 0 || r.Top > Height) continue;
            var row = rows[i];
            var fill = i == selected ? Theme.SurfaceSelected : i == hover ? Theme.SurfaceHover : Theme.Surface;
            using (var path = Theme.Rounded(r, Scaled(10)))
            using (var b = new SolidBrush(fill))
                g.FillPath(b, path);
            // A Mac glyph: a rounded screen on a foot.
            int gx = r.X + Scaled(16), gy = r.Y + Scaled(17);
            using (var pen = new Pen(row.Service != null ? Theme.Text : Theme.SecondaryText, Scaled(2)))
            {
                using var screen = Theme.Rounded(new RectangleF(gx, gy, Scaled(30), Scaled(21)), Scaled(3));
                g.DrawPath(pen, screen);
                g.DrawLine(pen, gx + Scaled(10), gy + Scaled(29), gx + Scaled(20), gy + Scaled(29));
                g.DrawLine(pen, gx + Scaled(15), gy + Scaled(22), gx + Scaled(15), gy + Scaled(28));
            }
            int tx = r.X + Scaled(62), tw = r.Width - Scaled(62) - Scaled(16);
            TextRenderer.DrawText(g, row.Name, Theme.BodyBold, new Rectangle(tx, r.Y + Scaled(12), tw, Scaled(24)),
                                  row.Service != null ? Theme.Text : Theme.SecondaryText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, row.Detail, Theme.Small, new Rectangle(tx, r.Y + Scaled(36), tw, Scaled(20)),
                                  i == selected ? Color.FromArgb(210, 225, 255) : Theme.SecondaryText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (row.Saved != null && row.Service != null)
            {
                using var dot = new SolidBrush(Theme.Good);
                g.FillEllipse(dot, r.Right - Scaled(26), r.Y + r.Height / 2 - Scaled(4), Scaled(8), Scaled(8));
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = RowAt(e.Location);
        if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        int i = RowAt(e.Location);
        if (i >= 0) { selected = i; Invalidate(); }
        if (i >= 0 && e.Button == MouseButtons.Right) ContextRequested?.Invoke(rows[i], e.Location);
        base.OnMouseDown(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        int i = RowAt(e.Location);
        if (i >= 0 && e.Button == MouseButtons.Left) Activated?.Invoke(rows[i]);
        base.OnMouseDoubleClick(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        int total = rows.Count * Scaled(RowHeight + Gap);
        scroll = Math.Clamp(scroll - e.Delta / 2, 0, Math.Max(0, total - Height));
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && selected < rows.Count - 1) { selected++; Invalidate(); }
        else if (e.KeyCode == Keys.Up && selected > 0) { selected--; Invalidate(); }
        else if (e.KeyCode == Keys.Enter && Selected is { } r) Activated?.Invoke(r);
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
}

/// <summary>The connect screen, and the app's flow: the Macs Bonjour lists and those this PC paired
/// with, a connection by address, pairing with the Mac's code, and the stream window it hands a
/// session to (and gets back when the session ends, or dials again when it was lost).</summary>
public sealed partial class ConnectForm : Form
{
    readonly MdnsBrowser browser = new();
    readonly MacList list = new() { Dock = DockStyle.Fill };
    readonly Label title = new() { Text = "Sill", Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true };
    readonly Label subtitle = new() { Text = "Looking for Macs on your network…", ForeColor = Theme.SecondaryText, AutoSize = true };
    readonly PictureBox logo = new() { SizeMode = PictureBoxSizeMode.Zoom };
    readonly Label empty = new() { ForeColor = Theme.SecondaryText, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, Visible = false };
    readonly Label status = new() { ForeColor = Theme.SecondaryText, AutoSize = false, Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(2, 4, 2, 4) };
    readonly ThemedButton connect = new() { Primary = true, Text = "Connect", Width = 130 };
    readonly ThemedButton byAddress = new() { Text = "Connect by Address…", Width = 190 };
    readonly ThemedButton settings = new() { Text = "Settings", Width = 100 };
    readonly LinkLabel footer = new() { Text = "Needs the free Sill app on your Mac: getsill.app", AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent, ForeColor = Theme.SecondaryText };
    readonly System.Windows.Forms.Timer emptyTimer = new() { Interval = 4000 };

    StreamForm? stream;
    MacRow? streaming;
    CancellationTokenSource? dial;
    bool busy;

    public ConnectForm()
    {
        Theme.Apply(this);
        Text = "Sill";
        Icon = Theme.AppIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 560);
        MinimumSize = new Size(440, 420);
        StartPosition = FormStartPosition.CenterScreen;
        TestArguments.Place(this);
        KeyPreview = true;

        logo.Image = Theme.AppImage(64);
        var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.Background };
        header.Controls.AddRange(new Control[] { logo, title, subtitle });
        header.Layout += (_, _) =>
        {
            int s = (int)(52 * DeviceDpi / 96.0);
            logo.SetBounds(24, (header.Height - s) / 2, s, s);
            title.Location = new Point(logo.Right + 14, header.Height / 2 - title.Height + 4);
            subtitle.Location = new Point(logo.Right + 16, header.Height / 2 + 4);
        };

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 4, 22, 0), BackColor = Theme.Background };
        empty.Text = "No Mac found yet.\n\nOpen Sill on your Mac (it lives in the menu bar),\non the same network as this PC.";
        listHost.Controls.Add(list);
        listHost.Controls.Add(empty);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 132, Padding = new Padding(22, 0, 22, 14), BackColor = Theme.Background };
        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Theme.Background };
        buttons.Controls.AddRange(new Control[] { connect, byAddress, settings });
        buttons.Layout += (_, _) =>
        {
            connect.SetBounds(buttons.Width - connect.Width, 3, connect.Width, 34);
            byAddress.SetBounds(0, 3, byAddress.Width, 34);
            settings.SetBounds(byAddress.Right + 10, 3, settings.Width, 34);
        };
        var footerRow = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Theme.Background };
        footerRow.Controls.Add(footer);
        footer.Location = new Point(0, 8);
        bottom.Controls.Add(status);
        bottom.Controls.Add(buttons);
        bottom.Controls.Add(footerRow);
        status.BringToFront();

        Controls.Add(listHost);
        Controls.Add(bottom);
        Controls.Add(header);

        AcceptButton = connect;
        connect.Click += (_, _) =>
        {
            if (busy) dial?.Cancel();
            else if (list.Selected is { } r) _ = Connect(r);
        };
        byAddress.Click += (_, _) => ConnectByAddress();
        settings.Click += (_, _) => { using var d = new SettingsDialog(); d.ShowDialog(this); };
        footer.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://getsill.app") { UseShellExecute = true });
        list.Activated += r => _ = Connect(r);
        list.ContextRequested += ShowRowMenu;
        browser.Changed += () => { if (IsHandleCreated) BeginInvoke(Refresh); };
        emptyTimer.Tick += (_, _) => { emptyTimer.Stop(); UpdateEmpty(); };
        Shown += (_, _) =>
        {
            browser.Start();
            emptyTimer.Start();
            Refresh();
            list.Focus();
            // TEST: `Sill.exe --connect host:port [--pair-code DIGITS]` dials at once, as the iOS
            // harness's -SillConnect does; the code, when given, is typed into the pairing dialog.
            if (TestArguments.Connect is { } address)
            {
                if (address.Contains(':')) _ = ConnectToAddress(address);
                else _ = ConnectWhenListed(address);
            }
            else if (PendingLaunch == null && Settings.Current.AutoConnect && (ModifierKeys & Keys.Shift) == 0 &&
                     SavedMacs.Load().Macs.FirstOrDefault(m => m.MacID == Settings.Current.LastMacID) is { } last)
            {
                _ = AutoConnect(last);
            }
            if (TestArguments.QuitAfter is { } seconds)
                _ = Task.Delay(TimeSpan.FromSeconds(seconds)).ContinueWith(_ => BeginInvoke(() => { TestArguments.Log("quit"); quitting = true; Close(); }));
        };
        FormClosed += (_, _) => browser.Dispose();
    }

    bool lookedLongEnough;

    new void Refresh()
    {
        var saved = SavedMacs.Load();
        var rows = new List<MacRow>();
        var matched = new HashSet<string>();
        foreach (var svc in browser.Services)
        {
            var mac = saved.Match(svc.Tag, svc.Name);
            if (mac != null) matched.Add(mac.MacID);
            rows.Add(new MacRow { Name = svc.Name, Service = svc, Saved = mac });
        }
        foreach (var mac in saved.Macs.Where(m => !matched.Contains(m.MacID)))
            rows.Add(new MacRow { Name = mac.Name, Saved = mac });
        list.SetRows(rows);
        subtitle.Text = browser.Services.Count switch
        {
            0 => "Looking for Macs on your network…",
            1 => "1 Mac on your network",
            var n => $"{n} Macs on your network",
        };
        UpdateEmpty();
        connect.Enabled = !busy && list.Selected != null;
    }

    void UpdateEmpty()
    {
        if (!emptyTimer.Enabled) lookedLongEnough = true;
        empty.Visible = list.Count == 0 && lookedLongEnough;
        list.Visible = list.Count > 0 || !lookedLongEnough;
    }

    void ShowRowMenu(MacRow row, Point at)
    {
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer() };
        menu.Items.Add("Connect", null, (_, _) => _ = Connect(row));
        if (row.Saved != null)
            menu.Items.Add("Forget This Mac", null, (_, _) =>
            {
                SavedMacs.Update(s => s.Macs.RemoveAll(m => m.MacID == row.Saved.MacID));
                if (Settings.Current.LastMacID == row.Saved.MacID) { Settings.Current.LastMacID = null; Settings.Save(); }
                SetStatus($"Forgot {row.Name}. Connecting again pairs again.");
                Refresh();
            });
        menu.Show(list, at);
    }

    void SetStatus(string text, bool problem = false)
    {
        if (text.Length > 0) TestArguments.Log($"status: {text}");
        status.Text = text;
        status.ForeColor = problem ? Theme.Warning : Theme.SecondaryText;
    }

    void SetBusy(bool on)
    {
        busy = on;
        byAddress.Enabled = !on;
        connect.Text = on ? "Cancel" : "Connect";
        connect.Enabled = on || list.Selected != null;
    }

    void ConnectByAddress()
    {
        using var d = new AddressDialog(Settings.Current.LastMac);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _ = ConnectToAddress(d.Address);
    }

    /// <summary>The Mac streamed last, as Sill opens: as soon as Bonjour lists it, else (4 s on) at the
    /// address it was last reached at. Cancel stays on this screen, and so does Shift held at launch.</summary>
    async Task AutoConnect(SavedMac mac)
    {
        var wait = new CancellationTokenSource();
        dial = wait;
        SetBusy(true);
        SetStatus($"Connecting to {mac.Name}…");
        MacRow? row = null;
        try
        {
            for (int i = 0; i < 16; i++)
            {
                var current = Current(new MacRow { Name = mac.Name, Saved = mac });
                if (current.Service != null) { row = current; break; }
                await Task.Delay(250, wait.Token);
            }
        }
        catch (OperationCanceledException)
        {
            SetBusy(false);
            SetStatus("");
            return;
        }
        SetBusy(false);
        TestArguments.Log($"auto-connect: {mac.Name} {(row != null ? "listed" : $"at its last address {mac.LastAddress}:{mac.LastPort}")}");
        await Connect(row ?? new MacRow { Name = mac.Name, Saved = mac });
    }

    /// <summary>TEST: `--connect NAME` connects to the Mac Bonjour lists by that name, once it does (15 s at most).</summary>
    async Task ConnectWhenListed(string name)
    {
        for (int i = 0; i < 60; i++)
        {
            var svc = browser.Services.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (svc != null)
            {
                var saved = SavedMacs.Load().Match(svc.Tag, svc.Name);
                await Connect(new MacRow { Name = svc.Name, Service = svc, Saved = saved });
                return;
            }
            await Task.Delay(250);
        }
        SetStatus($"{name} isn’t listed.", true);
    }

    async Task ConnectToAddress(string text)
    {
        dial = new CancellationTokenSource();
        try
        {
            SetBusy(true);
            SetStatus($"Looking up {text}…");
            var endpoints = await Transport.Resolve(text, dial.Token);
            Settings.Current.LastMac = text;
            Settings.Save();
            // An address has no TXT record: a saved Mac's pin when one was last reached here, else the
            // door as it answers (TLS first; a plain door fails its handshake at once).
            var saved = SavedMacs.Load().Macs.FirstOrDefault(m => endpoints.Any(ep => m.LastAddress == ep.Address.ToString() && m.LastPort == ep.Port));
            var row = new MacRow { Name = saved?.Name ?? text, Saved = saved, Service = new MacService { FullName = text, Name = saved?.BonjourName ?? text, Port = endpoints[0].Port, Addresses = endpoints.Select(e => e.Address).ToList(), Txt = new(StringComparer.OrdinalIgnoreCase) { ["p"] = "1" } } };
            SetBusy(false);
            await Connect(row, byAddress: true);
        }
        catch (Exception e) when (e is FormatException or System.Net.Sockets.SocketException or OperationCanceledException)
        {
            SetStatus(e is OperationCanceledException ? "" : e.Message, true);
            SetBusy(false);
        }
    }

    /// <summary>Dials a Mac and hands the session to the stream window, pairing first when this PC must.</summary>
    async Task Connect(MacRow row, bool byAddress = false, bool pairFirst = false)
    {
        if (busy) return;
        var endpoints = row.Endpoints;
        if (endpoints.Count == 0) { SetStatus($"{row.Name} isn’t on this network now.", true); return; }
        dial = new CancellationTokenSource();
        var ct = dial.Token;
        SetBusy(true);
        try
        {
            SetStatus($"Connecting to {row.Name}…");
            Dialed? d = null;
            var saved = row.Saved;
            if (row.Door == Door.Plain)
            {
                d = await Transport.Dial(endpoints, null, null, ct);
            }
            else
            {
                if (saved == null && (row.Door == Door.PairingRequired && !byAddress || pairFirst))
                {
                    saved = await Pair(row, endpoints, ct);
                    if (saved == null) return;
                }
                try
                {
                    d = await Transport.Dial(endpoints, Transport.SessionALPN, saved?.Pin, ct);
                }
                catch (DialException e) when (e.Failure == DialFailure.PinRefused && saved != null)
                {
                    if (MessageBox.Show(this, $"{row.Name} has a new key since this PC paired (Sill was set up again, or this is another Mac). Pair again?",
                                        "Sill", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) { SetStatus(""); return; }
                    saved = await Pair(row, endpoints, ct);
                    if (saved == null) return;
                    d = await Transport.Dial(endpoints, Transport.SessionALPN, saved.Pin, ct);
                }
                catch (DialException e) when (e.Failure == DialFailure.Handshake && byAddress)
                {
                    // Typed addresses: maybe a plain door (SillHost without --pairing).
                    d = await Transport.Dial(endpoints, null, null, ct);
                }
            }
            if (saved != null)
            {
                var ep = d.Endpoint;
                SavedMacs.Update(s =>
                {
                    var m = s.Macs.FirstOrDefault(x => x.MacID == saved.MacID);
                    if (m == null) return;
                    m.LastAddress = ep.Address.ToString(); m.LastPort = ep.Port; m.LastConnectedAt = DateTimeOffset.Now;
                    if (row.Service != null && !byAddress) m.BonjourName = row.Service.Name;
                });
                Settings.Current.LastMacID = saved.MacID;
                Settings.Save();
            }
            SetStatus("");
            StartStream(d, row, saved);
        }
        catch (OperationCanceledException) { SetStatus(""); }
        catch (DialException e)
        {
            SetStatus(e.Failure switch
            {
                DialFailure.NoAnswer => $"{row.Name} didn’t answer. Is Sill open on it, on this network?",
                DialFailure.PinRefused => $"{row.Name} answered with another key.",
                _ => row.Door == Door.PairingRequired && row.Saved != null
                    ? $"{row.Name} didn’t accept this PC. It may have removed it: right-click it and choose Forget, then connect to pair again."
                    : $"Couldn’t connect securely to {row.Name}: {e.Message}",
            }, true);
            browser.Refresh();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Net.Sockets.SocketException)
        {
            SetStatus($"Couldn’t connect to {row.Name}: {e.Message}", true);
        }
        finally
        {
            SetBusy(false);
            Refresh();
        }
    }

    /// <summary>The ask, then the code: a saved Mac, or null (refused, cancelled, or a problem shown).</summary>
    async Task<SavedMac?> Pair(MacRow row, IReadOnlyList<IPEndPoint> endpoints, CancellationToken ct)
    {
        SetStatus($"Asking {row.Name} to pair…");
        var answer = await PairingClient.Ask(endpoints, ct);
        var r = answer.Result;
        if (r.Ok)
        {
            // An ok without a proof is only ever the USB cable's answer, which this PC never claims.
            SetStatus($"{row.Name} answered in a way this PC doesn’t accept.", true);
            return null;
        }
        if (r.Reason is not ("shown" or "openOnMac"))
        {
            SetStatus(PairingClient.Describe(r, row.Name), true);
            return null;
        }
        SetStatus($"Type the code {row.Name} shows.");
        SavedMac? saved = null;
        var explanation = r.Reason == "shown"
            ? $"{row.Name} shows a pairing code now. Type its 12 digits here."
            : $"On {row.Name}, choose Pair iPhone or iPad… from Sill’s menu, then type the 12 digits it shows.";
        using var dialog = new CodeDialog(row.Name, explanation, async code =>
        {
            var (result, mac) = await PairingClient.PairWithCode(endpoints, answer.MacFingerprint, code, row.BonjourName, ct);
            if (mac != null) { saved = mac; return null; }
            return PairingClient.Describe(result, row.Name);
        });
        if (dialog.ShowDialog(this) != DialogResult.OK || saved == null)
        {
            if (r.Reason == "shown") _ = PairingClient.Cancel(endpoints, answer.MacFingerprint);
            SetStatus("");
            return null;
        }
        SetStatus($"Paired with {saved.Name}.");
        return saved;
    }

    void StartStream(Dialed d, MacRow row, SavedMac? with_saved)
    {
        streaming = row;
        if (stream == null || stream.IsDisposed)
        {
            stream = new StreamForm();
            stream.Disconnected += OnStreamClosed;
            stream.Show();
        }
        var session = new Session(d, with_saved?.Name ?? row.Name, stream.Video);
        session.Ended += end => BeginInvoke(() => OnSessionEnded(session, end));
        stream.Attach(session);
        HandStreamItsLaunch();
        Hide();
    }

    bool quitting;

    void OnStreamClosed()
    {
        stream = null;
        streaming = null;
        if (quitting || IsDisposed) return;
        Show();
        Activate();
        Refresh();
    }

    /// <summary>A session ended without the user: back to the list with its words, or dialed again.</summary>
    async void OnSessionEnded(Session session, SessionEnd end)
    {
        var s = stream;
        if (s == null || s.IsDisposed || !ReferenceEquals(s.Session, session)) return;
        var row = streaming;
        if (end.Reason is "unpaired" or "removed" && row?.Saved != null)
            SavedMacs.Update(x => x.Macs.RemoveAll(m => m.MacID == row.Saved.MacID));
        if (end.Reason == "unpaired" && row != null)
        {
            // The Mac's door wants pairing (an address has no TXT record to say so, or the Mac forgot this PC).
            s.CloseForEnd();
            SetStatus(end.Message, true);
            await Connect(new MacRow { Name = row.Name, Service = row.Service }, byAddress: false, pairFirst: true);
            return;
        }
        if (end.Reconnect && row != null)
        {
            s.ShowMessage($"Reconnecting to {session.MacName}…");
            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
            int wait = 1000;
            while (DateTime.UtcNow < deadline && !s.IsDisposed && ReferenceEquals(s.Session, session))
            {
                await Task.Delay(wait);
                wait = Math.Min(wait * 2, 8000);
                if (s.IsDisposed || !ReferenceEquals(s.Session, session)) return;
                var current = Current(row);
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                    var d = current.Door == Door.Plain
                        ? await Transport.Dial(current.Endpoints, null, null, cts.Token)
                        : await Transport.Dial(current.Endpoints, Transport.SessionALPN, current.Saved?.Pin, cts.Token);
                    var next = new Session(d, current.Saved?.Name ?? current.Name, s.Video);
                    next.Ended += e => BeginInvoke(() => OnSessionEnded(next, e));
                    streaming = current;
                    s.Attach(next);
                    return;
                }
                catch (Exception e) when (e is DialException or OperationCanceledException or IOException or System.Net.Sockets.SocketException) { }
            }
            if (s.IsDisposed || !ReferenceEquals(s.Session, session)) return;
            end = end with { Message = $"Couldn’t reach {session.MacName} again." };
        }
        s.CloseForEnd();
        SetStatus(end.Message, true);
    }

    /// <summary>The row as Bonjour lists it now (a Mac back from sleep may have another port).</summary>
    MacRow Current(MacRow row)
    {
        foreach (var svc in browser.Services)
        {
            if (row.Saved != null ? RecognitionTag.Matches(svc.Tag, row.Saved.RecognitionKeyBytes) || svc.Tag == null && svc.Name == row.Saved.BonjourName
                                  : svc.FullName == row.Service?.FullName)
                return new MacRow { Name = row.Name, Service = svc, Saved = row.Saved };
        }
        return row;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        quitting = true;
        if (stream != null && !stream.IsDisposed) { stream.Close(); }
        base.OnFormClosing(e);
    }
}
