using Sill.Windows.Protocol;

namespace Sill.Windows.UI;

/// <summary>Choose Windows…: the Mac's windows with a check each, and whether their apps' new windows
/// join by themselves (kind 29's `apps`), for entering Coherence with more than the window streaming.</summary>
sealed class CoherencePicker : Form
{
    readonly CheckedListBox list = new() { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, IntegralHeight = false };
    readonly CheckBox wholeApps = new() { Text = "Also show these apps’ new windows as they open", AutoSize = true, ForeColor = Theme.Text, Checked = true };
    readonly List<WindowInfo> windows;

    public CoherenceChoice? Choice { get; private set; }

    public CoherencePicker(IReadOnlyList<WindowInfo> windows, uint? current)
    {
        this.windows = windows.ToList();
        Theme.Apply(this);
        Text = "Show Windows Separately";
        Icon = Theme.AppIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 420);
        var heading = new Label { Text = "Each window you choose opens as a window of this PC.", Dock = DockStyle.Top, Height = 36, ForeColor = Theme.SecondaryText, Padding = new Padding(12, 10, 12, 0) };
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 92, Padding = new Padding(12, 8, 12, 12), BackColor = Theme.Background };
        var ok = new ThemedButton { Text = "Show", Primary = true, Width = 110 };
        var cancel = new ThemedButton { Text = "Cancel", Width = 110 };
        bottom.Controls.AddRange(new Control[] { wholeApps, ok, cancel });
        bottom.Layout += (_, _) =>
        {
            wholeApps.Location = new Point(12, 8);
            ok.SetBounds(bottom.Width - ok.Width - 12, bottom.Height - 46, ok.Width, 34);
            cancel.SetBounds(ok.Left - cancel.Width - 10, bottom.Height - 46, cancel.Width, 34);
        };
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 4), BackColor = Theme.Background };
        host.Controls.Add(list);
        Controls.Add(host);
        Controls.Add(bottom);
        Controls.Add(heading);
        foreach (var w in this.windows)
        {
            var title = string.IsNullOrWhiteSpace(w.Title) ? w.AppName : $"{w.AppName} — {w.Title}";
            list.Items.Add(SafeText.Label(title, 100), w.Id == current);
        }
        AcceptButton = ok;
        CancelButton = cancel;
        ok.Click += (_, _) =>
        {
            var chosen = list.CheckedIndices.Cast<int>().Select(i => this.windows[i]).ToList();
            if (chosen.Count == 0) { DialogResult = DialogResult.Cancel; return; }
            var apps = wholeApps.Checked ? chosen.Select(w => w.BundleID).Where(b => b.Length > 0).Distinct().ToList() : new List<string>();
            uint focus = chosen.FirstOrDefault(w => w.Id == current)?.Id ?? chosen[0].Id;
            Choice = new CoherenceChoice(chosen.Select(w => w.Id).ToList(), apps, false, focus);
            DialogResult = DialogResult.OK;
        };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
    }
}
