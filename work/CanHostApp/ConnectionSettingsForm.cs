using System.Diagnostics;
using CanHost.Adapters;
using CanHost.Models;

namespace CanHost;

public sealed class ConnectionSettingsForm : Form
{
    private static readonly Color BackgroundColor = Color.FromArgb(12, 18, 32);
    private static readonly Color CardColor = Color.FromArgb(20, 30, 48);
    private static readonly Color FieldColor = Color.FromArgb(14, 23, 39);
    private static readonly Color BorderColor = Color.FromArgb(48, 64, 88);
    private static readonly Color TextColor = Color.FromArgb(229, 237, 247);
    private static readonly Color MutedColor = Color.FromArgb(139, 155, 179);
    private static readonly Color AccentColor = Color.FromArgb(56, 189, 248);
    private static readonly Color SuccessColor = Color.FromArgb(34, 197, 94);
    private static readonly Color DangerColor = Color.FromArgb(248, 113, 113);

    private readonly Func<ConnectionOptions, string?> _connectAction;
    private readonly Action _disconnectAction;
    private readonly string _initialSummary;

    private readonly ComboBox _adapterCombo = new();
    private readonly ComboBox _portCombo = new();
    private readonly ComboBox _channelCombo = new();
    private readonly ComboBox _bitrateCombo = new();
    private readonly ComboBox _serialBitrateCombo = new();
    private readonly Button _refreshPortsButton = new();
    private readonly Button _connectButton = new();
    private readonly Button _disconnectButton = new();
    private readonly Label _portLabel = FieldLabel("串口端口");
    private readonly Label _serialLabel = FieldLabel("串口速率");
    private readonly Label _channelLabel = FieldLabel("CAN 通道");
    private readonly Label _statusLabel = new();
    private readonly Label _errorLabel = new();

    private ConnectionOptions _options;
    private bool _connected;

    public ConnectionSettingsForm(
        ConnectionOptions initialOptions,
        bool connected,
        string connectedSummary,
        Func<ConnectionOptions, string?> connectAction,
        Action disconnectAction)
    {
        _options = initialOptions;
        _connected = connected;
        _initialSummary = connectedSummary;
        _connectAction = connectAction;
        _disconnectAction = disconnectAction;

        Text = "通信配置 · " + AppInfo.DisplayName;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(630, 520);
        BackColor = BackgroundColor;
        ForeColor = TextColor;
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildInterface();
        LoadOptions();
        BindEvents();
        UpdateVisibleFields();
        UpdateConnectionStatus();
        ThemeApplicator.Apply(this);
    }

    public ConnectionOptions Options => _options;

    private void BuildInterface()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = Color.FromArgb(16, 25, 41),
            Padding = new Padding(28, 14, 28, 10)
        };
        var title = new Label
        {
            Text = "通信配置",
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(28, 13)
        };
        var subtitle = new Label
        {
            Text = "选择适配器、通道、波特率并建立 CAN 总线连接",
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = MutedColor,
            AutoSize = true,
            Location = new Point(30, 48)
        };
        header.Controls.AddRange([title, subtitle]);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 18, 28, 18)
        };

        var adapterLabel = FieldLabel("适配器类型");
        adapterLabel.Location = new Point(28, 18);
        StyleCombo(_adapterCombo);
        _adapterCombo.Items.AddRange(
        [
            "SLCAN（USB 串口）",
            "PEAK PCAN（USB）",
            "创芯科技 ControlCAN"
        ]);
        _adapterCombo.Location = new Point(28, 40);
        _adapterCombo.Width = 574;

        _portLabel.Location = new Point(28, 94);
        StyleCombo(_portCombo);
        _portCombo.Location = new Point(28, 116);
        _portCombo.Width = 480;
        StyleSecondaryButton(_refreshPortsButton);
        _refreshPortsButton.Text = "刷新";
        _refreshPortsButton.Location = new Point(518, 115);
        _refreshPortsButton.Size = new Size(84, 31);

        _serialLabel.Location = new Point(28, 167);
        StyleCombo(_serialBitrateCombo);
        _serialBitrateCombo.Items.AddRange(
        [
            "115200",
            "230400",
            "460800",
            "921600",
            "1000000",
            "2000000"
        ]);
        _serialBitrateCombo.Location = new Point(28, 189);
        _serialBitrateCombo.Width = 270;

        _channelLabel.Location = new Point(28, 94);
        StyleCombo(_channelCombo);
        _channelCombo.Location = new Point(28, 116);
        _channelCombo.Width = 270;

        var bitrateLabel = FieldLabel("CAN 波特率");
        bitrateLabel.Location = new Point(326, 94);
        StyleCombo(_bitrateCombo);
        _bitrateCombo.Items.AddRange(
        [
            "125 kbit/s",
            "250 kbit/s",
            "500 kbit/s",
            "1 Mbit/s"
        ]);
        _bitrateCombo.Location = new Point(326, 116);
        _bitrateCombo.Width = 276;

        var statusCard = CardPanel();
        statusCard.Location = new Point(28, 225);
        statusCard.Size = new Size(574, 92);
        _statusLabel.AutoSize = false;
        _statusLabel.Location = new Point(16, 13);
        _statusLabel.Size = new Size(542, 26);
        _statusLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        _errorLabel.AutoSize = false;
        _errorLabel.Location = new Point(16, 43);
        _errorLabel.Size = new Size(542, 38);
        _errorLabel.ForeColor = DangerColor;
        _errorLabel.Font = new Font("Microsoft YaHei UI", 8.6F);
        statusCard.Controls.AddRange([_statusLabel, _errorLabel]);

        var hint = new Label
        {
            Text = "连接成功后此窗口会自动关闭，通信状态会显示在主窗口底部状态栏。",
            ForeColor = MutedColor,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            Location = new Point(28, 328)
        };

        StyleSecondaryButton(_disconnectButton);
        _disconnectButton.Text = "断开连接";
        _disconnectButton.Location = new Point(28, 365);
        _disconnectButton.Size = new Size(140, 42);

        var cancelButton = new Button();
        StyleSecondaryButton(cancelButton);
        cancelButton.Text = "关闭";
        cancelButton.Location = new Point(370, 365);
        cancelButton.Size = new Size(100, 42);
        cancelButton.DialogResult = DialogResult.Cancel;

        StylePrimaryButton(_connectButton);
        _connectButton.Text = "连接 CAN 总线";
        _connectButton.Location = new Point(480, 365);
        _connectButton.Size = new Size(122, 42);

        body.Controls.AddRange(
        [
            adapterLabel,
            _adapterCombo,
            _portLabel,
            _portCombo,
            _refreshPortsButton,
            _serialLabel,
            _serialBitrateCombo,
            _channelLabel,
            _channelCombo,
            bitrateLabel,
            _bitrateCombo,
            statusCard,
            hint,
            _disconnectButton,
            cancelButton,
            _connectButton
        ]);

        CancelButton = cancelButton;
        Controls.Add(body);
        Controls.Add(header);
    }

    private void LoadOptions()
    {
        _adapterCombo.SelectedIndex = Math.Clamp(_options.AdapterIndex, 0, 2);
        _bitrateCombo.SelectedIndex = _options.CanBitrate switch
        {
            125_000 => 0,
            250_000 => 1,
            500_000 => 2,
            1_000_000 => 3,
            _ => 3
        };

        RefreshPorts();
        if (!string.IsNullOrWhiteSpace(_options.Port) && _portCombo.Items.Contains(_options.Port))
        {
            _portCombo.SelectedItem = _options.Port;
        }
        else if (_portCombo.Items.Count > 0 && _portCombo.SelectedIndex < 0)
        {
            _portCombo.SelectedIndex = 0;
        }

        var serialRateText = _options.SerialBitrate.ToString();
        _serialBitrateCombo.SelectedItem = serialRateText;
        if (_serialBitrateCombo.SelectedIndex < 0)
        {
            _serialBitrateCombo.SelectedIndex = 0;
        }

    }

    private void BindEvents()
    {
        _adapterCombo.SelectedIndexChanged += (_, _) => UpdateVisibleFields();
        _refreshPortsButton.Click += (_, _) => RefreshPorts();
        _connectButton.Click += (_, _) => Connect();
        _disconnectButton.Click += (_, _) => Disconnect();
        FormClosing += (_, _) => _options = ReadOptions();
    }

    private void UpdateVisibleFields()
    {
        var adapterIndex = Math.Max(0, _adapterCombo.SelectedIndex);
        var isSlcan = adapterIndex == 0;
        var isControlCan = adapterIndex == 2;

        _portLabel.Visible = isSlcan;
        _portCombo.Visible = isSlcan;
        _refreshPortsButton.Visible = isSlcan;
        _serialLabel.Visible = isSlcan;
        _serialBitrateCombo.Visible = isSlcan;
        _channelLabel.Visible = !isSlcan;
        _channelCombo.Visible = !isSlcan;

        if (isControlCan)
        {
            _channelLabel.Text = "ControlCAN 通道";
            SetChannelOptions("CAN 1", "CAN 2");
        }
        else if (adapterIndex == 1)
        {
            _channelLabel.Text = "PCAN 通道";
            SetChannelOptions("USB 1", "USB 2", "USB 3", "USB 4");
        }

    }

    private void SetChannelOptions(params string[] values)
    {
        if (_channelCombo.Items.Count != values.Length ||
            !_channelCombo.Items.Cast<object>().Select(item => item.ToString())
                .SequenceEqual(values, StringComparer.Ordinal))
        {
            _channelCombo.Items.Clear();
            _channelCombo.Items.AddRange(values.Cast<object>().ToArray());
        }

        var targetIndex = Math.Clamp(_options.Channel - 1, 0, values.Length - 1);
        _channelCombo.SelectedIndex = targetIndex;
    }

    private void RefreshPorts()
    {
        var current = _portCombo.Text;
        var ports = SlcanAdapter.GetPortNames();
        _portCombo.Items.Clear();
        _portCombo.Items.AddRange(ports.Cast<object>().ToArray());
        if (ports.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            _portCombo.SelectedItem = current;
        }
        else if (ports.Count > 0)
        {
            _portCombo.SelectedIndex = 0;
        }
    }

    private void Connect()
    {
        _errorLabel.Text = string.Empty;
        _statusLabel.Text = "正在连接…";
        _statusLabel.ForeColor = AccentColor;
        _connectButton.Enabled = false;
        Application.DoEvents();

        try
        {
            var options = ReadOptions();
            _options = options;
            var error = _connectAction(options);
            if (!string.IsNullOrWhiteSpace(error))
            {
                _statusLabel.Text = "连接失败";
                _statusLabel.ForeColor = DangerColor;
                _errorLabel.Text = error;
                return;
            }

            DialogResult = DialogResult.OK;
            _connected = true;
            Close();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "连接失败";
            _statusLabel.ForeColor = DangerColor;
            _errorLabel.Text = ex.Message;
        }
        finally
        {
            _connectButton.Enabled = true;
        }
    }

    private void Disconnect()
    {
        try
        {
            _disconnectAction();
            _connected = false;
            _errorLabel.Text = string.Empty;
            UpdateConnectionStatus();
        }
        catch (Exception ex)
        {
            _errorLabel.Text = ex.Message;
        }
    }

    private void UpdateConnectionStatus()
    {
        _statusLabel.Text = _connected
            ? $"已连接  ·  {_initialSummary}"
            : "当前未连接 CAN 总线";
        _statusLabel.ForeColor = _connected ? SuccessColor : MutedColor;
        _disconnectButton.Enabled = _connected;
        _connectButton.Text = _connected ? "重新连接" : "连接 CAN 总线";
    }

    private ConnectionOptions ReadOptions()
    {
        var bitrate = _bitrateCombo.SelectedIndex switch
        {
            0 => 125_000,
            1 => 250_000,
            2 => 500_000,
            3 => 1_000_000,
            _ => 1_000_000
        };
        var serialBitrate = int.TryParse(_serialBitrateCombo.Text, out var rate)
            ? rate
            : 115_200;

        return new ConnectionOptions(
            Math.Max(0, _adapterCombo.SelectedIndex),
            bitrate,
            _channelCombo.SelectedIndex + 1,
            _portCombo.Text,
            serialBitrate,
            VciAdapter.FindControlCanDll());
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = MutedColor,
        Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold)
    };

    private static Panel CardPanel()
    {
        var card = new Panel { BackColor = CardColor };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        return card;
    }

    private static void StyleCombo(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.BackColor = FieldColor;
        combo.ForeColor = TextColor;
        combo.FlatStyle = FlatStyle.Flat;
        combo.Font = new Font("Microsoft YaHei UI", 9F);
    }

    private static void StylePrimaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = AccentColor;
        button.ForeColor = Color.FromArgb(7, 27, 43);
        button.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private static void StyleSecondaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = BorderColor;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = Color.FromArgb(25, 38, 59);
        button.ForeColor = TextColor;
        button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }
}
