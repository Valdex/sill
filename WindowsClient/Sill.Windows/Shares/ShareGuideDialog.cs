using Sill.Windows.Net;
using Sill.Windows.Protocol;
using Sill.Windows.UI;

namespace Sill.Windows.Shares;

/// <summary>The guide to the Mac's files on this PC (View › The Mac's Files on This PC…): what to turn on
/// on the Mac (File Sharing, SMB, the account), then the Mac's name (found by Bonjour: a Mac whose File
/// Sharing is on advertises `_smb._tcp`), the share (an account's home folder is shared under its short
/// name) and the account's password, mapped to a drive. The password goes to Windows' Credential
/// Manager, never to Sill's settings. Saved shares are listed, and "Open on Mac" is offered for them.</summary>
public sealed class ShareGuideDialog : ThemedDialog
{
    readonly Label steps = new() { ForeColor = Theme.Text, AutoSize = false };
    readonly Label found = new() { ForeColor = Theme.SecondaryText, Font = Theme.Small, AutoSize = false };
    readonly TextBox host = Field(), account = Field(), share = Field(), macPath = Field(), password = Field();
    readonly ComboBox drive = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface, ForeColor = Theme.Text };
    readonly CheckBox verb = new() { Text = "Add “Open on Mac” to Explorer’s menu (files on the Mac’s drive open there)", ForeColor = Theme.Text, AutoSize = false, FlatStyle = FlatStyle.System };
    readonly ListBox saved = new() { BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
    readonly ThemedButton remove = new() { Text = "Remove", Width = 100 };
    readonly Label[] labels;
    readonly Session? session;
    readonly MdnsBrowser smb = new("_smb._tcp.local");
    bool shareEdited, pathEdited, hostEdited;

    static TextBox Field() => new() { BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface, ForeColor = Theme.Text };
    static Label Caption(string text) => new() { Text = text, ForeColor = Theme.SecondaryText, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };

    public ShareGuideDialog(Session? session) : base("The Mac’s Files on This PC", 600, 700)
    {
        this.session = session;
        var mac = session?.MacName ?? "the Mac";
        HeadingLabel.Text = "The Mac’s files on this PC";
        steps.Text =
            $"1. On {mac}, open System Settings › General › Sharing and turn on File Sharing.\n" +
            "2. Click ⓘ beside File Sharing, then Options…: turn on “Share files and folders using SMB”, tick your account under Windows File Sharing and type its password there.\n" +
            "3. Here: the Mac’s name, your Mac account’s short name and password. Your home folder on the Mac becomes a drive, and files on it open on the Mac with Open on Mac.";
        Ok.Text = "Connect";
        CancelButtonControl.Text = "Close";
        password.UseSystemPasswordChar = true;
        labels = new[] { Caption("Mac’s name"), Caption("Account"), Caption("Share"), Caption("Folder on the Mac"), Caption("Password"), Caption("Drive") };
        foreach (var d in ShareConnector.FreeDrives()) drive.Items.Add(d);
        drive.Items.Add("No drive letter");
        drive.SelectedIndex = 0;
        verb.Checked = true;
        if (OpenOnMac.Registered) verb.Text = "“Open on Mac” in Explorer’s menu (untick to remove it)";
        found.Text = "Looking for Macs that share files…";
        account.PlaceholderText = "the name of your home folder on the Mac";
        host.TextChanged += (_, _) => { if (host.Focused) hostEdited = true; };
        account.TextChanged += (_, _) =>
        {
            if (!shareEdited) share.Text = account.Text.Trim();
        };
        share.TextChanged += (_, _) =>
        {
            if (share.Focused) shareEdited = true;
            if (!pathEdited) macPath.Text = share.Text.Trim().Length > 0 ? PathMapping.DefaultMacPath(share.Text) : "";
        };
        macPath.TextChanged += (_, _) => { if (macPath.Focused) pathEdited = true; };
        Body.Controls.AddRange(new Control[] { HeadingLabel, steps, found, host, account, share, macPath, password, drive, verb, saved, remove, Problem });
        Body.Controls.AddRange(labels);
        Ok.Click += async (_, _) => await ConnectClicked();
        remove.Click += (_, _) => RemoveSelected();
        FillSaved();
        smb.Changed += () => { if (IsHandleCreated) BeginInvoke(SuggestHost); };
        Shown += (_, _) => { smb.Start(); SuggestHost(); };
        FormClosed += (_, _) => smb.Dispose();
    }

    /// <summary>The Mac's name from Bonjour: the `_smb._tcp` service at the session's address (File
    /// Sharing is on), else the only one there is.</summary>
    void SuggestHost()
    {
        var services = smb.Services.Where(s => !string.IsNullOrEmpty(s.HostName)).ToList();
        var at = session?.Endpoint.Address;
        var match = services.FirstOrDefault(s => at != null && s.Addresses.Any(a => a.Equals(at))) ?? (services.Count == 1 ? services[0] : null);
        if (match != null)
        {
            if (!hostEdited) host.Text = match.HostName!.TrimEnd('.');
            found.Text = $"{match.Name} shares files over SMB.";
        }
        else if (services.Count > 1) found.Text = $"Macs sharing files: {string.Join(", ", services.Select(s => s.HostName!.TrimEnd('.')))}.";
        else found.Text = "No Mac sharing files has answered yet: turn on File Sharing and SMB (steps 1 and 2), or type the Mac’s name.";
        if (host.Text.Length == 0 && !hostEdited && at != null) host.Text = at.ToString();
    }

    void FillSaved()
    {
        saved.Items.Clear();
        foreach (var s in ShareStore.Load()) saved.Items.Add(new Row(s));
        remove.Enabled = saved.Items.Count > 0;
    }

    sealed record Row(MacShare Share)
    {
        public override string ToString() => $"{Share.Unc}{(Share.Drive != null ? $" as {Share.Drive}" : "")}  →  {Share.MacName}: {Share.MacPath}";
    }

    void RemoveSelected()
    {
        if (saved.SelectedItem is not Row r) return;
        if (r.Share.Drive != null) ShareConnector.Disconnect(r.Share.Drive);
        ShareStore.Remove(r.Share);
        FillSaved();
    }

    async Task ConnectClicked()
    {
        string h = host.Text.Trim().TrimStart('\\').TrimEnd('.'), a = account.Text.Trim(), sh = share.Text.Trim().Trim('\\', '/'), mp = macPath.Text.Trim(), pw = password.Text;
        if (h.Length == 0 || a.Length == 0 || sh.Length == 0) { Problem.Text = "Type the Mac’s name, your account and the share."; return; }
        if (!mp.StartsWith('/')) { Problem.Text = "The folder on the Mac is a path from /, like /Users/noah."; return; }
        string? letter = drive.SelectedItem is string d && d.EndsWith(':') ? d : null;
        Problem.ForeColor = Theme.SecondaryText;
        Problem.Text = $"Connecting to \\\\{h}\\{sh}…";
        Ok.Enabled = false;
        var error = await Task.Run(() => ShareConnector.Connect(h, sh, letter, a, pw));
        password.Text = "";
        Ok.Enabled = true;
        Problem.ForeColor = Theme.Warning;
        if (error != null) { Problem.Text = error; return; }
        var made = new MacShare
        {
            MacName = session?.MacName ?? h, Host = h, Share = sh, Drive = letter, MacPath = mp,
            MacID = session?.MacFingerprint is { } fp ? MacID.Make(fp) : null,
        };
        ShareStore.Save(made);
        try { OpenOnMac.Register(verb.Checked); }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        FillSaved();
        Problem.ForeColor = Theme.SecondaryText;
        Problem.Text = letter != null ? $"Connected: {letter} is {made.MacName}’s {sh}." : $"Connected: \\\\{h}\\{sh}.";
        TestArguments.Log($"shares: mapped {made.Unc}{(letter != null ? " as " + letter : "")} → {mp}");
    }

    protected override void LayoutBody()
    {
        int x = Body.Padding.Left, w = Body.ClientSize.Width - Body.Padding.Horizontal, y = Body.Padding.Top;
        HeadingLabel.SetBounds(x, y, w, 30); y += 36;
        steps.SetBounds(x, y, w, 132); y += 138;
        found.SetBounds(x, y, w, 34); y += 38;
        var fields = new Control[] { host, account, share, macPath, password, drive };
        int lw = 140;
        for (int i = 0; i < fields.Length; i++)
        {
            labels[i].SetBounds(x, y, lw, 28);
            fields[i].SetBounds(x + lw, y + 2, w - lw, 26);
            y += 34;
        }
        verb.SetBounds(x, y, w, 28); y += 34;
        Problem.SetBounds(x, y, w, 44); y += 48;
        int listH = Math.Max(50, Body.ClientSize.Height - y - 8);
        saved.SetBounds(x, y, w - 110, listH);
        remove.SetBounds(x + w - 100, y, 100, 32);
    }
}
