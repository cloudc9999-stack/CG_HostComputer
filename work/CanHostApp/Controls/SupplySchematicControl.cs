using System.Drawing.Drawing2D;

namespace CanHost.Controls;

public enum ResponseLogType
{
    ConnectionSn,
    HeartbeatAck,
    Ack,
    Nak,
    StatusUpload,
    PressureReport,
    TemperatureReport,
    Other
}

public sealed record ResponseLogEntry(
    DateTime Timestamp,
    ResponseLogType Type,
    string Message);

public sealed class SupplySchematicControl : UserControl
{
    private const int DesignWidth = 1800;
    private const int DesignHeight = 780;
    private static readonly int[] DesignBranchCenters = [150, 320, 490, 660];

    private readonly SchematicSurface _surface = new();

    public static bool AnimationEnabled { get; set; } = true;

    public SupplySchematicControl()
    {
        ResizeRedraw = true;
        BackColor = AppTheme.Field;
        Dock = DockStyle.Fill;
        _surface.Dock = DockStyle.Fill;
        Controls.Add(_surface);
        _surface.BranchClicked += branch => BranchSelected?.Invoke(branch);
        _surface.SelfLockCommandRequested += open => SelfLockCommandRequested?.Invoke(open);
        _surface.SolenoidModeRequested +=
            (branch, automatic) => SolenoidModeRequested?.Invoke(branch, automatic);
        _surface.InlineSolenoidDutySetRequested +=
            (branch, value) => InlineSolenoidDutySetRequested?.Invoke(branch, value);
        _surface.InlinePressureTargetSetRequested +=
            (branch, value) => InlinePressureTargetSetRequested?.Invoke(branch, value);
        _surface.PtcModeRequested +=
            (ptc, automatic) => PtcModeRequested?.Invoke(ptc, automatic);
        _surface.PtcSwitchRequested +=
            (ptc, on) => PtcSwitchRequested?.Invoke(ptc, on);
    }

    public event Action<int>? BranchSelected;
    public event Action<bool>? SelfLockCommandRequested;
    public event Action<int, bool>? SolenoidModeRequested;
    public event Action<int, double>? InlineSolenoidDutySetRequested;
    public event Action<int, uint>? InlinePressureTargetSetRequested;
    public event Action<int, bool>? PtcModeRequested;
    public event Action<int, bool>? PtcSwitchRequested;

    public bool CanControl
    {
        get => _surface.CanControl;
        set => _surface.CanControl = value;
    }

    public bool GasFlowEnabled => _surface.GasFlowEnabled;
    public IReadOnlyList<ResponseLogEntry> ResponseLog => _surface.ResponseLog;
    public string GetSolenoidRatioText(int branch) => _surface.GetSolenoidRatioText(branch);
    public bool IsBranchFlowEnabled(int branch) => _surface.IsBranchFlowEnabled(branch);
    public string GetMainPressureText() => _surface.GetMainPressureText();
    public string GetPressureText(int sensor) => _surface.GetPressureText(sensor);
    public string GetTemperatureText(int branch) => _surface.GetTemperatureText(branch);
    public void SetGasFlowEnabled(bool enabled) => _surface.SetGasFlowEnabled(enabled);
    public void SetSelfLockControlEnabled(bool enabled) => _surface.SetSelfLockControlEnabled(enabled);
    public void SetSelfLockDisabledHint(string text) => _surface.SetSelfLockDisabledHint(text);
    public void SetSelfLockValveState(string value) => _surface.SetSelfLockValveState(value);
    public void SetMainPressure(string value) => _surface.SetMainPressure(value);
    public void SetSolenoidRatio(int branch, string value) => _surface.SetSolenoidRatio(branch, value);
    public void SetSolenoidValues(
        int branch,
        bool automatic,
        double? uploadedDuty,
        double? settingDuty,
        uint? pressureTarget) =>
        _surface.SetSolenoidValues(
            branch,
            automatic,
            uploadedDuty,
            settingDuty,
            pressureTarget);
    public void SetSolenoidInlineEditorsEnabled(int branch, bool duty, bool target) =>
        _surface.SetSolenoidInlineEditorsEnabled(branch, duty, target);
    public void FocusInlineEditor(int branch) => _surface.FocusInlineEditor(branch);
    public void AppendResponseLog(ResponseLogType type, string message) =>
        _surface.AppendResponseLog(type, message);
    public void SetPressure(int sensor, string value) => _surface.SetPressure(sensor, value);
    public void SetTemperature(int branch, string value) => _surface.SetTemperature(branch, value);
    public void SetPtcState(int ptc, bool on, bool automatic) =>
        _surface.SetPtcState(ptc, on, automatic);
    public void FlashBranch(int branch) => _surface.FlashBranch(branch);

    private sealed class SchematicSurface : Control
    {
        private const int MainCenterY = 150;
        private static readonly Rectangle SelfLockOpenRect = new(332, 244, 96, 34);
        private static readonly Rectangle SelfLockCloseRect = new(436, 244, 96, 34);
        private static readonly int[] BranchCenters = DesignBranchCenters;

        private static Color PipeColor => AppTheme.Accent;
        private static Color PipeOutline => Color.FromArgb(
            AppTheme.IsLight ? 80 : 120,
            AppTheme.IsLight ? 120 : 210,
            AppTheme.IsLight ? 170 : 240);
        private static Color HeatColor => Color.FromArgb(239, 68, 68);
        private static Color ColdColor => Color.FromArgb(147, 197, 253);
        private static Color WarmColor => Color.FromArgb(251, 146, 60);

        private readonly string[] _ratios = ["--%", "--%", "--%", "--%"];
        private readonly string[] _uploadedDutyTexts = ["上传 --", "上传 --", "上传 --", "上传 --"];
        private readonly string[] _settingDutyTexts = ["设定 --", "设定 --", "设定 --", "设定 --"];
        private readonly string[] _pressureTargetTexts = ["目标 --", "目标 --", "目标 --", "目标 --"];
        private readonly bool[] _solenoidAutoModes = [false, false, false, false];
        private readonly string[] _pressures = ["--", "--", "--", "--"];
        private readonly string[] _temperatures = ["--", "--", "--", "--"];
        private readonly bool[] _ptcOnStates = [false, false, false, false];
        private readonly bool[] _ptcAutoModes = [false, false, false, false];
        private readonly System.Windows.Forms.Timer _flashTimer = new() { Interval = 180 };
        private readonly Font _fontSmall = new("Microsoft YaHei UI", 10F);
        private readonly Font _fontCompact = new("Microsoft YaHei UI", 9F);
        private readonly Font _fontValue = new("Microsoft YaHei UI", 11F, FontStyle.Bold);
        private readonly Font _fontNormal = new("Microsoft YaHei UI", 12F);
        private readonly Font _fontBold = new("Microsoft YaHei UI", 13F, FontStyle.Bold);
        private readonly Font _fontSensor = new("Microsoft YaHei UI", 15F, FontStyle.Bold);
        private readonly Font _fontTank = new("Microsoft YaHei UI", 22F, FontStyle.Bold);
        private readonly System.Windows.Forms.Timer _flowTimer = new() { Interval = 500 };
        private readonly System.Windows.Forms.Timer _valveAnimationTimer = new() { Interval = 50 };
        private readonly bool[] _branchFlowEnabled = new bool[4];
        private readonly List<ResponseLogEntry> _responseLog = [];
        private readonly Dictionary<ResponseLogType, bool> _responseLogFilters = new()
        {
            [ResponseLogType.ConnectionSn] = true,
            [ResponseLogType.HeartbeatAck] = true,
            [ResponseLogType.Ack] = true,
            [ResponseLogType.Nak] = true,
            [ResponseLogType.StatusUpload] = true,
            [ResponseLogType.PressureReport] = false,
            [ResponseLogType.TemperatureReport] = false,
            [ResponseLogType.Other] = true
        };
        private static readonly Rectangle ResponseLogRect = new(40, 675, 900, 76);
        private static readonly Rectangle ResponseFilterRect = new(850, 680, 82, 25);
        private readonly NumericUpDown[] _inlineDutyInputs = new NumericUpDown[4];
        private readonly Button[] _inlineDutyButtons = new Button[4];
        private readonly NumericUpDown[] _inlineTargetInputs = new NumericUpDown[4];
        private readonly Button[] _inlineTargetButtons = new Button[4];
        private readonly float[] _solenoidTargetPercent = new float[4];
        private readonly float[] _solenoidVisualPercent = new float[4];

        private string _selfLockState = "未接入";
        private string _mainPressure = "--";
        private int _flashBranch = -1;
        private bool _canControl;
        private bool _selfLockControlEnabled = true;
        private string _selfLockDisabledHint = "CAN 未连接";
        private bool _gasFlowEnabled;
        private float _scale = 1f;
        private float _offsetX;
        private float _offsetY;
        private float _flowPhase;
        private float _valveAnimationPhase;
        private float _selfLockTargetOpen;
        private float _selfLockVisualOpen;
        private ContextMenuStrip? _responseFilterMenu;

        public SchematicSurface()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = AppTheme.Field;
            CreateInlineEditors();
            _flashTimer.Tick += (_, _) =>
            {
                _flashTimer.Stop();
                _flashBranch = -1;
                Invalidate();
            };
            _flowTimer.Tick += (_, _) =>
            {
                _flowPhase = (_flowPhase + 2) % 72;
                Invalidate();
            };
            _valveAnimationTimer.Tick += (_, _) =>
            {
                var moving = false;
                var selfLockDelta = _selfLockTargetOpen - _selfLockVisualOpen;
                if (Math.Abs(selfLockDelta) > 0.01f)
                {
                    _selfLockVisualOpen += Math.Clamp(selfLockDelta, -0.12f, 0.12f);
                    moving = true;
                }
                else
                {
                    _selfLockVisualOpen = _selfLockTargetOpen;
                }

                for (var index = 0; index < _solenoidVisualPercent.Length; index++)
                {
                    var delta = _solenoidTargetPercent[index] - _solenoidVisualPercent[index];
                    if (Math.Abs(delta) > 0.05f)
                    {
                        _solenoidVisualPercent[index] += Math.Clamp(delta, -3f, 3f);
                        moving = true;
                    }
                    else
                    {
                        _solenoidVisualPercent[index] = _solenoidTargetPercent[index];
                    }
                }

                _valveAnimationPhase = (_valveAnimationPhase + 0.16f) % 1f;
                Invalidate();
                if (!moving &&
                    _selfLockTargetOpen < 0.01f &&
                    _solenoidTargetPercent.All(value => value < 0.01f))
                {
                    _valveAnimationTimer.Stop();
                }
            };
            _flowTimer.Enabled = Visible &&
                                 SupplySchematicControl.AnimationEnabled &&
                                 _gasFlowEnabled;
            UpdateValveAnimationTimer();
        }

        private void CreateInlineEditors()
        {
            for (var index = 0; index < 4; index++)
            {
                var branch = index + 1;
                var dutyInput = CreateInlineNumeric(0, 100, 1, 0.1m);
                var dutyButton = CreateInlineButton("✓");
                dutyInput.KeyDown += (_, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        InlineSolenoidDutySetRequested?.Invoke(
                            branch,
                            (double)dutyInput.Value);
                        e.SuppressKeyPress = true;
                    }
                };
                dutyButton.Click += (_, _) =>
                    InlineSolenoidDutySetRequested?.Invoke(
                        branch,
                        (double)dutyInput.Value);
                Controls.Add(dutyInput);
                Controls.Add(dutyButton);
                _inlineDutyInputs[index] = dutyInput;
                _inlineDutyButtons[index] = dutyButton;

                var targetInput = CreateInlineNumeric(0, 65_535, 0, 1);
                var targetButton = CreateInlineButton("✓");
                targetInput.KeyDown += (_, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        InlinePressureTargetSetRequested?.Invoke(
                            branch,
                            (uint)targetInput.Value);
                        e.SuppressKeyPress = true;
                    }
                };
                targetButton.Click += (_, _) =>
                    InlinePressureTargetSetRequested?.Invoke(
                        branch,
                        (uint)targetInput.Value);
                Controls.Add(targetInput);
                Controls.Add(targetButton);
                _inlineTargetInputs[index] = targetInput;
                _inlineTargetButtons[index] = targetButton;
            }
        }

        private static NumericUpDown CreateInlineNumeric(
            decimal minimum,
            decimal maximum,
            int decimalPlaces,
            decimal increment) => new()
            {
                Minimum = minimum,
                Maximum = maximum,
                DecimalPlaces = decimalPlaces,
                Increment = increment,
                TextAlign = HorizontalAlignment.Right,
                BackColor = AppTheme.Field,
                ForeColor = AppTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Cascadia Mono", 8F),
                ThousandsSeparator = false,
                Visible = false
            };

        private static Button CreateInlineButton(string text)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = AppTheme.SecondaryButton,
                ForeColor = AppTheme.Accent,
                Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold),
                Padding = Padding.Empty,
                Visible = false
            };
            button.FlatAppearance.BorderColor = AppTheme.Border;
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        private void UpdateViewTransform()
        {
            _scale = Math.Min(
                Width / (float)DesignWidth,
                Height / (float)DesignHeight);
            if (_scale <= 0)
            {
                return;
            }
            _offsetX = (Width - DesignWidth * _scale) / 2f;
            _offsetY = (Height - DesignHeight * _scale) / 2f;
        }

        private Rectangle ToClientRectangle(Rectangle designRectangle)
        {
            return new Rectangle(
                (int)Math.Round(_offsetX + designRectangle.X * _scale),
                (int)Math.Round(_offsetY + designRectangle.Y * _scale),
                Math.Max(38, (int)Math.Round(designRectangle.Width * _scale)),
                Math.Max(20, (int)Math.Round(designRectangle.Height * _scale)));
        }

        private void LayoutInlineEditors()
        {
            if (_scale <= 0)
            {
                return;
            }

            for (var index = 0; index < 4; index++)
            {
                var valveBody = GetBranchButton(index);
                var dutyRect = ToClientRectangle(new Rectangle(
                    valveBody.X + 2,
                    valveBody.Bottom + 29,
                    76,
                    22));
                _inlineDutyInputs[index].Bounds = dutyRect;
                _inlineDutyButtons[index].Bounds = new Rectangle(
                    dutyRect.Right + 2,
                    dutyRect.Top,
                    24,
                    dutyRect.Height);
                _inlineDutyInputs[index].Visible = !_solenoidAutoModes[index];
                _inlineDutyButtons[index].Visible = !_solenoidAutoModes[index];

                var pressureBody = GetPressureBody(index);
                var targetRect = ToClientRectangle(new Rectangle(
                    pressureBody.X + 5,
                    pressureBody.Bottom + 5,
                    60,
                    22));
                _inlineTargetInputs[index].Bounds = targetRect;
                _inlineTargetButtons[index].Bounds = new Rectangle(
                    targetRect.Right + 2,
                    targetRect.Top,
                    24,
                    targetRect.Height);
                _inlineTargetInputs[index].Visible = _solenoidAutoModes[index];
                _inlineTargetButtons[index].Visible = _solenoidAutoModes[index];
            }
        }

        public void SetSolenoidInlineEditorsEnabled(int branch, bool duty, bool target)
        {
            if (branch is < 1 or > 4)
            {
                return;
            }

            var index = branch - 1;
            _inlineDutyInputs[index].Enabled = duty;
            _inlineDutyButtons[index].Enabled = duty;
            _inlineTargetInputs[index].Enabled = target;
            _inlineTargetButtons[index].Enabled = target;
        }

        public void FocusInlineEditor(int branch)
        {
            if (branch is < 1 or > 4)
            {
                return;
            }
            var index = branch - 1;
            var editor = _solenoidAutoModes[index]
                ? _inlineTargetInputs[index]
                : _inlineDutyInputs[index];
            if (editor.Visible && editor.Enabled)
            {
                editor.Focus();
                editor.Select(0, editor.Text.Length);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateViewTransform();
            LayoutInlineEditors();
            Invalidate(true);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            _flowTimer.Enabled = Visible &&
                                 SupplySchematicControl.AnimationEnabled &&
                                 _gasFlowEnabled;
            UpdateValveAnimationTimer();
        }

        public event Action<int>? BranchClicked;
        public event Action<bool>? SelfLockCommandRequested;
        public event Action<int, bool>? SolenoidModeRequested;
        public event Action<int, double>? InlineSolenoidDutySetRequested;
        public event Action<int, uint>? InlinePressureTargetSetRequested;
        public event Action<int, bool>? PtcModeRequested;
        public event Action<int, bool>? PtcSwitchRequested;

        public bool CanControl
        {
            get => _canControl;
            set
            {
                _canControl = value;
                Cursor = value ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        public bool GasFlowEnabled => _gasFlowEnabled;
        public IReadOnlyList<ResponseLogEntry> ResponseLog => _responseLog;
        public string GetSolenoidRatioText(int branch) =>
            branch is >= 1 and <= 4 ? _ratios[branch - 1] : "--";
        public string GetMainPressureText() => _mainPressure;
        public string GetPressureText(int sensor) =>
            sensor is >= 1 and <= 4 ? _pressures[sensor - 1] : "--";
        public string GetTemperatureText(int branch) =>
            branch is >= 1 and <= 4 ? _temperatures[branch - 1] : "--";
        public bool IsBranchFlowEnabled(int branch) =>
            branch is >= 1 and <= 4 && _gasFlowEnabled && _branchFlowEnabled[branch - 1];

        public void SetSelfLockValveState(string value)
        {
            _selfLockState = value;
            if (value == "开启")
            {
                _selfLockTargetOpen = 1f;
            }
            else if (value is "关闭" or "未接入")
            {
                _selfLockTargetOpen = 0f;
            }
            UpdateValveAnimationTimer();
            Invalidate();
        }

        public void SetGasFlowEnabled(bool enabled)
        {
            _gasFlowEnabled = enabled;
            _flowTimer.Enabled = Visible && SupplySchematicControl.AnimationEnabled && enabled;
            UpdateValveAnimationTimer();
            Invalidate();
        }

        public void AppendResponseLog(ResponseLogType type, string message)
        {
            _responseLog.Add(new ResponseLogEntry(DateTime.Now, type, message));
            if (_responseLog.Count > 500)
            {
                _responseLog.RemoveRange(0, _responseLog.Count - 500);
            }
            Invalidate();
        }

        public void SetSelfLockControlEnabled(bool enabled)
        {
            _selfLockControlEnabled = enabled;
            Invalidate();
        }

        public void SetSelfLockDisabledHint(string text)
        {
            _selfLockDisabledHint = text;
            Invalidate();
        }

        public void SetMainPressure(string value)
        {
            _mainPressure = value;
            Invalidate();
        }

        public void SetSolenoidRatio(int branch, string value)
        {
            if (branch is >= 1 and <= 4)
            {
                _ratios[branch - 1] = value;
                if (value.StartsWith("设置中", StringComparison.Ordinal))
                {
                    Invalidate();
                    return;
                }
                var numeric = value.Trim().TrimEnd('%');
                var parsed = double.TryParse(
                        numeric,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var percent);
                _solenoidTargetPercent[branch - 1] = parsed
                    ? (float)Math.Clamp(percent, 0, 100)
                    : 0f;
                _branchFlowEnabled[branch - 1] =
                    _solenoidTargetPercent[branch - 1] > 0;
                UpdateValveAnimationTimer();
                Invalidate();
            }
        }

        public void SetSolenoidValues(
            int branch,
            bool automatic,
            double? uploadedDuty,
            double? settingDuty,
            uint? pressureTarget)
        {
            if (branch is < 1 or > 4)
            {
                return;
            }

            var index = branch - 1;
            _solenoidAutoModes[index] = automatic;
            _uploadedDutyTexts[index] =
                automatic
                    ? $"上传 {(uploadedDuty.HasValue ? $"{uploadedDuty.Value:0.0}%" : "--")}"
                    : "上传 --";
            _settingDutyTexts[index] = automatic
                ? "设定 --"
                : $"设定 {(settingDuty.HasValue ? $"{settingDuty.Value:0.0}%" : "--")}";
            _pressureTargetTexts[index] = automatic
                ? $"目标 {(pressureTarget.HasValue ? pressureTarget.Value.ToString() : "--")}"
                : "目标 --";

            var displayedDuty = automatic ? uploadedDuty : settingDuty;
            _ratios[index] = displayedDuty.HasValue
                ? $"{displayedDuty.Value:0.0}%"
                : "--%";
            _branchFlowEnabled[index] =
                displayedDuty.HasValue &&
                displayedDuty.Value > 0;
            _inlineDutyInputs[index].Value = (decimal)(settingDuty ?? 0);
            _inlineTargetInputs[index].Value = pressureTarget ?? 0;
            LayoutInlineEditors();
            Invalidate();
        }

        private void UpdateValveAnimationTimer()
        {
            var selfLockMoving = Math.Abs(_selfLockTargetOpen - _selfLockVisualOpen) > 0.01f;
            var solenoidMoving = Enumerable.Range(0, 4).Any(index =>
                Math.Abs(_solenoidTargetPercent[index] - _solenoidVisualPercent[index]) > 0.05f);
            var shouldRun = Visible &&
                            SupplySchematicControl.AnimationEnabled &&
                            (_selfLockTargetOpen > 0.01f ||
                             selfLockMoving ||
                             _solenoidTargetPercent.Any(value => value > 0.01f) ||
                             solenoidMoving);
            _valveAnimationTimer.Enabled = shouldRun;
        }

        public void SetPressure(int sensor, string value)
        {
            if (sensor is >= 1 and <= 4)
            {
                _pressures[sensor - 1] = value;
                Invalidate();
            }
        }

        public void SetTemperature(int branch, string value)
        {
            if (branch is >= 1 and <= 4)
            {
                _temperatures[branch - 1] = value;
                Invalidate();
            }
        }

        public void SetPtcState(int ptc, bool on, bool automatic)
        {
            if (ptc is >= 1 and <= 4)
            {
                _ptcOnStates[ptc - 1] = on;
                _ptcAutoModes[ptc - 1] = automatic;
                Invalidate();
            }
        }

        public void FlashBranch(int branch)
        {
            _flashBranch = branch;
            Invalidate();
            _flashTimer.Stop();
            _flashTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(AppTheme.Field);
            UpdateViewTransform();
            if (_scale <= 0)
            {
                return;
            }
            graphics.TranslateTransform(_offsetX, _offsetY);
            graphics.ScaleTransform(_scale, _scale);

            DrawBackgroundGrid(graphics);
            DrawPipeNetwork(graphics);
            DrawMainComponents(graphics);
            for (var branch = 0; branch < 4; branch++)
            {
                DrawBranch(graphics, branch);
            }
            DrawResponseLogPanel(graphics);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var designPoint = ToDesignPoint(e.Location);
            if (ResponseFilterRect.Contains(designPoint))
            {
                Cursor = Cursors.Hand;
                return;
            }
            Cursor = (_selfLockControlEnabled &&
                      _canControl &&
                      (SelfLockOpenRect.Contains(designPoint) ||
                       SelfLockCloseRect.Contains(designPoint))) ||
                     (_canControl &&
                      (GetBranchAt(designPoint).HasValue ||
                       IsParameterControlAt(designPoint)))
                ? Cursors.Hand
                : Cursors.Default;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_canControl && e.Button == MouseButtons.Left)
            {
                var designPoint = ToDesignPoint(e.Location);
                if (ResponseFilterRect.Contains(designPoint))
                {
                    ShowResponseFilterMenu();
                    return;
                }
                if (TryHandleSelfLockClick(designPoint))
                {
                    return;
                }
                if (TryHandleParameterClick(designPoint))
                {
                    return;
                }

                var branch = GetBranchAt(designPoint);
                if (branch.HasValue)
                {
                    BranchClicked?.Invoke(branch.Value);
                }
            }
        }

        private bool TryHandleSelfLockClick(Point designPoint)
        {
            if (!_selfLockControlEnabled ||
                !_canControl ||
                (!SelfLockOpenRect.Contains(designPoint) &&
                 !SelfLockCloseRect.Contains(designPoint)))
            {
                return false;
            }

            if (SelfLockOpenRect.Contains(designPoint) &&
                _selfLockState != "开启")
            {
                SelfLockCommandRequested?.Invoke(true);
            }
            else if (SelfLockCloseRect.Contains(designPoint) &&
                     _selfLockState != "关闭")
            {
                SelfLockCommandRequested?.Invoke(false);
            }
            return true;
        }

        private bool IsParameterControlAt(Point designPoint)
        {
            for (var index = 0; index < 4; index++)
            {
                if (GetSolenoidManualRect(index).Contains(designPoint) ||
                    GetSolenoidAutoRect(index).Contains(designPoint) ||
                    GetPtcManualRect(index).Contains(designPoint) ||
                    GetPtcAutoRect(index).Contains(designPoint) ||
                    ((!_ptcAutoModes[index]) &&
                     (GetPtcOnRect(index).Contains(designPoint) ||
                      GetPtcOffRect(index).Contains(designPoint))))
                {
                    return true;
                }
            }
            return false;
        }

        private bool TryHandleParameterClick(Point designPoint)
        {
            for (var index = 0; index < 4; index++)
            {
                if (GetSolenoidManualRect(index).Contains(designPoint))
                {
                    SolenoidModeRequested?.Invoke(index + 1, false);
                    return true;
                }
                if (GetSolenoidAutoRect(index).Contains(designPoint))
                {
                    SolenoidModeRequested?.Invoke(index + 1, true);
                    return true;
                }
                if (GetPtcManualRect(index).Contains(designPoint))
                {
                    PtcModeRequested?.Invoke(index, false);
                    return true;
                }
                if (GetPtcAutoRect(index).Contains(designPoint))
                {
                    PtcModeRequested?.Invoke(index, true);
                    return true;
                }
                if (!_ptcAutoModes[index] &&
                    GetPtcOnRect(index).Contains(designPoint))
                {
                    PtcSwitchRequested?.Invoke(index, true);
                    return true;
                }
                if (!_ptcAutoModes[index] &&
                    GetPtcOffRect(index).Contains(designPoint))
                {
                    PtcSwitchRequested?.Invoke(index, false);
                    return true;
                }
            }
            return false;
        }

        private static void DrawBackgroundGrid(Graphics graphics)
        {
            using var gridPen = new Pen(
                Color.FromArgb(AppTheme.IsLight ? 18 : 26, AppTheme.Border),
                1)
            {
                DashStyle = DashStyle.Dot
            };
            for (var x = 30; x < 1780; x += 40)
            {
                graphics.DrawLine(gridPen, x, 25, x, 745);
            }
            for (var y = 35; y < 760; y += 40)
            {
                graphics.DrawLine(gridPen, 20, y, 1780, y);
            }
        }

        private void DrawPipeNetwork(Graphics graphics)
        {
            using var path = new GraphicsPath { FillMode = FillMode.Winding };
            path.AddLine(300, MainCenterY, 940, MainCenterY);
            path.AddLine(940, MainCenterY, 940, 660);
            foreach (var center in BranchCenters)
            {
                path.StartFigure();
                path.AddLine(940, center, 1760, center);
            }

            var activePipeColor = _gasFlowEnabled ? PipeColor : AppTheme.Muted;
            var activeOutline = _gasFlowEnabled ? PipeOutline : AppTheme.Border;
            using var outlinePen = new Pen(activeOutline, 29)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var pipePen = new Pen(activePipeColor, 18)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawPath(outlinePen, path);
            graphics.DrawPath(pipePen, path);

            if (!_gasFlowEnabled)
            {
                return;
            }
            using var upstreamFlowPath = new GraphicsPath { FillMode = FillMode.Winding };
            upstreamFlowPath.AddLine(300, MainCenterY, 940, MainCenterY);
            upstreamFlowPath.AddLine(940, MainCenterY, 940, 660);
            foreach (var center in BranchCenters)
            {
                upstreamFlowPath.StartFigure();
                upstreamFlowPath.AddLine(940, center, 1055, center);
            }
            using var flowPen = new Pen(
                Color.FromArgb(AppTheme.IsLight ? 130 : 175, 255, 255, 255),
                5f)
            {
                DashStyle = DashStyle.Custom,
                DashPattern = [1.4f, 6.2f],
                DashOffset = -_flowPhase,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawPath(flowPen, upstreamFlowPath);
            for (var index = 0; index < 4; index++)
            {
                if (!_branchFlowEnabled[index])
                {
                    continue;
                }
                using var downstreamFlowPath = new GraphicsPath();
                downstreamFlowPath.AddLine(
                    1195,
                    BranchCenters[index],
                    1760,
                    BranchCenters[index]);
                graphics.DrawPath(flowPen, downstreamFlowPath);
            }
        }

        private void DrawMainComponents(Graphics graphics)
        {
            DrawTank(graphics);
            DrawSelfLockValve(graphics);
            DrawPressureReducer(graphics);
            DrawPressureSensor(
                graphics,
                new Rectangle(710, 94, 88, 112),
                "主气压",
                _mainPressure);
            DrawConnector(graphics, 342, MainCenterY);
            DrawConnector(graphics, 360, MainCenterY);
            DrawConnector(graphics, 500, MainCenterY);
            DrawConnector(graphics, 566, MainCenterY);
            DrawConnector(graphics, 644, MainCenterY);
            DrawConnector(graphics, 702, MainCenterY);
            DrawConnector(graphics, 804, MainCenterY);
            DrawConnector(graphics, 823, MainCenterY, vertical: true);
            if (_gasFlowEnabled)
            {
                DrawArrow(graphics, new Point(330, MainCenterY - 10));
                DrawArrow(graphics, new Point(490, MainCenterY - 10));
                DrawArrow(graphics, new Point(558, MainCenterY - 10));
                DrawArrow(graphics, new Point(636, MainCenterY - 10));
                DrawArrow(graphics, new Point(830, MainCenterY - 10));
            }
        }

        private void DrawTank(Graphics graphics)
        {
            var body = new Rectangle(42, 78, 268, 144);
            using var bodyPath = RoundedRectangle(body, 55);
            var shadow = body;
            shadow.Offset(0, 5);
            using var shadowPath = RoundedRectangle(shadow, 55);
            using var shadowBrush = new SolidBrush(Color.FromArgb(55, 0, 0, 0));
            graphics.FillPath(shadowBrush, shadowPath);
            using var gradient = new LinearGradientBrush(
                body,
                Color.FromArgb(14, 165, 233),
                Color.FromArgb(2, 132, 199),
                LinearGradientMode.Vertical);
            using var borderPen = new Pen(AppTheme.Border, 2);
            graphics.FillPath(gradient, bodyPath);
            graphics.DrawPath(borderPen, bodyPath);
            using var neckBrush = new SolidBrush(PipeColor);
            graphics.FillRectangle(neckBrush, 310, 138, 35, 24);
            DrawConnector(graphics, 337, MainCenterY);
            DrawText(graphics, "储气罐", body, _fontTank, Color.White, true);

            using var legBrush = new SolidBrush(AppTheme.Card);
            graphics.FillRectangle(legBrush, 85, 220, 20, 25);
            graphics.FillRectangle(legBrush, 245, 220, 20, 25);
            graphics.DrawRectangle(borderPen, 85, 220, 20, 25);
            graphics.DrawRectangle(borderPen, 245, 220, 20, 25);
        }

        private void DrawSelfLockValve(Graphics graphics)
        {
            var body = new Rectangle(360, 96, 140, 108);
            DrawRoundedCard(graphics, body, AppTheme.Card, 8);
            DrawSelfLockAnimation(graphics, body);
            using var statusBrush = new SolidBrush(
                _selfLockState == "开启" ? AppTheme.Success : AppTheme.Muted);
            graphics.FillEllipse(statusBrush, body.Right - 22, body.Top + 10, 10, 10);
            DrawText(
                graphics,
                "自锁阀",
                new Rectangle(body.X, body.Bottom - 34, body.Width, 28),
                _fontBold,
                AppTheme.Text,
                true);
            DrawText(
                graphics,
                $"开关：{_selfLockState}",
                new Rectangle(body.X - 20, body.Bottom + 6, body.Width + 40, 28),
                _fontSmall,
                _selfLockState == "开启" ? AppTheme.Success : AppTheme.Muted,
                true);
            DrawSelfLockButton(
                graphics,
                SelfLockOpenRect,
                "开启",
                _selfLockState == "开启",
                AppTheme.Success);
            DrawSelfLockButton(
                graphics,
                SelfLockCloseRect,
                "关闭",
                _selfLockState == "关闭",
                AppTheme.Danger);

            if (!_selfLockControlEnabled)
            {
                DrawText(
                    graphics,
                    _selfLockState == "等待应答"
                        ? "等待应答…"
                        : _selfLockDisabledHint,
                    new Rectangle(332, 282, 200, 22),
                    _fontSmall,
                    AppTheme.Muted,
                    true);
            }
        }

        private void DrawSelfLockAnimation(Graphics graphics, Rectangle body)
        {
            var energized = _selfLockVisualOpen > 0.08f;
            var coilColor = energized ? AppTheme.Success : AppTheme.Muted;
            var coilRect = new Rectangle(body.Left + 12, body.Top + 17, 34, 23);
            using var coilPen = new Pen(coilColor, 2f);
            for (var index = 0; index < 5; index++)
            {
                graphics.DrawEllipse(
                    coilPen,
                    coilRect.Left + index * 6,
                    coilRect.Top,
                    11,
                    coilRect.Height);
            }
            if (energized)
            {
                var dotX = coilRect.Left + (int)(_valveAnimationPhase * coilRect.Width);
                using var currentBrush = new SolidBrush(AppTheme.Success);
                graphics.FillEllipse(currentBrush, dotX, coilRect.Top + 8, 5, 5);
                using var fieldPen = new Pen(
                    Color.FromArgb(120, AppTheme.Success),
                    1.2f)
                {
                    DashStyle = DashStyle.Dot
                };
                graphics.DrawArc(fieldPen, body.Left + 49, body.Top + 42, 55, 42, 195, 150);
            }

            var channelY = body.Top + 58;
            var gapLeft = body.Left + 63;
            var gapRight = body.Left + 87;
            using var pipeOutline = new Pen(AppTheme.Border, 7f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var pipeInner = new Pen(
                energized ? AppTheme.Accent : AppTheme.Muted,
                3.5f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(pipeOutline, body.Left + 8, channelY, gapLeft, channelY);
            graphics.DrawLine(pipeOutline, gapRight, channelY, body.Right - 8, channelY);
            graphics.DrawLine(pipeInner, body.Left + 8, channelY, gapLeft, channelY);
            graphics.DrawLine(pipeInner, gapRight, channelY, body.Right - 8, channelY);

            var bridgeAlpha = (int)(220 * _selfLockVisualOpen);
            if (bridgeAlpha > 12)
            {
                using var bridgePen = new Pen(
                    Color.FromArgb(bridgeAlpha, AppTheme.Success),
                    2f + 2f * _selfLockVisualOpen)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                graphics.DrawLine(bridgePen, gapLeft, channelY, gapRight, channelY);
            }

            var plungerLift = _selfLockVisualOpen * 13f;
            var plunger = new Rectangle(
                gapLeft + 5,
                channelY - 24 - (int)plungerLift,
                14,
                25);
            using var plungerBrush = new SolidBrush(AppTheme.Text);
            using var plungerPen = new Pen(
                energized ? AppTheme.Success : AppTheme.Border,
                1.5f);
            graphics.FillRectangle(plungerBrush, plunger);
            graphics.DrawRectangle(plungerPen, plunger);
            graphics.DrawLine(
                plungerPen,
                plunger.Left + plunger.Width / 2,
                plunger.Top,
                plunger.Left + plunger.Width / 2,
                plunger.Top - 8);

            using var seatPen = new Pen(AppTheme.Muted, 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(
                seatPen,
                gapLeft - 5,
                channelY - 9,
                gapLeft + 4,
                channelY);
            graphics.DrawLine(
                seatPen,
                gapRight + 5,
                channelY - 9,
                gapRight - 4,
                channelY);

            if (_gasFlowEnabled && _selfLockVisualOpen > 0.9f)
            {
                var flowX = gapLeft + (int)(_flowPhase % Math.Max(1, gapRight - gapLeft));
                using var flowBrush = new SolidBrush(Color.FromArgb(220, AppTheme.Success));
                graphics.FillEllipse(flowBrush, flowX, channelY - 3, 6, 6);
            }
        }

        private void DrawSelfLockButton(
            Graphics graphics,
            Rectangle rect,
            string text,
            bool active,
            Color activeColor)
        {
            var enabled = _selfLockControlEnabled && _canControl;
            var fillColor = active
                ? AppTheme.HighlightSurface
                : enabled
                    ? AppTheme.SecondaryButton
                    : AppTheme.Field;
            var borderColor = active ? activeColor : AppTheme.Border;
            DrawRoundedCard(
                graphics,
                rect,
                fillColor,
                8,
                borderColor,
                active ? 2f : 1f);

            var lampRect = new Rectangle(rect.Left + 12, rect.Top + rect.Height / 2 - 6, 12, 12);
            using var lampGlow = new SolidBrush(
                active
                    ? Color.FromArgb(65, activeColor)
                    : Color.Transparent);
            using var lamp = new SolidBrush(
                active
                    ? activeColor
                    : enabled
                        ? AppTheme.Muted
                        : AppTheme.Border);
            if (active)
            {
                graphics.FillEllipse(lampGlow, Rectangle.Inflate(lampRect, 5, 5));
            }
            graphics.FillEllipse(lamp, lampRect);
            using var lampOutline = new Pen(
                active ? activeColor : AppTheme.Border,
                1f);
            graphics.DrawEllipse(lampOutline, lampRect);

            DrawText(
                graphics,
                text,
                new Rectangle(rect.Left + 30, rect.Top, rect.Width - 34, rect.Height),
                _fontSmall,
                active
                    ? activeColor
                    : enabled
                        ? AppTheme.Text
                        : AppTheme.Muted,
                true);
        }

        private void DrawPressureReducer(Graphics graphics)
        {
            Point[] shape =
            [
                new(580, 131),
                new(630, 138),
                new(630, 162),
                new(580, 169)
            ];
            using var fill = new SolidBrush(AppTheme.Card);
            using var border = new Pen(AppTheme.Border, 2);
            graphics.FillPolygon(fill, shape);
            graphics.DrawPolygon(border, shape);
            DrawText(
                graphics,
                "一级减压阀",
                new Rectangle(550, 173, 110, 20),
                _fontCompact,
                AppTheme.Text,
                true);
        }

        private void DrawBranch(Graphics graphics, int index)
        {
            var centerY = BranchCenters[index];
            var branchTag = new Rectangle(955, centerY - 16, 72, 32);
            DrawPill(graphics, branchTag, AppTheme.HighlightSurface, AppTheme.Accent);
            DrawText(graphics, $"支路 {index + 1}", branchTag, _fontSmall, AppTheme.Accent, true);

            DrawSolenoidValve(graphics, index, GetBranchButton(index));
            DrawPressureSensor(
                graphics,
                GetPressureBody(index),
                $"支路 {index + 1}",
                _pressures[index]);
            var pressureBody = GetPressureBody(index);
            DrawText(
                graphics,
                _pressureTargetTexts[index],
                new Rectangle(
                    pressureBody.X - 8,
                    pressureBody.Bottom + 30,
                    pressureBody.Width + 16,
                    18),
                _fontCompact,
                _solenoidAutoModes[index] ? AppTheme.Accent : AppTheme.Muted,
                true);
            DrawBufferChamber(graphics, index, GetPtcBody(index));
            DrawRestrictor(graphics, index, GetRestrictorBody(index));

            DrawConnector(graphics, 1042, centerY);
            DrawConnector(graphics, 1055, centerY);
            DrawConnector(graphics, 1184, centerY);
            DrawConnector(graphics, 1198, centerY);
            DrawConnector(graphics, 1312, centerY);
            DrawConnector(graphics, 1402, centerY);
            DrawConnector(graphics, 1560, centerY);
            DrawConnector(graphics, 1638, centerY);
            if (_gasFlowEnabled)
            {
                DrawArrow(graphics, new Point(980, centerY - 10));
            }
            if (_gasFlowEnabled && _branchFlowEnabled[index])
            {
                DrawArrow(graphics, new Point(1200, centerY - 10));
                DrawArrow(graphics, new Point(1330, centerY - 10));
                DrawArrow(graphics, new Point(1608, centerY - 10));
            }
        }

        private void DrawSolenoidValve(Graphics graphics, int index, Rectangle body)
        {
            var active = _flashBranch == index + 1;
            var energized = _solenoidVisualPercent[index] > 0.1f ||
                            _solenoidTargetPercent[index] > 0.1f;
            var flowing = IsBranchFlowEnabled(index + 1);
            var background = active ? Color.FromArgb(34, 197, 94) : AppTheme.Card;
            DrawRoundedCard(
                graphics,
                body,
                background,
                10,
                flowing
                    ? AppTheme.Success
                    : energized
                        ? AppTheme.Accent
                        : AppTheme.Border,
                active || flowing ? 2.2f : 1.5f);
            DrawSolenoidAnimation(graphics, body, index, energized, flowing);

            DrawText(
                graphics,
                $"电磁阀 {index + 1}",
                new Rectangle(body.X + 4, body.Y + 33, body.Width - 8, 18),
                _fontCompact,
                active ? Color.White : AppTheme.Text,
                true);
            DrawText(
                graphics,
                $"比例 {_ratios[index]}",
                new Rectangle(body.X + 4, body.Y + 52, body.Width - 8, 17),
                _fontCompact,
                active
                    ? Color.White
                    : flowing
                        ? AppTheme.Success
                        : energized
                            ? AppTheme.Accent
                            : AppTheme.Muted,
                true);
            DrawSolenoidDetails(graphics, body, index);
        }

        private void DrawSolenoidDetails(Graphics graphics, Rectangle body, int index)
        {
            var manualRect = GetSolenoidManualRect(index);
            var autoRect = GetSolenoidAutoRect(index);
            DrawSmallButton(
                graphics,
                manualRect,
                "手动",
                !_solenoidAutoModes[index],
                AppTheme.Accent);
            DrawSmallButton(
                graphics,
                autoRect,
                "自动",
                _solenoidAutoModes[index],
                AppTheme.Accent);

            DrawText(
                graphics,
                _solenoidAutoModes[index]
                    ? $"{_uploadedDutyTexts[index]}  |  {_settingDutyTexts[index]}"
                    : _settingDutyTexts[index],
                new Rectangle(body.X - 10, body.Bottom + 54, body.Width + 20, 18),
                _fontSmall,
                AppTheme.Text,
                true);
        }

        private void DrawSolenoidAnimation(
            Graphics graphics,
            Rectangle body,
            int index,
            bool energized,
            bool flowing)
        {
            var percent = Math.Clamp(_solenoidVisualPercent[index], 0, 100);
            var coilColor = energized ? AppTheme.Accent : AppTheme.Muted;
            var coilRect = new Rectangle(body.X + 9, body.Y + 8, 27, 16);
            using var coilPen = new Pen(coilColor, 1.6f);
            for (var loop = 0; loop < 4; loop++)
            {
                graphics.DrawEllipse(
                    coilPen,
                    coilRect.Left + loop * 6,
                    coilRect.Top,
                    9,
                    coilRect.Height);
            }
            if (energized)
            {
                var currentX = coilRect.Left + (int)(_valveAnimationPhase * coilRect.Width);
                using var currentBrush = new SolidBrush(AppTheme.Accent);
                graphics.FillEllipse(currentBrush, currentX, coilRect.Top + 5, 4, 4);
            }

            var channelY = body.Y + 19;
            var channelLeft = body.X + 42;
            var channelRight = body.Right - 8;
            var gateLeft = body.X + 74;
            var gateRight = gateLeft + 16;
            using var outlinePen = new Pen(AppTheme.Border, 5f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var channelPen = new Pen(
                flowing ? AppTheme.Success : AppTheme.Muted,
                2.5f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(outlinePen, channelLeft, channelY, channelRight, channelY);
            graphics.DrawLine(channelPen, channelLeft, channelY, gateLeft, channelY);
            graphics.DrawLine(channelPen, gateRight, channelY, channelRight, channelY);

            var opening = 2 + percent / 100f * 10f;
            var topGateBottom = channelY - opening / 2f;
            var bottomGateTop = channelY + opening / 2f;
            using var gateBrush = new SolidBrush(
                energized ? AppTheme.Accent : AppTheme.Muted);
            graphics.FillRectangle(
                gateBrush,
                gateLeft,
                body.Y + 7,
                16,
                Math.Max(1, (int)(topGateBottom - body.Y - 7)));
            graphics.FillRectangle(
                gateBrush,
                gateLeft,
                (int)bottomGateTop,
                16,
                Math.Max(1, (int)(body.Y + 32 - bottomGateTop)));
            using var stemPen = new Pen(AppTheme.Text, 2f);
            graphics.DrawLine(
                stemPen,
                gateLeft + 8,
                body.Y + 5,
                gateLeft + 8,
                topGateBottom - 2);

            if (!flowing)
            {
                return;
            }

            using var particleBrush = new SolidBrush(AppTheme.Success);
            var pathLength = Math.Max(1, channelRight - channelLeft);
            for (var particle = 0; particle < 3; particle++)
            {
                var position = (int)(
                    channelLeft +
                    ((_valveAnimationPhase + particle / 3f) % 1f) * pathLength);
                if (position >= gateLeft && position <= gateRight && percent < 3)
                {
                    continue;
                }
                graphics.FillEllipse(
                    particleBrush,
                    position - 2,
                    channelY - 2,
                    4,
                    4);
            }
        }

        private void DrawSmallButton(
            Graphics graphics,
            Rectangle rect,
            string text,
            bool active,
            Color activeColor,
            bool enabled = true)
        {
            DrawRoundedCard(
                graphics,
                rect,
                enabled
                    ? active
                        ? activeColor
                        : AppTheme.SecondaryButton
                    : AppTheme.Field,
                6,
                enabled && active ? activeColor : AppTheme.Border,
                1f);
            DrawText(
                graphics,
                text,
                rect,
                _fontSmall,
                enabled
                    ? active
                        ? Color.FromArgb(7, 27, 43)
                        : AppTheme.Text
                    : AppTheme.Muted,
                true);
        }

        private static Rectangle GetSolenoidManualRect(int index)
        {
            var body = GetBranchButton(index);
            return new Rectangle(body.X, body.Bottom + 5, 44, 24);
        }

        private static Rectangle GetSolenoidAutoRect(int index)
        {
            var body = GetBranchButton(index);
            return new Rectangle(body.X + 48, body.Bottom + 5, 44, 24);
        }

        private static Rectangle GetPtcBody(int index)
        {
            return new Rectangle(
                1410,
                BranchCenters[index] - 48,
                126,
                96);
        }

        private static Rectangle GetPtcManualRect(int index)
        {
            var body = GetPtcBody(index);
            return new Rectangle(body.X, body.Bottom + 4, 46, 22);
        }

        private static Rectangle GetPtcAutoRect(int index)
        {
            var body = GetPtcBody(index);
            return new Rectangle(body.X + 48, body.Bottom + 4, 46, 22);
        }

        private static Rectangle GetPtcOnRect(int index)
        {
            var body = GetPtcBody(index);
            return new Rectangle(body.X + 96, body.Bottom + 4, 24, 22);
        }

        private static Rectangle GetPtcOffRect(int index)
        {
            var body = GetPtcBody(index);
            return new Rectangle(body.X + 120, body.Bottom + 4, 24, 22);
        }

        private void DrawPressureSensor(Graphics graphics, Rectangle body, string caption, string value)
        {
            DrawRoundedCard(graphics, body, AppTheme.Card, 10);
            var gauge = new Rectangle(body.X + body.Width / 2 - 16, body.Y + 7, 32, 32);
            using var gaugeBrush = new SolidBrush(AppTheme.HighlightSurface);
            graphics.FillEllipse(gaugeBrush, gauge);
            DrawText(graphics, "P", gauge, _fontSensor, AppTheme.Accent, true);
            DrawText(
                graphics,
                caption,
                new Rectangle(body.X + 4, body.Y + 40, body.Width - 8, 18),
                _fontCompact,
                AppTheme.Muted,
                true);
            DrawText(
                graphics,
                value,
                new Rectangle(body.X + 4, body.Y + 58, body.Width - 8, 23),
                _fontValue,
                AppTheme.Text,
                true);
        }

        private void DrawBufferChamber(Graphics graphics, int index, Rectangle body)
        {
            using var bodyPath = RoundedRectangle(body, 12);
            using var gradient = new LinearGradientBrush(
                body,
                WarmColor,
                ColdColor,
                LinearGradientMode.Vertical);
            using var border = new Pen(AppTheme.Border, 2);
            graphics.FillPath(gradient, bodyPath);
            graphics.DrawPath(border, bodyPath);
            using var heaterPen = new Pen(
                _ptcOnStates[index] ? HeatColor : AppTheme.Muted,
                8)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(heaterPen, body.Left + 6, body.Top - 10, body.Right - 6, body.Top - 10);
            DrawText(
                graphics,
                $"PTC {index + 1} · 缓冲腔 {index + 1}",
                new Rectangle(body.X + 3, body.Y + 28, body.Width - 6, 21),
                _fontCompact,
                AppTheme.Text,
                true);
            DrawText(
                graphics,
                _temperatures[index],
                new Rectangle(body.X + 4, body.Bottom - 27, body.Width - 8, 22),
                _fontValue,
                AppTheme.Danger,
                true);
            DrawText(
                graphics,
                "PT100",
                new Rectangle(body.Right + 4, body.Top + 8, 56, 22),
                _fontSmall,
                AppTheme.Muted);
            DrawSmallButton(
                graphics,
                GetPtcManualRect(index),
                "手动",
                !_ptcAutoModes[index],
                AppTheme.Accent,
                _canControl);
            DrawSmallButton(
                graphics,
                GetPtcAutoRect(index),
                "自动",
                _ptcAutoModes[index],
                AppTheme.Accent,
                _canControl);
            DrawSmallButton(
                graphics,
                GetPtcOnRect(index),
                "开",
                _ptcOnStates[index],
                AppTheme.Success,
                _canControl && !_ptcAutoModes[index]);
            DrawSmallButton(
                graphics,
                GetPtcOffRect(index),
                "关",
                !_ptcOnStates[index],
                AppTheme.Danger,
                _canControl && !_ptcAutoModes[index]);
            DrawText(
                graphics,
                $"PTC状态：{(_ptcAutoModes[index] ? "自动" : "手动")} · " +
                $"{(_ptcOnStates[index] ? "开" : "关")}",
                new Rectangle(body.X - 10, body.Bottom + 30, body.Width + 20, 20),
                _fontSmall,
                _ptcOnStates[index] ? AppTheme.Success : AppTheme.Muted,
                true);
        }

        private void DrawRestrictor(Graphics graphics, int index, Rectangle body)
        {
            var centerX = body.Left + body.Width / 2;
            var centerY = body.Top + body.Height / 2;
            using var pipeOutline = new Pen(AppTheme.Border, 9f);
            using var pipeInner = new Pen(AppTheme.Card, 4f);
            graphics.DrawLine(pipeOutline, body.Left - 8, centerY, body.Right + 8, centerY);
            graphics.DrawLine(pipeInner, body.Left - 8, centerY, body.Right + 8, centerY);

            Point[] valveBody =
            [
                new(centerX - 42, body.Top),
                new(centerX + 42, body.Top),
                new(centerX + 13, centerY - 12),
                new(centerX + 13, centerY - 5),
                new(centerX + 28, centerY - 5),
                new(centerX + 28, centerY + 5),
                new(centerX + 13, centerY + 5),
                new(centerX + 13, centerY + 12),
                new(centerX + 42, body.Bottom),
                new(centerX - 42, body.Bottom),
                new(centerX - 13, centerY + 12),
                new(centerX - 13, centerY + 5),
                new(centerX - 28, centerY + 5),
                new(centerX - 28, centerY - 5),
                new(centerX - 13, centerY - 5),
                new(centerX - 13, centerY - 12)
            ];
            using var fill = new SolidBrush(PipeColor);
            using var border = new Pen(AppTheme.Border, 2);
            graphics.FillPolygon(fill, valveBody);
            graphics.DrawPolygon(border, valveBody);

            using var seatBrush = new SolidBrush(AppTheme.Card);
            graphics.FillRectangle(seatBrush, centerX - 15, body.Top + 7, 6, body.Height - 14);
            graphics.FillRectangle(seatBrush, centerX + 9, body.Top + 7, 6, body.Height - 14);
            graphics.DrawLine(
                border,
                centerX - 16,
                centerY - 7,
                centerX + 16,
                centerY - 7);
            graphics.DrawLine(
                border,
                centerX - 16,
                centerY + 7,
                centerX + 16,
                centerY + 7);
            DrawText(
                graphics,
                $"节流器 {index + 1}",
                new Rectangle(body.X - 12, body.Bottom + 7, body.Width + 24, 26),
                _fontValue,
                AppTheme.Text,
                true);
        }

        private static void DrawConnector(
            Graphics graphics,
            int position,
            int center,
            bool vertical = false)
        {
            var rect = vertical
                ? new Rectangle(position - 17, center - 5, 34, 10)
                : new Rectangle(position - 5, center - 16, 10, 32);
            using var fill = new SolidBrush(AppTheme.SecondaryButton);
            using var border = new Pen(AppTheme.Border, 1.5f);
            using var path = RoundedRectangle(rect, 4);
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
        }

        private static void DrawArrow(Graphics graphics, Point tip)
        {
            Point[] arrow =
            [
                tip,
                new Point(tip.X - 18, tip.Y - 2),
                new Point(tip.X - 9, tip.Y - 11),
                new Point(tip.X - 9, tip.Y + 7)
            ];
            using var brush = new SolidBrush(Color.FromArgb(225, 255, 255, 255));
            graphics.FillPolygon(brush, arrow);
        }

        private static void DrawRoundedCard(
            Graphics graphics,
            Rectangle rect,
            Color fillColor,
            int radius,
            Color? borderColor = null,
            float borderWidth = 1.5f)
        {
            var shadowRect = rect;
            shadowRect.Offset(0, 3);
            using var shadowPath = RoundedRectangle(shadowRect, radius);
            using var shadow = new SolidBrush(Color.FromArgb(
                AppTheme.IsLight ? 24 : 42,
                0,
                0,
                0));
            graphics.FillPath(shadow, shadowPath);
            using var path = RoundedRectangle(rect, radius);
            using var fill = new SolidBrush(fillColor);
            using var border = new Pen(borderColor ?? AppTheme.Border, borderWidth);
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
        }

        private static void DrawPill(
            Graphics graphics,
            Rectangle rect,
            Color fillColor,
            Color borderColor)
        {
            DrawRoundedCard(graphics, rect, fillColor, rect.Height / 2, borderColor, 1f);
        }

        private void DrawResponseLogPanel(Graphics graphics)
        {
            DrawRoundedCard(
                graphics,
                ResponseLogRect,
                AppTheme.HighlightSurface,
                8,
                AppTheme.Border,
                1f);
            DrawText(
                graphics,
                "上传应答日志",
                new Rectangle(
                    ResponseLogRect.Left + 12,
                    ResponseLogRect.Top + 3,
                    160,
                    22),
                _fontSmall,
                AppTheme.Text);
            DrawRoundedCard(
                graphics,
                ResponseFilterRect,
                AppTheme.SecondaryButton,
                6,
                AppTheme.Border,
                1f);
            DrawText(
                graphics,
                "筛选 ▼",
                ResponseFilterRect,
                _fontSmall,
                AppTheme.Accent,
                true);
            var entries = _responseLog
                .Where(entry =>
                    _responseLogFilters.TryGetValue(entry.Type, out var visible) &&
                    visible)
                .TakeLast(3)
                .ToArray();
            if (entries.Length == 0)
            {
                DrawText(
                    graphics,
                    "暂无符合条件的应答",
                    new Rectangle(
                        ResponseLogRect.Left + 14,
                        ResponseLogRect.Top + 29,
                        ResponseLogRect.Width - 28,
                        18),
                    _fontCompact,
                    AppTheme.Muted);
                return;
            }

            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                DrawText(
                    graphics,
                    $"{entry.Timestamp:HH:mm:ss.fff}  {entry.Message}",
                    new Rectangle(
                        ResponseLogRect.Left + 14,
                        ResponseLogRect.Top + 27 + index * 16,
                        ResponseLogRect.Width - 28,
                        16),
                    _fontCompact,
                    ResponseLogColor(entry.Type));
            }
        }

        private static Color ResponseLogColor(ResponseLogType type) => type switch
        {
            ResponseLogType.Ack => AppTheme.Success,
            ResponseLogType.Nak => AppTheme.Danger,
            ResponseLogType.HeartbeatAck => AppTheme.Accent,
            ResponseLogType.StatusUpload => Color.FromArgb(250, 204, 21),
            _ => AppTheme.Text
        };

        private void ShowResponseFilterMenu()
        {
            _responseFilterMenu?.Dispose();
            _responseFilterMenu = new ContextMenuStrip
            {
                BackColor = AppTheme.MenuBackground,
                ForeColor = AppTheme.Text,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            foreach (var type in Enum.GetValues<ResponseLogType>())
            {
                var item = new ToolStripMenuItem(ResponseLogTypeName(type))
                {
                    Checked = _responseLogFilters[type],
                    CheckOnClick = true,
                    BackColor = AppTheme.MenuBackground,
                    ForeColor = AppTheme.Text
                };
                item.Click += (_, _) =>
                {
                    _responseLogFilters[type] = item.Checked;
                    Invalidate();
                };
                _responseFilterMenu.Items.Add(item);
            }
            var location = new Point(
                (int)(_offsetX + ResponseFilterRect.Left * _scale),
                (int)(_offsetY + ResponseFilterRect.Bottom * _scale));
            _responseFilterMenu.Show(this, location);
        }

        private static string ResponseLogTypeName(ResponseLogType type) => type switch
        {
            ResponseLogType.ConnectionSn => "SN 连接应答",
            ResponseLogType.HeartbeatAck => "心跳应答",
            ResponseLogType.Ack => "ACK 应答",
            ResponseLogType.Nak => "NAK 应答",
            ResponseLogType.StatusUpload => "状态上报",
            ResponseLogType.PressureReport => "压力传感器上报",
            ResponseLogType.TemperatureReport => "温度传感器上报",
            _ => "其他接收帧"
        };

        private static void DrawText(
            Graphics graphics,
            string text,
            Rectangle rect,
            Font font,
            Color? color = null,
            bool centered = false)
        {
            using var brush = new SolidBrush(color ?? AppTheme.Text);
            using var format = new StringFormat
            {
                Alignment = centered ? StringAlignment.Center : StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            };
            graphics.DrawString(text, font, brush, rect, format);
        }

        private Point ToDesignPoint(Point point)
        {
            if (_scale <= 0)
            {
                return point;
            }
            return new Point(
                (int)((point.X - _offsetX) / _scale),
                (int)((point.Y - _offsetY) / _scale));
        }

        private static int? GetBranchAt(Point location)
        {
            for (var index = 0; index < BranchCenters.Length; index++)
            {
                if (GetBranchButton(index).Contains(location))
                {
                    return index + 1;
                }
            }
            return null;
        }

        private static Rectangle GetBranchButton(int index)
        {
            return new Rectangle(1058, BranchCenters[index] - 40, 126, 80);
        }

        private static Rectangle GetPressureBody(int index)
        {
            return new Rectangle(1210, BranchCenters[index] - 44, 90, 88);
        }

        private static Rectangle GetRestrictorBody(int index)
        {
            return new Rectangle(1648, BranchCenters[index] - 36, 102, 72);
        }

        private static GraphicsPath RoundedRectangle(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _flashTimer.Dispose();
                _flowTimer.Dispose();
                _valveAnimationTimer.Dispose();
                _responseFilterMenu?.Dispose();
                _fontSmall.Dispose();
                _fontNormal.Dispose();
                _fontBold.Dispose();
                _fontSensor.Dispose();
                _fontTank.Dispose();
            }
            base.Dispose(disposing);
        }
    }

}
