using System.Globalization;
using CanHost.Models;

namespace CanHost.Modules;

public sealed class SendFramesModule : UserControl
{
    private static readonly Color BackgroundColor = Color.FromArgb(20, 30, 48);
    private static readonly Color FieldColor = Color.FromArgb(14, 23, 39);
    private static Color BorderColor => AppTheme.Border;
    private static readonly Color TextColor = Color.FromArgb(229, 237, 247);
    private static readonly Color MutedColor = Color.FromArgb(139, 155, 179);
    private static readonly Color AccentColor = Color.FromArgb(56, 189, 248);
    private static readonly Color GreenColor = Color.FromArgb(134, 239, 172);

    private readonly CheckBox _extendedCheck = new();
    private readonly CheckBox _remoteCheck = new();
    private readonly TextBox _idBox = new();
    private readonly TextBox _dataBox = new();
    private readonly Label _idHint = new();
    private readonly Label _dataHint = new();
    private readonly Label _sentLabel = new();
    private readonly Button _sendButton = new();
    private readonly Button _periodicButton = new();
    private readonly NumericUpDown _intervalInput = new();
    private readonly System.Windows.Forms.Timer _periodicTimer = new();
    private long _sentCount;
    private bool _connected;

    public SendFramesModule()
    {
        BackColor = BackgroundColor;
        Dock = DockStyle.Fill;
        Padding = new Padding(18, 14, 18, 14);
        BuildInterface();
        BindEvents();
        SetConnected(false);
    }

    public Func<CanFrame, string?>? SendAction { get; set; }
    public event Action<string?>? StatusChanged;

    public void SetConnected(bool connected)
    {
        _connected = connected;
        _sendButton.Enabled = connected;
        _periodicButton.Enabled = connected;
        if (!connected)
        {
            StopPeriodicSending();
        }
    }

    public void LoadFrame(CanFrame frame)
    {
        _extendedCheck.Checked = frame.Extended;
        _remoteCheck.Checked = frame.Remote;
        _idBox.Text = frame.IdText;
        _dataBox.Text = string.Join(" ", frame.Data.Select(value => value.ToString("X2")));
        UpdateEditor();
    }

    public void ResetCount()
    {
        _sentCount = 0;
        _sentLabel.Text = "已发送  0";
    }

    public void SendCurrent()
    {
        SendSingleFrame();
    }

    private void BuildInterface()
    {
        Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };

        var title = new Label
        {
            Text = "发送帧",
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(18, 14)
        };

        _sentLabel.Text = "已发送  0";
        _sentLabel.AutoSize = true;
        _sentLabel.ForeColor = GreenColor;
        _sentLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        _sentLabel.Location = new Point(88, 16);

        var typeLabel = FieldLabel("帧类型");
        typeLabel.Location = new Point(18, 45);
        StyleCheck(_extendedCheck);
        _extendedCheck.Text = "扩展帧（29 位 ID）";
        _extendedCheck.Location = new Point(18, 68);
        StyleCheck(_remoteCheck);
        _remoteCheck.Text = "远程帧";
        _remoteCheck.Location = new Point(178, 68);

        var idLabel = FieldLabel("CAN ID（十六进制）");
        idLabel.Location = new Point(310, 45);
        StyleTextBox(_idBox);
        _idBox.Text = "123";
        _idBox.CharacterCasing = CharacterCasing.Upper;
        _idBox.MaxLength = 8;
        _idBox.Bounds = new Rectangle(310, 68, 142, 24);
        _idHint.Text = "标准帧范围：000 - 7FF";
        StyleHint(_idHint);
        _idHint.Location = new Point(310, 99);

        var dataLabel = FieldLabel("数据（十六进制，最多 8 字节）");
        dataLabel.Location = new Point(482, 45);
        StyleTextBox(_dataBox);
        _dataBox.Text = "00 11 22 33 44 55 66 77";
        _dataBox.CharacterCasing = CharacterCasing.Upper;
        _dataBox.MaxLength = 40;
        _dataBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _dataBox.Bounds = new Rectangle(482, 68, 520, 24);
        _dataHint.Text = "8 字节";
        StyleHint(_dataHint);
        _dataHint.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _dataHint.TextAlign = ContentAlignment.MiddleRight;
        _dataHint.Bounds = new Rectangle(Width - 94, 99, 72, 18);

        var intervalLabel = FieldLabel("周期");
        intervalLabel.Location = new Point(18, 128);
        _intervalInput.Bounds = new Rectangle(18, 149, 96, 31);
        _intervalInput.Minimum = 10;
        _intervalInput.Maximum = 60_000;
        _intervalInput.Value = 100;
        _intervalInput.ThousandsSeparator = true;
        StyleNumeric(_intervalInput);
        var unit = new Label
        {
            Text = "ms",
            ForeColor = MutedColor,
            AutoSize = true,
            Location = new Point(120, 154)
        };

        StylePrimaryButton(_sendButton);
        _sendButton.Text = "发送一次  Ctrl+Enter";
        _sendButton.Bounds = new Rectangle(310, 140, 190, 38);

        StyleSecondaryButton(_periodicButton);
        _periodicButton.Text = "开始周期发送";
        _periodicButton.Bounds = new Rectangle(510, 140, 190, 38);

        var hint = new Label
        {
            Text = "双击接收帧可载入当前发送区",
            ForeColor = MutedColor,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            Location = new Point(720, 154)
        };

        Controls.AddRange(
        [
            title,
            _sentLabel,
            typeLabel,
            _extendedCheck,
            _remoteCheck,
            idLabel,
            _idBox,
            _idHint,
            dataLabel,
            _dataBox,
            _dataHint,
            intervalLabel,
            _intervalInput,
            unit,
            _sendButton,
            _periodicButton,
            hint
        ]);
        Resize += (_, _) =>
        {
            _dataBox.Width = Math.Max(250, ClientSize.Width - 500);
            _dataHint.Left = ClientSize.Width - 94;
            hint.Left = Math.Max(720, ClientSize.Width - hint.Width - 20);
        };
    }

    private void BindEvents()
    {
        _extendedCheck.CheckedChanged += (_, _) => UpdateEditor();
        _remoteCheck.CheckedChanged += (_, _) => UpdateEditor();
        _dataBox.TextChanged += (_, _) => UpdateDataHint();
        _sendButton.Click += (_, _) => SendSingleFrame();
        _periodicButton.Click += (_, _) => TogglePeriodicSending();
        _periodicTimer.Tick += (_, _) => SendPeriodicFrame();
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                SendSingleFrame();
                e.SuppressKeyPress = true;
            }
        };
    }

    private void SendSingleFrame()
    {
        if (!_connected)
        {
            ShowWarning("请先连接 CAN 适配器。");
            return;
        }

        try
        {
            var frame = BuildFrameFromEditor();
            var error = SendAction?.Invoke(frame);
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException(error);
            }
            IncrementSent();
            StatusChanged?.Invoke(null);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(ex.Message);
            ShowWarning(ex.Message);
        }
    }

    private void TogglePeriodicSending()
    {
        if (_periodicTimer.Enabled)
        {
            StopPeriodicSending();
            return;
        }

        if (!_connected)
        {
            ShowWarning("请先连接 CAN 适配器。");
            return;
        }

        try
        {
            _ = BuildFrameFromEditor();
            _periodicTimer.Interval = (int)_intervalInput.Value;
            _periodicTimer.Start();
            _periodicButton.Text = "停止周期发送";
            _periodicButton.BackColor = Color.FromArgb(76, 41, 47);
            _periodicButton.ForeColor = Color.FromArgb(254, 202, 202);
            SendPeriodicFrame();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(ex.Message);
            ShowWarning(ex.Message);
        }
    }

    private void SendPeriodicFrame()
    {
        if (!_connected)
        {
            StopPeriodicSending();
            return;
        }

        try
        {
            var frame = BuildFrameFromEditor();
            var error = SendAction?.Invoke(frame);
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException(error);
            }
            IncrementSent();
        }
        catch (Exception ex)
        {
            StopPeriodicSending();
            StatusChanged?.Invoke($"周期发送已停止：{ex.Message}");
            ShowWarning($"周期发送已停止：\n\n{ex.Message}");
        }
    }

    private void StopPeriodicSending()
    {
        _periodicTimer.Stop();
        _periodicButton.Text = "开始周期发送";
        StyleSecondaryButton(_periodicButton);
    }

    private void IncrementSent()
    {
        _sentCount++;
        _sentLabel.Text = $"已发送  {_sentCount:N0}";
    }

    private CanFrame BuildFrameFromEditor()
    {
        var idText = _idBox.Text.Trim();
        if (idText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            idText = idText[2..];
        }

        if (!uint.TryParse(idText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
        {
            throw new InvalidOperationException("CAN ID 必须是十六进制数字。");
        }

        var extended = _extendedCheck.Checked;
        var maxId = extended ? 0x1FFFFFFFu : 0x7FFu;
        if (id > maxId)
        {
            throw new InvalidOperationException(
                extended
                    ? "扩展帧 ID 不能超过 1FFFFFFF。"
                    : "标准帧 ID 不能超过 7FF。");
        }

        var remote = _remoteCheck.Checked;
        var data = remote ? Array.Empty<byte>() : ParseHexBytes(_dataBox.Text);
        if (data.Length > 8)
        {
            throw new InvalidOperationException("CAN 2.0 数据帧最多只能发送 8 字节。");
        }

        return new CanFrame(
            id,
            extended,
            remote,
            remote ? (byte)Math.Min(8, _dataBox.Text.Trim().Length / 2) : (byte)data.Length,
            data,
            DateTime.Now,
            FrameDirection.Tx);
    }

    private void UpdateEditor()
    {
        if (_extendedCheck.Checked)
        {
            _idHint.Text = "扩展帧范围：00000000 - 1FFFFFFF";
            _idBox.MaxLength = 8;
            if (_idBox.Text.Length is > 0 and < 4)
            {
                _idBox.Text = "00000123";
            }
        }
        else
        {
            _idHint.Text = "标准帧范围：000 - 7FF";
            _idBox.MaxLength = 3;
            if (uint.TryParse(_idBox.Text, NumberStyles.HexNumber, null, out var id))
            {
                _idBox.Text = Math.Min(id, 0x7FFu).ToString("X3");
            }
        }

        _dataBox.Enabled = !_remoteCheck.Checked;
        UpdateDataHint();
    }

    private void UpdateDataHint()
    {
        if (_remoteCheck.Checked)
        {
            _dataHint.Text = "远程帧无数据";
            return;
        }

        try
        {
            var data = ParseHexBytes(_dataBox.Text);
            _dataHint.Text = data.Length > 8 ? "超过 8 字节" : $"{data.Length} 字节";
        }
        catch
        {
            _dataHint.Text = "格式有误";
        }
    }

    private static byte[] ParseHexBytes(string text)
    {
        var normalized = text
            .Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace(",", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return [];
        }
        if (normalized.Length % 2 != 0)
        {
            throw new InvalidOperationException("数据必须是完整的十六进制字节，例如：01 02 A5 FF。");
        }

        try
        {
            return Convert.FromHexString(normalized);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("数据中包含非十六进制字符。");
        }
    }

    private void ShowWarning(string message)
    {
        MessageBox.Show(
            FindForm(),
            message,
            "发送帧",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = MutedColor,
        Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold)
    };

    private static void StyleHint(Label label)
    {
        label.ForeColor = Color.FromArgb(111, 133, 162);
        label.Font = new Font("Microsoft YaHei UI", 8.2F);
        label.AutoSize = true;
    }

    private static void StyleTextBox(TextBox textBox)
    {
        textBox.BackColor = FieldColor;
        textBox.ForeColor = TextColor;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Font = new Font("Cascadia Mono", 9.5F);
    }

    private static void StyleCheck(CheckBox checkBox)
    {
        checkBox.AutoSize = true;
        checkBox.ForeColor = TextColor;
        checkBox.FlatStyle = FlatStyle.Flat;
        checkBox.Font = new Font("Microsoft YaHei UI", 9F);
    }

    private static void StyleNumeric(NumericUpDown numeric)
    {
        numeric.BackColor = FieldColor;
        numeric.ForeColor = TextColor;
        numeric.BorderStyle = BorderStyle.FixedSingle;
        numeric.Font = new Font("Cascadia Mono", 9.5F);
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
