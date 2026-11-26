using CanHost.Modules;

namespace CanHost;

public sealed class PressurePlotWindowManager : IDisposable
{
    private readonly PlotModule[] _plots;
    private readonly Form?[] _windows;
    private bool _isClosing;

    public PressurePlotWindowManager(PlotModule[] plots)
    {
        _plots = plots;
        _windows = new Form?[plots.Length];
    }

    public void ShowConfiguration(IWin32Window owner)
    {
        using var dialog = new PressureMonitorSettingsForm(
            _plots.Select(plot => plot.Title).ToArray(),
            _plots.Select((_, index) => _windows[index]?.Visible == true).ToArray());
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            return;
        }

        for (var index = 0; index < _plots.Length; index++)
        {
            SetWindowVisible(index, dialog.SelectedStates[index]);
        }
    }

    private void SetWindowVisible(int index, bool visible)
    {
        if (_isClosing)
        {
            return;
        }

        var window = _windows[index];
        if (visible)
        {
            if (window is null)
            {
                window = CreateWindow(index);
                _windows[index] = window;
            }
            window.Show();
            window.WindowState = FormWindowState.Normal;
            window.Activate();
        }
        else
        {
            window?.Hide();
        }
    }

    private Form CreateWindow(int index)
    {
        var plot = _plots[index];
        var window = new Form
        {
            Text = plot.Title,
            StartPosition = FormStartPosition.CenterScreen,
            MinimumSize = new Size(500, 340),
            Size = new Size(760, 500),
            BackColor = AppTheme.Background,
            ForeColor = AppTheme.Text,
            ShowInTaskbar = true,
            ShowIcon = false
        };
        plot.Dock = DockStyle.Fill;
        window.Controls.Add(plot);
        window.FormClosing += (_, e) =>
        {
            if (_isClosing)
            {
                return;
            }
            e.Cancel = true;
            window.Hide();
        };
        ThemeApplicator.Apply(window);
        return window;
    }

    public void CloseAll()
    {
        _isClosing = true;
        foreach (var window in _windows)
        {
            window?.Close();
            window?.Dispose();
        }
        Array.Clear(_windows);
    }

    public void Dispose() => CloseAll();
}

public sealed class PressureMonitorSettingsForm : Form
{
    private readonly CheckedListBox _list = new();

    public PressureMonitorSettingsForm(string[] titles, bool[] states)
    {
        Text = "压力传感器波形配置";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(430, 360);

        var title = new Label
        {
            Text = "压力传感器波形窗口",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            Location = new Point(22, 18)
        };
        var hint = new Label
        {
            Text = "勾选后点击“应用”，窗口将作为独立窗口打开。",
            AutoSize = true,
            ForeColor = AppTheme.Muted,
            Location = new Point(24, 52)
        };
        _list.CheckOnClick = true;
        _list.Location = new Point(24, 82);
        _list.Size = new Size(380, 190);
        for (var index = 0; index < titles.Length; index++)
        {
            _list.Items.Add(titles[index], index < states.Length && states[index]);
        }

        var apply = new Button
        {
            Text = "应用",
            DialogResult = DialogResult.OK,
            Location = new Point(308, 296),
            Size = new Size(96, 38)
        };
        var cancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(206, 296),
            Size = new Size(92, 38)
        };
        StylePrimary(apply);
        StyleSecondary(cancel);
        Controls.AddRange([title, hint, _list, cancel, apply]);
        AcceptButton = apply;
        CancelButton = cancel;
        ThemeApplicator.Apply(this);
    }

    public bool[] SelectedStates
    {
        get
        {
            var states = new bool[_list.Items.Count];
            for (var index = 0; index < states.Length; index++)
            {
                states[index] = _list.GetItemChecked(index);
            }
            return states;
        }
    }

    private static void StylePrimary(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = AppTheme.Accent;
        button.ForeColor = Color.FromArgb(7, 27, 43);
        button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
    }

    private static void StyleSecondary(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = AppTheme.Border;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = AppTheme.SecondaryButton;
        button.ForeColor = AppTheme.Text;
        button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
    }
}
