namespace CanHost.Docking;

internal sealed class FloatingModuleForm : Form
{
    private readonly DockableWorkspace _workspace;

    public FloatingModuleForm(DockableWorkspace workspace, DockableModuleHost host)
    {
        _workspace = workspace;
        Host = host;
        Text = host.ModuleTitle;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = true;
        MaximizeBox = true;
        ShowInTaskbar = true;
        ShowIcon = false;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.Text;
        MinimumSize = new Size(
            host.MinimumSize.Width + 20,
            host.MinimumSize.Height + 45);

        host.Dock = DockStyle.Fill;
        Controls.Add(host);
        ThemeApplicator.Apply(this);

        Resize += (_, _) => host.RefreshWindowButtonState();
        Move += (_, _) => _workspace.SaveLayout();
        ResizeEnd += (_, _) => _workspace.SaveLayout();
        FormClosing += OnFormClosing;
    }

    public DockableModuleHost Host { get; }
    public bool AllowClose { get; set; }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (AllowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        _workspace.NotifyModuleVisibilityChanged(Host, false);
        _workspace.SaveLayout();
    }
}
