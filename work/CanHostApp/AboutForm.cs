using System.Diagnostics;

namespace CanHost;

public sealed class AboutForm : Form
{
    private const string RepositoryUrl =
        "https://github.com/cloudc9999-stack/CG_HostComputer";

    public AboutForm()
    {
        Text = "关于";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(560, 260);
        Font = new Font("Microsoft YaHei UI", 9F);

        var title = new Label
        {
            Text = AppInfo.DisplayName,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
            Location = new Point(26, 22)
        };
        var description = new Label
        {
            Text = "Windows CAN 上位机 · 储供系统监控与控制",
            AutoSize = true,
            ForeColor = AppTheme.Muted,
            Location = new Point(28, 61)
        };
        var updateLabel = new Label
        {
            Text = "获取最新更新：",
            AutoSize = true,
            ForeColor = AppTheme.Text,
            Location = new Point(28, 105)
        };
        var link = new LinkLabel
        {
            Text = RepositoryUrl,
            AutoSize = true,
            LinkColor = AppTheme.Accent,
            ActiveLinkColor = AppTheme.Accent,
            VisitedLinkColor = AppTheme.Accent,
            Location = new Point(28, 131)
        };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo
        {
            FileName = RepositoryUrl,
            UseShellExecute = true
        });
        var ok = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Location = new Point(436, 196),
            Size = new Size(94, 36)
        };
        ok.FlatStyle = FlatStyle.Flat;
        ok.FlatAppearance.BorderColor = AppTheme.Border;
        ok.BackColor = AppTheme.SecondaryButton;
        ok.ForeColor = AppTheme.Text;

        Controls.AddRange([title, description, updateLabel, link, ok]);
        AcceptButton = ok;
        CancelButton = ok;
        ThemeApplicator.Apply(this);
    }
}
