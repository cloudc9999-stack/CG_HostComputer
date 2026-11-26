using CanHost.Adapters;
using CanHost.Models;

namespace CanHost;

public sealed class ConnectionDockPanel : UserControl
{
    private readonly ComboBox _adapter = new();
    private readonly ComboBox _channel = new();
    private readonly ComboBox _port = new();
    private readonly ComboBox _bitrate = new();
    private readonly Label _portLabel = FieldLabel("串口");
    private readonly Button _connect = new();
    private readonly Button _disconnect = new();
    private readonly Button _refresh = new();
    private readonly Label _state = new();
    private readonly Label _sn = new();

    public ConnectionDockPanel()
    {
        Width = 320;
        Dock = DockStyle.Left;
        BackColor = AppTheme.Card;
        Padding = new Padding(14, 12, 14, 12);
        BuildInterface();
    }

    public event Func<ConnectionOptions, string?>? ConnectRequested;
    public event Action? DisconnectRequested;

    public ConnectionOptions CurrentOptions => ReadOptions();

    public void SetConnectionState(bool connected, string summary)
    {
        _state.Text = connected ? "●  已连接  " + summary : "●  未连接";
        _state.ForeColor = connected ? AppTheme.Success : AppTheme.Danger;
        _connect.Enabled = !connected;
        _disconnect.Enabled = connected;
        _adapter.Enabled = !connected;
        _channel.Enabled = !connected;
        _port.Enabled = !connected;
        _bitrate.Enabled = !connected;
        _refresh.Enabled = !connected;
    }

    public void SetDeviceSn(string? value)
    {
        _sn.Text = string.IsNullOrWhiteSpace(value) ? "设备 SN：--" : value;
        _sn.ForeColor = _sn.Text.EndsWith("--", StringComparison.Ordinal)
            ? AppTheme.Muted
            : AppTheme.Success;
    }

    private void BuildInterface()
    {
        var title = new Label
        {
            Text = "通信配置 · 板卡连接",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            ForeColor = AppTheme.Text
        };
        var adapterLabel = FieldLabel("适配器");
        StyleCombo(_adapter);
        _adapter.Items.AddRange(
        [
            "SLCAN",
            "PEAK PCAN",
            "创芯科技 ControlCAN"
        ]);
        _adapter.SelectedIndex = 2;
        _adapter.SelectedIndexChanged += (_, _) => UpdateVisibleFields();

        var channelLabel = FieldLabel("通道");
        StyleCombo(_channel);
        _channel.Items.AddRange(["CAN 1", "CAN 2"]);
        _channel.SelectedIndex = 0;

        StyleCombo(_port);
        StyleSecondary(_refresh);
        _refresh.Text = "刷新";
        _refresh.Size = new Size(54, 26);
        _refresh.Click += (_, _) => RefreshPorts();

        var bitrateLabel = FieldLabel("CAN 波特率");
        StyleCombo(_bitrate);
        _bitrate.Items.AddRange(
        [
            "125 kbit/s",
            "250 kbit/s",
            "500 kbit/s",
            "1 Mbit/s"
        ]);
        _bitrate.SelectedIndex = 3;

        StylePrimary(_connect);
        _connect.Text = "连接设备";
        _connect.Size = new Size(108, 32);
        _connect.Click += (_, _) => Connect();
        StyleSecondary(_disconnect);
        _disconnect.Text = "断开设备";
        _disconnect.Size = new Size(108, 32);
        _disconnect.Enabled = false;
        _disconnect.Click += (_, _) => DisconnectRequested?.Invoke();

        _state.Text = "●  未连接";
        _state.ForeColor = AppTheme.Danger;
        _state.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        _state.AutoEllipsis = true;
        _state.Height = 34;

        _sn.Text = "设备 SN：--";
        _sn.ForeColor = AppTheme.Muted;
        _sn.Font = new Font("Cascadia Mono", 9F, FontStyle.Bold);
        _sn.AutoEllipsis = true;
        _sn.Height = 25;

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 4, 0, 0)
        };
        void AddRow(Control control, int width = 288)
        {
            control.Width = width;
            control.Margin = new Padding(0, 0, 0, 8);
            stack.Controls.Add(control);
        }

        AddRow(adapterLabel);
        AddRow(_adapter);
        AddRow(channelLabel);
        AddRow(_channel);
        AddRow(_portLabel);
        var portRow = new Panel { Height = 32 };
        _port.Bounds = new Rectangle(0, 0, 232, 28);
        _refresh.Location = new Point(240, 1);
        portRow.Controls.AddRange([_port, _refresh]);
        AddRow(portRow);
        AddRow(bitrateLabel);
        AddRow(_bitrate);
        var buttons = new FlowLayoutPanel
        {
            Height = 36,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        buttons.Controls.AddRange([_connect, _disconnect]);
        AddRow(buttons);
        AddRow(_state);
        AddRow(_sn);

        Controls.Add(stack);
        Controls.Add(title);
        UpdateVisibleFields();
        RefreshPorts();
        ThemeApplicator.Apply(this);
    }

    private void Connect()
    {
        var error = ConnectRequested?.Invoke(ReadOptions());
        if (!string.IsNullOrWhiteSpace(error))
        {
            _state.Text = "●  " + error;
            _state.ForeColor = AppTheme.Danger;
        }
    }

    private void UpdateVisibleFields()
    {
        var slcan = _adapter.SelectedIndex == 0;
        _channel.Visible = !slcan;
        _portLabel.Visible = slcan;
        _port.Visible = slcan;
        _refresh.Visible = slcan;
        _channel.Items.Clear();
        if (_adapter.SelectedIndex == 1)
        {
            _channel.Items.AddRange(["USB 1", "USB 2", "USB 3", "USB 4"]);
        }
        else
        {
            _channel.Items.AddRange(["CAN 1", "CAN 2"]);
        }
        _channel.SelectedIndex = 0;
    }

    private void RefreshPorts()
    {
        _port.Items.Clear();
        _port.Items.AddRange(SlcanAdapter.GetPortNames().Cast<object>().ToArray());
        if (_port.Items.Count > 0)
        {
            _port.SelectedIndex = 0;
        }
    }

    private ConnectionOptions ReadOptions()
    {
        var bitrate = _bitrate.SelectedIndex switch
        {
            0 => 125_000,
            1 => 250_000,
            2 => 500_000,
            _ => 1_000_000
        };
        return new ConnectionOptions(
            Math.Max(0, _adapter.SelectedIndex),
            bitrate,
            Math.Max(1, _channel.SelectedIndex + 1),
            _port.Text,
            115_200,
            VciAdapter.FindControlCanDll());
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Height = 20,
        ForeColor = AppTheme.Muted,
        Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
    };

    private static void StyleCombo(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Height = 28;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = AppTheme.Field;
        combo.ForeColor = AppTheme.Text;
        combo.Font = new Font("Microsoft YaHei UI", 9F);
    }

    private static void StylePrimary(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = AppTheme.Accent;
        button.ForeColor = Color.FromArgb(7, 27, 43);
        button.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
    }

    private static void StyleSecondary(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = AppTheme.Border;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = AppTheme.SecondaryButton;
        button.ForeColor = AppTheme.Text;
        button.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
    }
}
