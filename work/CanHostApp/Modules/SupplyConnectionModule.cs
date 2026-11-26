using CanHost.Models;

namespace CanHost.Modules;

public sealed class SupplyConnectionModule : UserControl
{
    private const uint HostRequestId = 0x01;
    private const uint DeviceResponseId = 0x05;
    private const uint ExpectedSnDecimal = 2_026_010_001;
    private static readonly byte[] ConnectRequest = [0x01, 0x01];
    private static readonly byte[] KeepAliveRequest = [0x01, 0x03];
    private static readonly byte[] KeepAliveResponse = [0x01, 0x03];
    private static readonly byte[] DisconnectRequest = [0x01, 0x04];

    private static readonly Color BackgroundColor = Color.FromArgb(20, 30, 48);
    private static readonly Color TextColor = Color.FromArgb(229, 237, 247);
    private static readonly Color AccentColor = Color.FromArgb(56, 189, 248);

    private readonly Label _connectedSnLabel = new();
    private readonly Button _connectButton = new();
    private readonly Button _disconnectButton = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };

    private ConnectionState _state;
    private bool _canAvailable;
    private bool _awaitingHeartbeatAck;
    private DateTime _stateEnteredAt;
    private DateTime _lastHeartbeatSentAt;
    private DateTime _nextRequestAt;
    private int _requestCount;
    private int _heartbeatCount;
    private int _ackCount;

    public SupplyConnectionModule()
    {
        BackColor = BackgroundColor;
        Dock = DockStyle.Fill;
        Padding = new Padding(18, 14, 18, 14);
        BuildInterface();
        _timer.Tick += (_, _) => OnTimerTick();
        ResetConnection("CAN 适配器尚未连接");
    }

    public Func<CanFrame, string?>? SendAction { get; set; }
    public event Action<string?>? StatusChanged;
    public event Action<bool>? LinkStateChanged;

    public bool IsLinkConnected => _state == ConnectionState.Connected;
    public string ConnectedSnDisplayText => _connectedSnLabel.Text;

    public void SetCanAvailable(bool available)
    {
        _canAvailable = available;
        _connectButton.Enabled = available && _state == ConnectionState.Disconnected;
        if (!available)
        {
            ResetConnection("CAN 适配器已断开");
        }
    }

    public void StartConnection()
    {
        if (!_canAvailable)
        {
            ShowWarning("请先打开“选项 → 通信配置”连接 CAN 适配器。");
            return;
        }

        _state = ConnectionState.WaitingForSn;
        _stateEnteredAt = DateTime.Now;
        _awaitingHeartbeatAck = false;
        _requestCount = 0;
        _heartbeatCount = 0;
        _ackCount = 0;
        _connectedSnLabel.Text = "设备 SN：--";
        SendConnectionRequest();
        _timer.Start();
    }

    public void DisconnectLink()
    {
        if (_state == ConnectionState.Disconnected)
        {
            return;
        }

        var frame = new CanFrame(
            HostRequestId,
            false,
            false,
            2,
            DisconnectRequest,
            DateTime.Now,
            FrameDirection.Tx);
        var message = "发送断开请求：01  2  01 04（无需应答）";
        if (SendAction is not null)
        {
            try
            {
                var error = SendAction(frame);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    message = "断开请求发送失败，本地已断开：" + error;
                }
            }
            catch (Exception ex)
            {
                message = "断开请求发送失败，本地已断开：" + ex.Message;
            }
        }

        ResetConnection(message);
    }

    public void ProcessFrame(CanFrame frame)
    {
        if (frame.Direction != FrameDirection.Rx || frame.Remote || frame.Data.Length < 2)
        {
            return;
        }

        if (frame.Id == DeviceResponseId && IsKeepAliveResponse(frame.Data))
        {
            if (_state == ConnectionState.Connected)
            {
                _awaitingHeartbeatAck = false;
                _ackCount++;
                StatusChanged?.Invoke(null);
            }
            return;
        }

        if (frame.Id != DeviceResponseId || _state != ConnectionState.WaitingForSn)
        {
            return;
        }

        if (frame.Data.Length < 8 ||
            frame.Data[0] != 0x01 ||
            frame.Data[1] != 0x02 ||
            frame.Data[2] != 0x00 ||
            frame.Data[3] != 0x00)
        {
            return;
        }

        var snHex = Convert.ToHexString(frame.Data, 4, 4);
        var snDecimal = ((uint)frame.Data[4] << 24) |
                        ((uint)frame.Data[5] << 16) |
                        ((uint)frame.Data[6] << 8) |
                        frame.Data[7];
        _connectedSnLabel.Text = $"设备 SN：0x{snHex}";

        if (snDecimal != ExpectedSnDecimal)
        {
            ResetConnection($"SN 不匹配：收到 {snDecimal:N0}");
            return;
        }

        _state = ConnectionState.Connected;
        _awaitingHeartbeatAck = false;
        _lastHeartbeatSentAt = DateTime.MinValue;
        _connectButton.Enabled = false;
        _disconnectButton.Enabled = true;
        SendHeartbeat();
        LinkStateChanged?.Invoke(true);
    }

    private void SendConnectionRequest()
    {
        if (_state != ConnectionState.WaitingForSn)
        {
            return;
        }

        var frame = new CanFrame(
            HostRequestId,
            false,
            false,
            2,
            ConnectRequest,
            DateTime.Now,
            FrameDirection.Tx);
        if (SendAction is null)
        {
            ResetConnection("发送接口未初始化");
            return;
        }
        var error = SendAction(frame);
        if (!string.IsNullOrWhiteSpace(error))
        {
            ResetConnection("连接请求发送失败：" + error);
            return;
        }

        _requestCount++;
        _nextRequestAt = DateTime.Now.AddSeconds(1);
    }

    private void SendHeartbeat()
    {
        if (_state != ConnectionState.Connected)
        {
            return;
        }

        var frame = new CanFrame(
            HostRequestId,
            false,
            false,
            2,
            KeepAliveRequest,
            DateTime.Now,
            FrameDirection.Tx);
        if (SendAction is null)
        {
            ResetConnection("发送接口未初始化");
            return;
        }
        var error = SendAction(frame);
        if (!string.IsNullOrWhiteSpace(error))
        {
            ResetConnection("连接应答发送失败：" + error);
            return;
        }

        _heartbeatCount++;
        _awaitingHeartbeatAck = true;
        _lastHeartbeatSentAt = DateTime.Now;
    }

    private void OnTimerTick()
    {
        var now = DateTime.Now;
        if (_state == ConnectionState.WaitingForSn)
        {
            if (now - _stateEnteredAt > TimeSpan.FromSeconds(5))
            {
                ResetConnection("等待 SN 超时，设备无响应");
                return;
            }
            if (now >= _nextRequestAt)
            {
                SendConnectionRequest();
            }
            return;
        }

        if (_state != ConnectionState.Connected)
        {
            return;
        }

        if (_awaitingHeartbeatAck)
        {
            if (now - _lastHeartbeatSentAt >= TimeSpan.FromSeconds(2))
            {
                ResetConnection("连接应答丢失，连接已中断");
            }
            return;
        }

        if (now - _lastHeartbeatSentAt >= TimeSpan.FromSeconds(2))
        {
            SendHeartbeat();
        }
    }

    private void ResetConnection(string message)
    {
        var wasConnected = _state == ConnectionState.Connected;
        _state = ConnectionState.Disconnected;
        _timer.Stop();
        _awaitingHeartbeatAck = false;
        _connectButton.Enabled = _canAvailable;
        _disconnectButton.Enabled = false;
        _connectedSnLabel.Text = "设备 SN：--";
        StatusChanged?.Invoke(message);
        if (wasConnected)
        {
            LinkStateChanged?.Invoke(false);
        }
    }

    private void BuildInterface()
    {
        Paint += (_, e) =>
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };

        StylePrimaryButton(_connectButton);
        _connectButton.Text = "连接设备";
        _connectButton.Location = new Point(18, 14);
        _connectButton.Size = new Size(130, 38);
        _connectButton.Click += (_, _) => StartConnection();

        StyleSecondaryButton(_disconnectButton);
        _disconnectButton.Text = "断开连接";
        _disconnectButton.Enabled = false;
        _disconnectButton.Location = new Point(158, 14);
        _disconnectButton.Size = new Size(110, 38);
        _disconnectButton.Click += (_, _) => DisconnectLink();

        _connectedSnLabel.AutoSize = false;
        _connectedSnLabel.TextAlign = ContentAlignment.MiddleLeft;
        _connectedSnLabel.ForeColor = AppTheme.Success;
        _connectedSnLabel.Font = new Font("Cascadia Mono", 10F, FontStyle.Bold);
        _connectedSnLabel.Location = new Point(276, 14);
        _connectedSnLabel.Size = new Size(Math.Max(120, Width - 294), 38);
        _connectedSnLabel.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Left |
            AnchorStyles.Right;
        _connectedSnLabel.Text = "设备 SN：--";

        Controls.AddRange(
        [
            _connectButton,
            _disconnectButton,
            _connectedSnLabel
        ]);
        Resize += (_, _) =>
        {
            _connectedSnLabel.Width = Math.Max(120, ClientSize.Width - 294);
        };
    }

    private static bool IsKeepAliveResponse(byte[] data)
    {
        return data.Length >= 2 &&
               data[0] == KeepAliveResponse[0] &&
               data[1] == KeepAliveResponse[1];
    }

    private void ShowWarning(string message)
    {
        MessageBox.Show(
            FindForm(),
            message,
            "储供连接",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
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
        button.FlatAppearance.BorderColor = AppTheme.Border;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = Color.FromArgb(25, 38, 59);
        button.ForeColor = TextColor;
        button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private enum ConnectionState
    {
        Disconnected,
        WaitingForSn,
        Connected
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
