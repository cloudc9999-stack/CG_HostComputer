using CanHost.Controls;
using CanHost.Models;
using System.Globalization;

namespace CanHost.Modules;

public sealed class SupplyControlModule : UserControl
{
    private static readonly Color BackgroundColor = Color.FromArgb(20, 30, 48);
    private static readonly Color BorderColor = Color.FromArgb(48, 64, 88);
    private static readonly Color TextColor = Color.FromArgb(229, 237, 247);
    private static readonly Color MutedColor = Color.FromArgb(139, 155, 179);

    private readonly SupplySchematicControl _schematic = new();
    private readonly SupplyConnectionModule _connection = new();
    private readonly TableLayoutPanel _layout = new();
    private readonly Panel _schematicHost = new();
    private readonly Panel _dutyEditorBar = new();
    private readonly Panel _connectionHost = new();
    private readonly Panel _connectionHeader = new();
    private readonly Button _controlConfigButton = new();
    private readonly Label _schematicTitle = new();
    private readonly NumericUpDown[] _dutyInputs = new NumericUpDown[4];
    private readonly Button[] _dutyButtons = new Button[4];
    private readonly NumericUpDown[] _pressureTargetInputs = new NumericUpDown[4];
    private readonly Button[] _pressureTargetButtons = new Button[4];
    private readonly Button[] _solenoidManualButtons = new Button[4];
    private readonly Button[] _solenoidAutoButtons = new Button[4];
    private readonly Button[] _ptcManualButtons = new Button[4];
    private readonly Button[] _ptcAutoButtons = new Button[4];
    private readonly Button[] _ptcOnButtons = new Button[4];
    private readonly Button[] _ptcOffButtons = new Button[4];
    private readonly double?[] _confirmedSolenoidDuties = new double?[4];
    private readonly uint?[] _confirmedPressureTargets = new uint?[4];
    private readonly uint?[] _uploadedSolenoidDutyValues = new uint?[4];
    private readonly bool[] _solenoidAutoModes = new bool[4];
    private readonly bool[] _ptcAutoModes = new bool[4];
    private readonly bool[] _ptcOnStates = new bool[4];
    private readonly Label _dutyLockHintLabel = new();
    private readonly System.Windows.Forms.Timer _selfLockTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _sensorTimeoutTimer = new() { Interval = 100 };
    private Func<CanFrame, string?>? _sendAction;
    private bool _canAvailable;
    private bool _supplyLinkConnected;
    private bool _requestedSelfLockOpen;
    private bool _selfLockOpen;
    private int _pendingSolenoidIndex;
    private double _pendingSolenoidDuty;
    private int _pendingPtcIndex;
    private bool _pendingPtcOn;
    private uint _pendingPressureTarget;
    private int _pendingModeNumber;
    private bool _pendingModeAutomatic;
    private PendingCommandType _pendingCommand;
    private DateTime _controlDeadline;
    private DateTime? _lastMainPressureAt;
    private DateTime? _lastBranchPressureAt;
    private DateTime? _lastTemperatureAt;

    public SupplyControlModule()
    {
        BackColor = BackgroundColor;
        Dock = DockStyle.Fill;
        BuildInterface();
        _schematic.SelfLockCommandRequested += SendSelfLockCommand;
        _schematic.BranchSelected += _schematic.FocusInlineEditor;
        _schematic.SolenoidModeRequested += SetSolenoidMode;
        _schematic.InlineSolenoidDutySetRequested += (branch, value) =>
        {
            _dutyInputs[branch - 1].Value = (decimal)value;
            SendSolenoidDutyCommand(branch, value);
        };
        _schematic.InlinePressureTargetSetRequested += (branch, value) =>
        {
            _pressureTargetInputs[branch - 1].Value = value;
            SendPressureTargetCommand(branch, value);
        };
        _schematic.PtcModeRequested += (index, automatic) => SetPtcMode(index, automatic);
        _schematic.PtcSwitchRequested += (index, on) => SetPtcSwitch(index, on);
        _connection.StatusChanged += message =>
        {
            StatusChanged?.Invoke(message);
            DeviceSnChanged?.Invoke(_connection.ConnectedSnDisplayText);
        };
        _connection.LinkStateChanged += connected =>
        {
            HandleSupplyLinkStateChanged(connected);
        };
        _selfLockTimer.Tick += (_, _) => CheckCommandAck();
        _sensorTimeoutTimer.Tick += (_, _) => CheckSensorTimeouts(DateTime.Now);
        _sensorTimeoutTimer.Start();
        _schematic.SetSelfLockValveState("未接入");
        _schematic.SetGasFlowEnabled(false);
        SetCanAvailable(false);
    }

    public event Action<string?>? StatusChanged;
    public event Action<string>? DeviceSnChanged;
    public event Action? PressurePlotConfigurationRequested;
    public string ConnectedSnText => _connection.ConnectedSnDisplayText;
    public bool IsSelfLockOpen => _selfLockOpen;
    public bool IsGasFlowEnabled => _schematic.GasFlowEnabled;
    public bool IsSolenoidControlPending =>
        _pendingCommand == PendingCommandType.SolenoidDuty;
    public double GetSolenoidDuty(int branch) =>
        branch is >= 1 and <= 4 ? (double)_dutyInputs[branch - 1].Value : 0;
    public string GetSolenoidRatioText(int branch) => _schematic.GetSolenoidRatioText(branch);
    public bool IsBranchFlowEnabled(int branch) => _schematic.IsBranchFlowEnabled(branch);
    public string GetMainPressureText() => _schematic.GetMainPressureText();
    public string GetPressureText(int sensor) => _schematic.GetPressureText(sensor);
    public string GetTemperatureText(int branch) => _schematic.GetTemperatureText(branch);

    public Func<CanFrame, string?>? SendAction
    {
        get => _sendAction;
        set
        {
            _sendAction = value;
            _connection.SendAction = value;
        }
    }

    public void SetCanAvailable(bool available)
    {
        _canAvailable = available;
        _connection.SetCanAvailable(available);
        if (!available)
        {
            AutoCloseForConnectionLoss("CAN 适配器已断开");
        }
        UpdateControlAvailability();
    }

    private void HandleSupplyLinkStateChanged(bool connected)
    {
        _supplyLinkConnected = connected;
        if (!connected)
        {
            AutoCloseForConnectionLoss("储供连接中断，自锁阀已自动关闭");
        }
        UpdateControlAvailability();
    }

    private void AutoCloseForConnectionLoss(string message)
    {
        _selfLockTimer.Stop();
        _pendingCommand = PendingCommandType.None;
        _selfLockOpen = false;
        _schematic.SetSelfLockValveState("关闭");
        _schematic.SetGasFlowEnabled(false);
        ResetAllSolenoidDuties();
        ClearSensorValues();
        UpdateControlAvailability();
        StatusChanged?.Invoke(message);
    }

    private void UpdateControlAvailability()
    {
        var processControlsAvailable = _canAvailable && _supplyLinkConnected;
        _schematic.CanControl = processControlsAvailable;
        _schematic.SetSelfLockControlEnabled(
            processControlsAvailable &&
            !IsAnyCommandPending);
        _schematic.SetSelfLockDisabledHint(
            !_canAvailable
                ? "CAN 未连接"
                : !_supplyLinkConnected
                    ? "储供未连接"
                    : "等待操作");
        UpdateParameterControlAvailability(processControlsAvailable);

        if (!_canAvailable)
        {
            SetDutyHint("CAN 适配器未连接", AppTheme.Danger);
        }
        else if (!_supplyLinkConnected)
        {
            SetDutyHint("储供设备未连接，暂不能操作", AppTheme.Danger);
        }
        else if (!_selfLockOpen)
        {
            SetDutyHint("请打开自锁阀后设置电磁阀占空比", AppTheme.Danger);
        }
        else if (IsAnyCommandPending)
        {
            SetDutyHint("正在等待从机应答…", Color.FromArgb(250, 204, 21));
        }
        else
        {
            SetDutyHint("参数控制已就绪，手动/自动模式按支路独立生效", AppTheme.Success);
        }
    }

    private bool IsAnyCommandPending => _pendingCommand != PendingCommandType.None;

    private void ResetAllSolenoidDuties()
    {
        for (var branch = 1; branch <= 4; branch++)
        {
            _confirmedSolenoidDuties[branch - 1] = 0;
            SetSolenoidDuty(branch, 0);
            RefreshSolenoidDisplay(branch - 1);
        }
    }

    private void SetDutyHint(string text, Color color)
    {
        _dutyLockHintLabel.Text = text;
        _dutyLockHintLabel.ForeColor = color;
    }

    public void ProcessFrame(CanFrame frame)
    {
        LogReceivedFrame(frame);
        if (SensorReportParser.TryParse(frame, out var sensorReport))
        {
            ApplySensorReport(sensorReport);
        }
        TryProcessStatusUpload(frame);

        if (IsAnyCommandPending &&
            frame.Direction == FrameDirection.Rx &&
            !frame.Remote &&
            frame.Id == 0x05 &&
            frame.Data.Length >= 2 &&
            frame.Data[0] == 0x02 &&
            frame.Data[1] is 0x01 or 0x02)
        {
            CompletePendingCommand(frame.Data[1] == 0x01);
        }

        _connection.ProcessFrame(frame);
    }

    private void LogReceivedFrame(CanFrame frame)
    {
        if (frame.Direction != FrameDirection.Rx || frame.Remote)
        {
            return;
        }

        var dataHex = Convert.ToHexString(frame.Data);
        if (frame.Id == 0x05 && frame.Data.Length >= 2)
        {
            if (frame.Data[0] == 0x01 && frame.Data[1] == 0x02)
            {
                _schematic.AppendResponseLog(
                    ResponseLogType.ConnectionSn,
                    $"SN连接应答  ID=05 DLC={frame.Dlc} Data={dataHex}");
                return;
            }
            if (frame.Data[0] == 0x01 && frame.Data[1] == 0x03)
            {
                _schematic.AppendResponseLog(
                    ResponseLogType.HeartbeatAck,
                    $"心跳应答  ID=05 DLC={frame.Dlc} Data={dataHex}");
                return;
            }
            if (frame.Data[0] == 0x02 && frame.Data[1] is 0x01 or 0x02)
            {
                var acknowledged = frame.Data[1] == 0x01;
                _schematic.AppendResponseLog(
                    acknowledged ? ResponseLogType.Ack : ResponseLogType.Nak,
                    $"{(acknowledged ? "ACK" : "NAK")} · {DescribePendingCommand()} " +
                    $"ID=05 DLC={frame.Dlc} Data={dataHex}");
                return;
            }
            if (frame.Data.Length >= 8 &&
                frame.Data[0] == 0x03 &&
                frame.Data[1] == 0x03)
            {
                _schematic.AppendResponseLog(
                    ResponseLogType.StatusUpload,
                    $"状态上报 · Num=0x{frame.Data[2]:X2} " +
                    $"Data={dataHex}");
                return;
            }
        }

        if (frame.Id is 0x06 or 0x07)
        {
            _schematic.AppendResponseLog(
                ResponseLogType.PressureReport,
                $"压力上报 · ID=0x{frame.Id:X2} Data={dataHex}");
        }
        else if (frame.Id == 0x08)
        {
            _schematic.AppendResponseLog(
                ResponseLogType.TemperatureReport,
                $"温度上报 · ID=0x08 Data={dataHex}");
        }
        else
        {
            _schematic.AppendResponseLog(
                ResponseLogType.Other,
                $"接收 · ID=0x{frame.Id:X2} DLC={frame.Dlc} Data={dataHex}");
        }
    }

    private string DescribePendingCommand()
    {
        return _pendingCommand switch
        {
            PendingCommandType.SelfLockSwitch => "自锁阀启停",
            PendingCommandType.PtcSwitch => $"PTC {_pendingPtcIndex + 1} 开关",
            PendingCommandType.ModeSwitch =>
                _pendingModeNumber <= 4
                    ? $"电磁阀 {_pendingModeNumber} 模式切换"
                    : $"PTC {_pendingModeNumber - 4} 模式切换",
            PendingCommandType.SolenoidDuty =>
                $"电磁阀 {_pendingSolenoidIndex} 占空比",
            PendingCommandType.PressureTarget =>
                $"气压传感器 {_pendingSolenoidIndex} 目标值",
            _ => "未关联命令"
        };
    }

    private void ApplySensorReport(SensorReport report)
    {
        switch (report.Id)
        {
            case 0x06:
                _lastMainPressureAt = DateTime.Now;
                _schematic.SetMainPressure(FormatSensorValue(report.Values[0]));
                break;
            case 0x07:
                _lastBranchPressureAt = DateTime.Now;
                for (var branch = 1; branch <= 4; branch++)
                {
                    _schematic.SetPressure(
                        branch,
                        FormatSensorValue(report.Values[branch - 1]));
                }
                break;
            case 0x08:
                _lastTemperatureAt = DateTime.Now;
                for (var branch = 1; branch <= 4; branch++)
                {
                    _schematic.SetTemperature(
                        branch,
                        FormatTemperatureValue(report.Values[branch - 1]));
                }
                break;
        }
    }

    private void TryProcessStatusUpload(CanFrame frame)
    {
        if (frame.Direction != FrameDirection.Rx ||
            frame.Remote ||
            frame.Id != 0x05 ||
            frame.Data.Length < 8 ||
            frame.Data[0] != 0x03 ||
            frame.Data[1] != 0x03 ||
            frame.Data[3] != 0x00)
        {
            return;
        }

        var number = frame.Data[2];
        var value = ((uint)frame.Data[4] << 24) |
                    ((uint)frame.Data[5] << 16) |
                    ((uint)frame.Data[6] << 8) |
                    frame.Data[7];
        if (number is >= 0x01 and <= 0x04)
        {
            var index = number - 1;
            _uploadedSolenoidDutyValues[index] = Math.Min(value, 1000);
            RefreshSolenoidDisplay(index);
        }
        else if (number is >= 0x05 and <= 0x08)
        {
            var index = number - 5;
            _ptcOnStates[index] = value != 0;
            UpdatePtcSchematicState(index);
            UpdateModeButtonStyles();
        }
    }

    private void RefreshSolenoidDisplay(int index)
    {
        var automatic = _solenoidAutoModes[index];
        var uploaded = _uploadedSolenoidDutyValues[index].HasValue
            ? _uploadedSolenoidDutyValues[index]!.Value / 10.0
            : (double?)null;
        var setting = _confirmedSolenoidDuties[index];
        var target = _confirmedPressureTargets[index];
        _schematic.SetSolenoidValues(
            index + 1,
            automatic,
            uploaded,
            setting,
            target);
    }

    private void ApplyModeSwitch(int number, bool automatic)
    {
        if (number is >= 1 and <= 4)
        {
            var index = number - 1;
            _solenoidAutoModes[index] = automatic;
            _confirmedSolenoidDuties[index] = 0;
            _uploadedSolenoidDutyValues[index] = 0;
            _dutyInputs[index].Value = 0;
            RefreshSolenoidDisplay(index);
        }
        else if (number is >= 5 and <= 8)
        {
            var index = number - 5;
            _ptcAutoModes[index] = automatic;
            UpdatePtcSchematicState(index);
        }
    }

    private static string FormatTemperatureValue(ushort rawValue)
    {
        if (rawValue == 4095)
        {
            return "未连接传感器";
        }
        if (rawValue > 4095)
        {
            return "超出量程";
        }

        return Pt1000TemperatureConverter.TryConvertRaw(rawValue, out var temperature)
            ? $"{temperature:0.0}°C"
            : "超出量程";
    }

    private void CheckSensorTimeouts(DateTime now)
    {
        var timeout = TimeSpan.FromSeconds(2);
        if (_lastMainPressureAt.HasValue &&
            now - _lastMainPressureAt.Value >= timeout)
        {
            _lastMainPressureAt = null;
            _schematic.SetMainPressure("--");
        }
        if (_lastBranchPressureAt.HasValue &&
            now - _lastBranchPressureAt.Value >= timeout)
        {
            _lastBranchPressureAt = null;
            for (var branch = 1; branch <= 4; branch++)
            {
                _schematic.SetPressure(branch, "--");
            }
        }
        if (_lastTemperatureAt.HasValue &&
            now - _lastTemperatureAt.Value >= timeout)
        {
            _lastTemperatureAt = null;
            for (var branch = 1; branch <= 4; branch++)
            {
                _schematic.SetTemperature(branch, "--");
            }
        }
    }

    private void ClearSensorValues()
    {
        _lastMainPressureAt = null;
        _lastBranchPressureAt = null;
        _lastTemperatureAt = null;
        _schematic.SetMainPressure("--");
        for (var branch = 1; branch <= 4; branch++)
        {
            _schematic.SetPressure(branch, "--");
            _schematic.SetTemperature(branch, "--");
        }
    }

    private static string FormatSensorValue(ushort value)
    {
        return value == 0
            ? "--"
            : value.ToString(CultureInfo.InvariantCulture);
    }

    public void SetSelfLockValveState(string value) => _schematic.SetSelfLockValveState(value);
    public void SetMainPressure(string value) => _schematic.SetMainPressure(value);
    public void SetSolenoidRatio(int branch, string value) => _schematic.SetSolenoidRatio(branch, value);
    public void SetPressure(int sensor, string value) => _schematic.SetPressure(sensor, value);
    public void SetTemperature(int branch, string value) => _schematic.SetTemperature(branch, value);

    private void SetSolenoidDuty(int branch, double percent)
    {
        if (branch is < 1 or > 4)
        {
            return;
        }
        var clamped = Math.Clamp(percent, 0, 100);
        _dutyInputs[branch - 1].Value = (decimal)clamped;
        _schematic.SetSolenoidRatio(branch, $"{clamped:0.0}%");
    }

    private void RestoreConfirmedSolenoidDuty(int branch)
    {
        if (branch is < 1 or > 4)
        {
            return;
        }
        var index = branch - 1;
        var value = _confirmedSolenoidDuties[index];
        _dutyInputs[index].Value = (decimal)(value ?? 0);
        _schematic.SetSolenoidRatio(
            branch,
            value.HasValue ? $"{value.Value:0.0}%" : "--%");
        RefreshSolenoidDisplay(index);
    }

    private void UpdateParameterControlAvailability(bool processAvailable)
    {
        var baseEnabled = processAvailable && !IsAnyCommandPending;
        for (var index = 0; index < 4; index++)
        {
            _solenoidManualButtons[index].Enabled = baseEnabled;
            _solenoidAutoButtons[index].Enabled = baseEnabled;
            _dutyInputs[index].Enabled =
                baseEnabled &&
                _selfLockOpen &&
                !_solenoidAutoModes[index];
            _dutyButtons[index].Enabled = _dutyInputs[index].Enabled;
            _pressureTargetInputs[index].Enabled =
                baseEnabled &&
                _solenoidAutoModes[index];
            _pressureTargetButtons[index].Enabled =
                _pressureTargetInputs[index].Enabled;

            _ptcManualButtons[index].Enabled = baseEnabled;
            _ptcAutoButtons[index].Enabled = baseEnabled;
            _ptcOnButtons[index].Enabled =
                baseEnabled &&
                !_ptcAutoModes[index];
            _ptcOffButtons[index].Enabled =
                baseEnabled &&
                !_ptcAutoModes[index];
            _schematic.SetSolenoidInlineEditorsEnabled(
                index + 1,
                _dutyInputs[index].Enabled,
                _pressureTargetInputs[index].Enabled);
            RefreshSolenoidDisplay(index);
            UpdatePtcSchematicState(index);
        }
        UpdateModeButtonStyles();
    }

    private void UpdateModeButtonStyles()
    {
        for (var index = 0; index < 4; index++)
        {
            ApplyToggleStyle(
                _solenoidManualButtons[index],
                !_solenoidAutoModes[index],
                AppTheme.Accent);
            ApplyToggleStyle(
                _solenoidAutoButtons[index],
                _solenoidAutoModes[index],
                AppTheme.Accent);
            ApplyToggleStyle(
                _ptcManualButtons[index],
                !_ptcAutoModes[index],
                AppTheme.Accent);
            ApplyToggleStyle(
                _ptcAutoButtons[index],
                _ptcAutoModes[index],
                AppTheme.Accent);
            ApplyToggleStyle(
                _ptcOnButtons[index],
                _ptcOnStates[index],
                AppTheme.Success);
            ApplyToggleStyle(
                _ptcOffButtons[index],
                !_ptcOnStates[index],
                AppTheme.Danger);
        }
    }

    private static void ApplyToggleStyle(Button button, bool active, Color activeColor)
    {
        button.BackColor = active ? activeColor : AppTheme.SecondaryButton;
        button.ForeColor = active ? Color.FromArgb(7, 27, 43) : AppTheme.Text;
        button.Invalidate();
    }

    private void FocusDutyInput(int branch)
    {
        if (branch is < 1 or > 4)
        {
            return;
        }
        var input = _dutyInputs[branch - 1];
        if (input.Enabled)
        {
            input.Focus();
            input.Select(0, input.Text.Length);
        }
    }

    private void SubmitDuty(int branch)
    {
        if (branch is < 1 or > 4 || !_dutyInputs[branch - 1].Enabled)
        {
            return;
        }
        SendSolenoidDutyCommand(branch, (double)_dutyInputs[branch - 1].Value);
    }

    private void SubmitPressureTarget(int branch)
    {
        if (branch is < 1 or > 4 ||
            !_pressureTargetInputs[branch - 1].Enabled)
        {
            return;
        }
        SendPressureTargetCommand(branch, (uint)_pressureTargetInputs[branch - 1].Value);
    }

    private void SetSolenoidMode(int branch, bool automatic)
    {
        if (branch is < 1 or > 4 ||
            !_canAvailable ||
            !_supplyLinkConnected ||
            IsAnyCommandPending)
        {
            return;
        }
        SendModeSwitchCommand(branch, automatic);
    }

    private void SetPtcMode(int index, bool automatic)
    {
        if (index is < 0 or > 3 ||
            !_canAvailable ||
            !_supplyLinkConnected ||
            IsAnyCommandPending)
        {
            return;
        }
        SendModeSwitchCommand(index + 5, automatic);
    }

    private void SetPtcSwitch(int index, bool on)
    {
        if (index is < 0 or > 3 ||
            _ptcAutoModes[index] ||
            IsAnyCommandPending)
        {
            return;
        }
        SendPtcSwitchCommand(index, on);
    }

    private void UpdatePtcSchematicState(int index)
    {
        if (index is >= 0 and <= 3)
        {
            _schematic.SetPtcState(
                index + 1,
                _ptcOnStates[index],
                _ptcAutoModes[index]);
        }
    }

    private void RestoreConfirmedPressureTarget(int sensor)
    {
        if (sensor is < 1 or > 4)
        {
            return;
        }
        _pressureTargetInputs[sensor - 1].Value =
            _confirmedPressureTargets[sensor - 1] ?? 0;
        RefreshSolenoidDisplay(sensor - 1);
    }

    private void SendPtcSwitchCommand(int index, bool on)
    {
        if (!_canAvailable ||
            !_supplyLinkConnected ||
            _sendAction is null ||
            IsAnyCommandPending ||
            index is < 0 or > 3)
        {
            return;
        }

        var frame = new CanFrame(
            0x01,
            false,
            false,
            4,
            [0x04, 0x01, (byte)(index + 2), on ? (byte)0x01 : (byte)0x00],
            DateTime.Now,
            FrameDirection.Tx);
        try
        {
            var error = _sendAction(frame);
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException(error);
            }

            _pendingCommand = PendingCommandType.PtcSwitch;
            _pendingPtcIndex = index;
            _pendingPtcOn = on;
            _controlDeadline = DateTime.Now.AddSeconds(1.5);
            _selfLockTimer.Start();
            StatusChanged?.Invoke(
                $"正在{(on ? "开启" : "关闭")} PTC {index + 1}…");
            UpdateControlAvailability();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"PTC {index + 1} 命令发送失败：{ex.Message}");
        }
    }

    private void SendPressureTargetCommand(int sensor, uint target)
    {
        if (!_canAvailable ||
            !_supplyLinkConnected ||
            _sendAction is null ||
            IsAnyCommandPending ||
            sensor is < 1 or > 4 ||
            !_solenoidAutoModes[sensor - 1])
        {
            return;
        }

        var frame = new CanFrame(
            0x01,
            false,
            false,
            8,
            [
                0x04,
                0x03,
                (byte)(sensor + 4),
                0x00,
                (byte)(target >> 24),
                (byte)(target >> 16),
                (byte)(target >> 8),
                (byte)target
            ],
            DateTime.Now,
            FrameDirection.Tx);
        try
        {
            var error = _sendAction(frame);
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException(error);
            }

            _pendingCommand = PendingCommandType.PressureTarget;
            _pendingSolenoidIndex = sensor;
            _pendingPressureTarget = target;
            _controlDeadline = DateTime.Now.AddSeconds(1.5);
            _selfLockTimer.Start();
            StatusChanged?.Invoke(
                $"正在设置气压传感器 {sensor} 目标值：{target}");
            UpdateControlAvailability();
        }
        catch (Exception ex)
        {
            RestoreConfirmedPressureTarget(sensor);
            StatusChanged?.Invoke($"气压传感器 {sensor} 目标值发送失败：{ex.Message}");
        }
    }

    private void SendModeSwitchCommand(int number, bool automatic)
    {
        if (!_canAvailable ||
            !_supplyLinkConnected ||
            _sendAction is null ||
            IsAnyCommandPending ||
            number is < 1 or > 8)
        {
            return;
        }

        var frame = new CanFrame(
            0x01,
            false,
            false,
            4,
            [0x04, 0x02, (byte)number, automatic ? (byte)0x00 : (byte)0x01],
            DateTime.Now,
            FrameDirection.Tx);
        try
        {
            var error = _sendAction(frame);
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException(error);
            }

            _pendingCommand = PendingCommandType.ModeSwitch;
            _pendingModeNumber = number;
            _pendingModeAutomatic = automatic;
            _controlDeadline = DateTime.Now.AddSeconds(1.5);
            _selfLockTimer.Start();
            StatusChanged?.Invoke(
                $"正在将{(number <= 4 ? $"电磁阀 {number}" : $"PTC {number - 4}")}" +
                $"切换为{(automatic ? "自动" : "手动")}模式…");
            UpdateControlAvailability();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("模式切换命令发送失败：" + ex.Message);
        }
    }

    private void SendSelfLockCommand(bool open)
    {
        if (!_canAvailable ||
            !_supplyLinkConnected ||
            _sendAction is null ||
            IsAnyCommandPending)
        {
            return;
        }

        var frame = new CanFrame(
            0x01,
            false,
            false,
            4,
            [0x04, 0x01, 0x01, open ? (byte)0x01 : (byte)0x00],
            DateTime.Now,
            FrameDirection.Tx);
        try
        {
            var error = _sendAction(frame);
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException(error);
            }

            _requestedSelfLockOpen = open;
            _pendingCommand = PendingCommandType.SelfLockSwitch;
            _controlDeadline = DateTime.Now.AddSeconds(1.5);
            _selfLockTimer.Start();
            _schematic.SetSelfLockControlEnabled(false);
            _schematic.SetSelfLockValveState("等待应答");
            if (!open)
            {
                ResetAllSolenoidDuties();
            }
            UpdateControlAvailability();
            StatusChanged?.Invoke(open ? "正在开启自锁阀…" : "正在关闭自锁阀…");
        }
        catch (Exception ex)
        {
            _schematic.SetSelfLockValveState("未知");
            _schematic.SetGasFlowEnabled(false);
            UpdateControlAvailability();
            StatusChanged?.Invoke("自锁阀操作失败：" + ex.Message);
        }
    }

    private void SendSolenoidDutyCommand(int branch, double dutyPercent)
    {
        if (branch is >= 1 and <= 4 && _solenoidAutoModes[branch - 1])
        {
            StatusChanged?.Invoke($"电磁阀 {branch} 处于自动模式，不能设置占空比");
            return;
        }

        if (!_canAvailable ||
            !_supplyLinkConnected ||
            _sendAction is null ||
            IsAnyCommandPending ||
            branch is < 1 or > 4)
        {
            return;
        }

        var dutyValue = (uint)Math.Round(
            Math.Clamp(dutyPercent, 0, 100) * 10,
            MidpointRounding.AwayFromZero);
        var frame = new CanFrame(
            0x01,
            false,
            false,
            8,
            [
                0x04,
                0x03,
                (byte)branch,
                0x00,
                (byte)(dutyValue >> 24),
                (byte)(dutyValue >> 16),
                (byte)(dutyValue >> 8),
                (byte)dutyValue
            ],
            DateTime.Now,
            FrameDirection.Tx);
        try
        {
            var error = _sendAction(frame);
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException(error);
            }

            _pendingCommand = PendingCommandType.SolenoidDuty;
            _pendingSolenoidIndex = branch;
            _pendingSolenoidDuty = dutyValue / 10.0;
            _controlDeadline = DateTime.Now.AddSeconds(1.5);
            _selfLockTimer.Start();
            _schematic.SetSolenoidRatio(
                branch,
                $"设置中 {Math.Clamp(dutyPercent, 0, 100):0.0}%");
            StatusChanged?.Invoke(
                $"正在设置电磁阀 {branch} 占空比：{_pendingSolenoidDuty:0.0}%");
        }
        catch (Exception ex)
        {
            RestoreConfirmedSolenoidDuty(branch);
            UpdateControlAvailability();
            StatusChanged?.Invoke("电磁阀占空比发送失败：" + ex.Message);
        }
    }

    private void CheckCommandAck()
    {
        if (_pendingCommand == PendingCommandType.None ||
            DateTime.Now <= _controlDeadline)
        {
            return;
        }

        var timedOutCommand = _pendingCommand;
        _pendingCommand = PendingCommandType.None;
        _selfLockTimer.Stop();
        switch (timedOutCommand)
        {
            case PendingCommandType.SelfLockSwitch:
                _schematic.SetSelfLockValveState(_selfLockOpen ? "开启" : "关闭");
                _schematic.SetGasFlowEnabled(_selfLockOpen);
                StatusChanged?.Invoke("自锁阀命令应答超时");
                break;
            case PendingCommandType.PtcSwitch:
                UpdatePtcSchematicState(_pendingPtcIndex);
                StatusChanged?.Invoke($"PTC {_pendingPtcIndex + 1} 开关命令应答超时");
                break;
            case PendingCommandType.ModeSwitch:
                StatusChanged?.Invoke("模式切换命令应答超时");
                break;
            case PendingCommandType.SolenoidDuty:
                RestoreConfirmedSolenoidDuty(_pendingSolenoidIndex);
                StatusChanged?.Invoke($"电磁阀 {_pendingSolenoidIndex} 占空比设置应答超时");
                break;
            case PendingCommandType.PressureTarget:
                RestoreConfirmedPressureTarget(_pendingSolenoidIndex);
                StatusChanged?.Invoke($"气压传感器 {_pendingSolenoidIndex} 目标值设置应答超时");
                break;
        }
        UpdateControlAvailability();
    }

    private void CompletePendingCommand(bool acknowledged)
    {
        var completedCommand = _pendingCommand;
        _pendingCommand = PendingCommandType.None;
        _selfLockTimer.Stop();

        switch (completedCommand)
        {
            case PendingCommandType.SelfLockSwitch:
                if (acknowledged)
                {
                    _selfLockOpen = _requestedSelfLockOpen;
                    _schematic.SetSelfLockValveState(_selfLockOpen ? "开启" : "关闭");
                    _schematic.SetGasFlowEnabled(_selfLockOpen);
                    if (_selfLockOpen)
                    {
                        ResetAllSolenoidDuties();
                    }
                }
                else
                {
                    _schematic.SetSelfLockValveState(_selfLockOpen ? "开启" : "关闭");
                    _schematic.SetGasFlowEnabled(_selfLockOpen);
                }
                StatusChanged?.Invoke(
                    acknowledged
                        ? _selfLockOpen
                            ? "自锁阀已开启"
                            : "自锁阀已关闭"
                        : "自锁阀命令被从机拒绝（NAK）");
                break;
            case PendingCommandType.PtcSwitch:
                if (acknowledged)
                {
                    _ptcOnStates[_pendingPtcIndex] = _pendingPtcOn;
                }
                UpdatePtcSchematicState(_pendingPtcIndex);
                StatusChanged?.Invoke(
                    acknowledged
                        ? $"PTC {_pendingPtcIndex + 1} 已{(_pendingPtcOn ? "开启" : "关闭")}"
                        : $"PTC {_pendingPtcIndex + 1} 开关命令被拒绝（NAK）");
                break;
            case PendingCommandType.ModeSwitch:
                if (acknowledged)
                {
                    ApplyModeSwitch(_pendingModeNumber, _pendingModeAutomatic);
                    StatusChanged?.Invoke(
                        $"{(_pendingModeNumber <= 4
                            ? $"电磁阀 {_pendingModeNumber}"
                            : $"PTC {_pendingModeNumber - 4}")}" +
                        $"已切换为{(_pendingModeAutomatic ? "自动" : "手动")}模式");
                }
                else
                {
                    StatusChanged?.Invoke("模式切换命令被拒绝（NAK）");
                }
                break;
            case PendingCommandType.SolenoidDuty:
                if (acknowledged)
                {
                    SetSolenoidDuty(_pendingSolenoidIndex, _pendingSolenoidDuty);
                    _confirmedSolenoidDuties[_pendingSolenoidIndex - 1] =
                        _pendingSolenoidDuty;
                    RefreshSolenoidDisplay(_pendingSolenoidIndex - 1);
                    StatusChanged?.Invoke(
                        $"电磁阀 {_pendingSolenoidIndex} 占空比已设置：{_pendingSolenoidDuty:0.0}%");
                }
                else
                {
                    RestoreConfirmedSolenoidDuty(_pendingSolenoidIndex);
                    StatusChanged?.Invoke(
                        $"电磁阀 {_pendingSolenoidIndex} 占空比设置被拒绝（NAK）");
                }
                break;
            case PendingCommandType.PressureTarget:
                if (acknowledged)
                {
                    _confirmedPressureTargets[_pendingSolenoidIndex - 1] =
                        _pendingPressureTarget;
                    _pressureTargetInputs[_pendingSolenoidIndex - 1].Value =
                        _pendingPressureTarget;
                    RefreshSolenoidDisplay(_pendingSolenoidIndex - 1);
                    StatusChanged?.Invoke(
                        $"气压传感器 {_pendingSolenoidIndex} 目标值已设置：{_pendingPressureTarget}");
                }
                else
                {
                    RestoreConfirmedPressureTarget(_pendingSolenoidIndex);
                    RefreshSolenoidDisplay(_pendingSolenoidIndex - 1);
                    StatusChanged?.Invoke(
                        $"气压传感器 {_pendingSolenoidIndex} 目标值被拒绝（NAK）");
                }
                break;
        }
        UpdateControlAvailability();
    }

    private void BuildInterface()
    {
        Paint += (_, e) =>
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };

        _schematicTitle.Text = "储供气路示意";
        _schematicTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        _schematicTitle.ForeColor = TextColor;
        _schematicTitle.AutoSize = true;
        _schematicTitle.Location = new Point(14, 10);

        _schematicHost.BackColor = BackgroundColor;
        _schematicHost.Padding = new Padding(8, 32, 8, 8);
        _schematicHost.Controls.Add(_schematic);
        _schematicHost.Controls.Add(_schematicTitle);

        _connection.Dock = DockStyle.Fill;
        _schematicHost.Dock = DockStyle.Fill;

        _connectionHost.Dock = DockStyle.Fill;
        _connectionHost.BackColor = AppTheme.Card;
        _connectionHost.Paint += (_, e) =>
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawRectangle(
                pen,
                0,
                0,
                _connectionHost.Width - 1,
                _connectionHost.Height - 1);
        };

        _connectionHeader.Dock = DockStyle.Top;
        _connectionHeader.Height = 34;
        _connectionHeader.BackColor = AppTheme.Card;
        _controlConfigButton.Text = "配置";
        _controlConfigButton.FlatStyle = FlatStyle.Flat;
        _controlConfigButton.FlatAppearance.BorderColor = AppTheme.Border;
        _controlConfigButton.BackColor = AppTheme.SecondaryButton;
        _controlConfigButton.ForeColor = AppTheme.Text;
        _controlConfigButton.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        _controlConfigButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _controlConfigButton.Bounds = new Rectangle(Width - 174, 5, 70, 25);
        _controlConfigButton.Click +=
            (_, _) => PressurePlotConfigurationRequested?.Invoke();
        _connectionHeader.Controls.Add(_controlConfigButton);
        _connectionHost.Controls.Add(_connection);
        _connectionHost.Controls.Add(_connectionHeader);
        _connection.Visible = true;
        Resize += (_, _) =>
        {
            _controlConfigButton.Left = Width - 174;
        };

        BuildDutyEditorBar();

        _layout.Dock = DockStyle.Fill;
        _layout.ColumnCount = 1;
        _layout.RowCount = 2;
        _layout.BackColor = BackgroundColor;
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _layout.Controls.Add(_connectionHost, 0, 0);
        _layout.Controls.Add(_schematicHost, 0, 1);
        Controls.Add(_layout);
    }

    private void BuildDutyEditorBar()
    {
        _dutyEditorBar.Dock = DockStyle.Fill;
        _dutyEditorBar.BackColor = AppTheme.Card;
        _dutyEditorBar.Paint += (_, e) =>
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawRectangle(
                pen,
                0,
                0,
                _dutyEditorBar.Width - 1,
                _dutyEditorBar.Height - 1);
        };

        _dutyLockHintLabel.Dock = DockStyle.Top;
        _dutyLockHintLabel.Height = 23;
        _dutyLockHintLabel.TextAlign = ContentAlignment.MiddleCenter;
        _dutyLockHintLabel.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        _dutyLockHintLabel.ForeColor = AppTheme.Danger;
        _dutyLockHintLabel.Text = "CAN 适配器未连接";

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 8.6F, FontStyle.Bold),
            Padding = new Point(12, 4)
        };
        var valveTab = new TabPage("电磁阀 / 气压目标")
        {
            BackColor = AppTheme.Card,
            ForeColor = AppTheme.Text
        };
        var ptcTab = new TabPage("PTC 加热板")
        {
            BackColor = AppTheme.Card,
            ForeColor = AppTheme.Text
        };
        var valveGrid = CreateParameterGrid();
        var ptcGrid = CreateParameterGrid();
        for (var index = 0; index < 4; index++)
        {
            valveGrid.Controls.Add(BuildSolenoidEditor(index), index % 2, index / 2);
            ptcGrid.Controls.Add(BuildPtcEditor(index), index % 2, index / 2);
        }
        valveTab.Controls.Add(valveGrid);
        ptcTab.Controls.Add(ptcGrid);
        tabs.TabPages.Add(valveTab);
        tabs.TabPages.Add(ptcTab);
        _dutyEditorBar.Controls.Add(tabs);
        _dutyEditorBar.Controls.Add(_dutyLockHintLabel);
    }

    private static TableLayoutPanel CreateParameterGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = AppTheme.Card,
            Padding = new Padding(2)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        return grid;
    }

    private Control BuildSolenoidEditor(int index)
    {
        var branch = index + 1;
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Card,
            Margin = new Padding(3)
        };
        var title = ParameterLabel($"支路 {branch}", 4, 3, 42);
        var manual = ParameterButton("手动", 48, 2, 44, 22);
        var automatic = ParameterButton("自动", 96, 2, 44, 22);
        manual.Click += (_, _) => SetSolenoidMode(branch, false);
        automatic.Click += (_, _) => SetSolenoidMode(branch, true);

        var dutyLabel = ParameterLabel("占空", 4, 28, 30);
        var dutyInput = ParameterInput(4, 28);
        var dutyButton = ParameterButton("✓", 104, 25, 28, 24);
        dutyInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                SubmitDuty(branch);
                e.SuppressKeyPress = true;
            }
        };
        dutyButton.Click += (_, _) => SubmitDuty(branch);

        var targetLabel = ParameterLabel("目标", 4, 52, 30);
        var targetInput = ParameterInput(4, 52);
        targetInput.DecimalPlaces = 0;
        targetInput.Increment = 1;
        targetInput.Maximum = 65_535;
        var targetButton = ParameterButton("✓", 104, 49, 28, 24);
        targetInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                SubmitPressureTarget(branch);
                e.SuppressKeyPress = true;
            }
        };
        targetButton.Click += (_, _) => SubmitPressureTarget(branch);

        panel.Controls.AddRange(
        [
            title,
            manual,
            automatic,
            dutyLabel,
            dutyInput,
            dutyButton,
            targetLabel,
            targetInput,
            targetButton
        ]);
        _dutyInputs[index] = dutyInput;
        _dutyButtons[index] = dutyButton;
        _pressureTargetInputs[index] = targetInput;
        _pressureTargetButtons[index] = targetButton;
        _solenoidManualButtons[index] = manual;
        _solenoidAutoButtons[index] = automatic;
        return panel;
    }

    private Control BuildPtcEditor(int index)
    {
        var ptc = index + 1;
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Card,
            Margin = new Padding(3)
        };
        var title = ParameterLabel($"PTC {ptc}", 4, 3, 42);
        var manual = ParameterButton("手动", 48, 2, 44, 22);
        var automatic = ParameterButton("自动", 96, 2, 44, 22);
        manual.Click += (_, _) => SetPtcMode(index, false);
        automatic.Click += (_, _) => SetPtcMode(index, true);

        var switchLabel = ParameterLabel("开关", 4, 33, 30);
        var onButton = ParameterButton("开", 48, 28, 44, 24);
        var offButton = ParameterButton("关", 96, 28, 44, 24);
        onButton.Click += (_, _) => SetPtcSwitch(index, true);
        offButton.Click += (_, _) => SetPtcSwitch(index, false);

        panel.Controls.AddRange(
        [
            title,
            manual,
            automatic,
            switchLabel,
            onButton,
            offButton
        ]);
        _ptcManualButtons[index] = manual;
        _ptcAutoButtons[index] = automatic;
        _ptcOnButtons[index] = onButton;
        _ptcOffButtons[index] = offButton;
        return panel;
    }

    private static Label ParameterLabel(string text, int x, int y, int width) => new()
    {
        Text = text,
        AutoSize = false,
        Bounds = new Rectangle(x, y, width, 21),
        ForeColor = AppTheme.Muted,
        Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static NumericUpDown ParameterInput(int x, int y) => new()
    {
        DecimalPlaces = 1,
        Increment = 0.1m,
        Minimum = 0,
        Maximum = 100,
        Value = 0,
        TextAlign = HorizontalAlignment.Right,
        BackColor = AppTheme.Field,
        ForeColor = AppTheme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Cascadia Mono", 8.5F),
        Bounds = new Rectangle(x + 32, y - 3, 66, 24),
        Enabled = false
    };

    private static Button ParameterButton(string text, int x, int y, int width, int height)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = AppTheme.SecondaryButton,
            ForeColor = AppTheme.Text,
            Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Bold),
            Bounds = new Rectangle(x, y, width, height),
            Enabled = false,
            Padding = Padding.Empty
        };
        button.FlatAppearance.BorderColor = AppTheme.Border;
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    private enum PendingCommandType
    {
        None,
        SelfLockSwitch,
        PtcSwitch,
        ModeSwitch,
        SolenoidDuty,
        PressureTarget
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _selfLockTimer.Dispose();
            _sensorTimeoutTimer.Dispose();
            _connection.Dispose();
        }
        base.Dispose(disposing);
    }
}
