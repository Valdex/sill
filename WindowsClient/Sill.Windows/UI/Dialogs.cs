using Sill.Windows.Net;
using Sill.Windows.Protocol;

namespace Sill.Windows.UI;

/// <summary>A small themed dialog: a heading, a line of explanation, an optional text box, a line for
/// problems, and its buttons.</summary>
public class ThemedDialog : Form
{
    protected readonly Label HeadingLabel = new() { AutoSize = false, Font = Theme.Heading, ForeColor = Theme.Text, Height = 30 };
    protected readonly Label Explanation = new() { AutoSize = false, ForeColor = Theme.SecondaryText, Height = 44 };
    protected readonly Label Problem = new() { AutoSize = false, ForeColor = Theme.Warning, Height = 40, Font = Theme.Small };
    protected readonly ThemedButton Ok = new() { Primary = true, Width = 110, Text = "OK" };
    protected readonly ThemedButton CancelButtonControl = new() { Width = 110, Text = "Cancel" };
    protected readonly FlowLayoutPanel Buttons = new() { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(16, 10, 16, 12), BackColor = Theme.Background };
    protected readonly Panel Body = new() { Dock = DockStyle.Fill, Padding = new Padding(22, 18, 22, 0), BackColor = Theme.Background };

    public ThemedDialog(string title, int width = 460, int height = 300)
    {
        Theme.Apply(this);
        Text = title;
        Icon = Theme.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(width, height);
        Buttons.Controls.Add(Ok);
        Buttons.Controls.Add(CancelButtonControl);
        Controls.Add(Body);
        Controls.Add(Buttons);
        AcceptButton = Ok;
        CancelButton = CancelButtonControl;
        CancelButtonControl.DialogResult = DialogResult.Cancel;
        Layout += (_, _) => LayoutBody();
    }

    protected virtual void LayoutBody() { }
}

/// <summary>The 12 digits the Mac's pairing window shows: checked here first (a typo never costs one of
/// the Mac's tries), then derived and sent by the caller.</summary>
public sealed class CodeDialog : ThemedDialog
{
    readonly TextBox code = new() { Font = new Font("Consolas", 20f), TextAlign = HorizontalAlignment.Center, MaxLength = 16, BorderStyle = BorderStyle.FixedSingle };
    readonly Func<string, Task<string?>> submit;

    /// <summary><paramref name="submit"/> pairs with the code and answers null on success, else the problem
    /// to show (the dialog stays open for another try).</summary>
    public CodeDialog(string mac, string explanation, Func<string, Task<string?>> submit) : base($"Pair with {mac}", 470, 290)
    {
        this.submit = submit;
        HeadingLabel.Text = $"Pair with {mac}";
        Explanation.Text = explanation;
        Ok.Text = "Pair";
        code.BackColor = Theme.Surface;
        code.ForeColor = Theme.Text;
        code.PlaceholderText = "0000 0000 0000";
        code.KeyPress += (_, e) => { if (!char.IsDigit(e.KeyChar) && e.KeyChar != ' ' && e.KeyChar != '-' && !char.IsControl(e.KeyChar)) e.Handled = true; };
        code.TextChanged += (_, _) => Problem.Text = "";
        Body.Controls.AddRange(new Control[] { HeadingLabel, Explanation, code, Problem });
        Ok.Click += async (_, _) => await Submit();
    }

    protected override void LayoutBody()
    {
        int w = Body.ClientSize.Width - Body.Padding.Horizontal;
        HeadingLabel.SetBounds(Body.Padding.Left, Body.Padding.Top, w, 30);
        Explanation.SetBounds(Body.Padding.Left, HeadingLabel.Bottom + 4, w, 44);
        code.SetBounds(Body.Padding.Left, Explanation.Bottom + 8, w, code.PreferredHeight);
        Problem.SetBounds(Body.Padding.Left, code.Bottom + 8, w, 40);
    }

    async Task Submit()
    {
        var (checkedCode, problem) = PairingCode.Check(code.Text);
        if (checkedCode == null)
        {
            Problem.Text = problem == PairingCode.Problem.Typo ? "That code has a typo. Check it against your Mac." : "A code has 12 digits.";
            return;
        }
        Ok.Enabled = code.Enabled = false;
        Problem.ForeColor = Theme.SecondaryText;
        Problem.Text = "Pairing…";
        string? failure;
        try { failure = await submit(checkedCode); }
        catch (Exception e) { failure = e.Message; }
        Problem.ForeColor = Theme.Warning;
        if (failure == null)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        Problem.Text = failure;
        Ok.Enabled = code.Enabled = true;
        code.SelectAll();
        code.Focus();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        code.Focus();
        if (TestArguments.PairCode is { } test)
        {
            code.Text = test;
            BeginInvoke(async () => await Submit());
        }
    }
}

/// <summary>A Mac by address, for a network Bonjour does not cross: the address and Sill's port, which
/// the Mac's Settings › Remote Access, or SillHost's first lines, show.</summary>
public sealed class AddressDialog : ThemedDialog
{
    readonly TextBox address = new() { BorderStyle = BorderStyle.FixedSingle };
    public string Address => address.Text.Trim();

    public AddressDialog(string? initial) : base("Connect by Address", 470, 270)
    {
        HeadingLabel.Text = "Connect by Address";
        Explanation.Text = "Type the Mac’s address and Sill’s port, like 192.168.1.20:52345 or my-mac.local:52345.";
        Ok.Text = "Connect";
        address.BackColor = Theme.Surface;
        address.ForeColor = Theme.Text;
        address.Font = new Font(Theme.Body.FontFamily, 12f);
        address.Text = initial ?? "";
        Body.Controls.AddRange(new Control[] { HeadingLabel, Explanation, address, Problem });
        Ok.Click += (_, _) =>
        {
            if (Address.LastIndexOf(':') <= 0) { Problem.Text = "Add Sill’s port after a colon."; return; }
            DialogResult = DialogResult.OK;
            Close();
        };
    }

    protected override void LayoutBody()
    {
        int w = Body.ClientSize.Width - Body.Padding.Horizontal;
        HeadingLabel.SetBounds(Body.Padding.Left, Body.Padding.Top, w, 30);
        Explanation.SetBounds(Body.Padding.Left, HeadingLabel.Bottom + 4, w, 44);
        address.SetBounds(Body.Padding.Left, Explanation.Bottom + 8, w, address.PreferredHeight);
        Problem.SetBounds(Body.Padding.Left, address.Bottom + 8, w, 40);
    }
}

/// <summary>This PC's preferences.</summary>
public sealed class SettingsDialog : ThemedDialog
{
    readonly CheckBox ctrlIsCommand = Check("Ctrl acts as ⌘ Command (and the Windows key as ⌃ Control)");
    readonly CheckBox windowsKey = Check("Send the Windows key to the Mac");
    readonly CheckBox clipboard = Check("Share the clipboard with the Mac");
    readonly CheckBox backForward = Check("Mouse back and forward buttons go back and forward (⌘[ and ⌘])");
    readonly CheckBox stats = Check("Show frame rate and latency");
    readonly CheckBox autoConnect = Check("Connect to the last Mac when Sill opens (hold Shift to skip)");
    readonly CheckBox atLogin = Check("Open Sill when you sign in to Windows");
    readonly Label scrollLabel = new() { Text = "Scroll speed", ForeColor = Theme.Text, AutoSize = true };
    readonly TrackBar scroll = new() { Minimum = 10, Maximum = 120, TickFrequency = 10, SmallChange = 5, LargeChange = 10 };
    readonly Label note = new() { ForeColor = Theme.SecondaryText, Font = Theme.Small, AutoSize = false, Height = 68 };

    static CheckBox Check(string text) => new() { Text = text, ForeColor = Theme.Text, AutoSize = false, Height = 28, FlatStyle = FlatStyle.System };

    CheckBox[] Checks => new[] { ctrlIsCommand, windowsKey, clipboard, backForward, stats, autoConnect, atLogin };

    public SettingsDialog() : base("Sill Settings", 540, 560)
    {
        var s = Settings.Current;
        HeadingLabel.Text = "Settings";
        Ok.Text = "Done";
        CancelButtonControl.Visible = false;
        ctrlIsCommand.Checked = s.CtrlIsCommand;
        windowsKey.Checked = s.WindowsKeyToMac;
        clipboard.Checked = s.ShareClipboard;
        backForward.Checked = s.MouseBackForward;
        stats.Checked = s.ShowStats;
        autoConnect.Checked = s.AutoConnect;
        bool login = false;
        try { login = LoginItem.Enabled; } catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { atLogin.Enabled = false; }
        atLogin.Checked = login;
        scroll.Value = (int)Math.Clamp(s.ScrollPoints, scroll.Minimum, scroll.Maximum);
        scroll.BackColor = Theme.Background;
        note.Text = "Keys follow the Mac’s keyboard layout. In a window, Alt+Tab stays with Windows; in full screen it goes to the Mac. " +
                    "Ctrl+Alt+Enter switches full screen; Ctrl+Alt+Shift with a letter is one of Sill’s own shortcuts (the stream’s View menu lists them).";
        Body.Controls.Add(HeadingLabel);
        Body.Controls.AddRange(Checks);
        Body.Controls.AddRange(new Control[] { scrollLabel, scroll, note });
        Ok.Click += (_, _) =>
        {
            s.CtrlIsCommand = ctrlIsCommand.Checked;
            s.WindowsKeyToMac = windowsKey.Checked;
            s.ShareClipboard = clipboard.Checked;
            s.MouseBackForward = backForward.Checked;
            s.ShowStats = stats.Checked;
            s.AutoConnect = autoConnect.Checked;
            s.ScrollPoints = scroll.Value;
            Settings.Save();
            if (atLogin.Enabled && atLogin.Checked != login)
            {
                try { LoginItem.Enabled = atLogin.Checked; }
                catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            }
            DialogResult = DialogResult.OK;
            Close();
        };
    }

    protected override void LayoutBody()
    {
        int x = Body.Padding.Left, w = Body.ClientSize.Width - Body.Padding.Horizontal, y = Body.Padding.Top;
        HeadingLabel.SetBounds(x, y, w, 30); y += 40;
        foreach (var c in Checks) { c.SetBounds(x, y, w, 28); y += 32; }
        y += 6;
        scrollLabel.Location = new Point(x, y + 6);
        scroll.SetBounds(x + 110, y, w - 110, 40); y += 48;
        note.SetBounds(x, y, w, 68);
    }
}
