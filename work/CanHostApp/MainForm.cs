using CanHost.Adapters;
using CanHost.Controls;
using CanHost.Docking;
using CanHost.Models;
using CanHost.Modules;

namespace CanHost;

public sealed class MainForm : Form
{
    private static readonly (string Id, string Title)[] PlotDefinitions =
    [
        ("plot-main-pressure", "主气压传感器"),
        ("plot-branch1-pressure", "支路 1 气压传感器"),
        ("plot-branch2-pressure", "支路 2 气压传感器"),
        ("plot-branch3-pressure", "支路 3 气压传感器"),
        ("plot-branch4-pressure", "支路 4 气压传感器")
    ];

    private static readonly Color BackgroundColor = Color.FromArgb(12, 18, 32);
    private static readonly Color FieldColor = Color.FromArgb(14, 23, 39);
    private static readonly Color BorderColor = Color.FromArgb(48, 64, 88);
    private static readonly Color TextColor = Color.FromArgb(229, 237, 247);
    private static readonly Color MutedColor = Color.FromArgb(139, 155, 179);
    private static readonly Color AccentColor = Color.FromArgb(56, 189, 248);
    private static readonly Color SuccessColor = Color.FromArgb(34, 197, 94);
    private static readonly Color DangerColor = Color.FromArgb(248, 113, 113);

    private readonly ReceiveFramesModule _receiveModule = new();
    private readonly FrameTickerControl _tickerModule = new();
    private readonly PlotModule[] _plotModules;
    private readonly PressurePlotWindowManager _pressurePlotWindowManager;
    private readonly SendFramesModule _sendModule = new();
    private readonly SupplyControlModule _supplyControlModule = new();
    private readonly ConnectionDockPanel _connectionPanel = new();
    private readonly DockableWorkspace _workspace = new();
    private readonly DockableModuleHost _receiveHost;
    private readonly DockableModuleHost _tickerHost;
    private readonly DockableModuleHost _sendHost;
    private readonly DockableModuleHost _supplyControlHost;
    private readonly Label _statusLabel = new();
    private readonly Label _connectionLabel = new();

    private ToolStripMenuItem? _receiveExpandedItem;
    private ToolStripMenuItem? _receiveTickerItem;
    private ToolStripMenuItem? _receiveCollapsedItem;
    private ToolStripMenuItem? _showSendItem;
    private ToolStripMenuItem? _showSupplyControlItem;
    private ToolStripMenuItem? _darkThemeItem;
    private ToolStripMenuItem? _lightThemeItem;
    private ICanAdapter? _adapter;
    private ConnectionOptions _connectionOptions;
    private DisplayState _displayState = DisplayStateStore.Load();
    private string _connectedSummary = string.Empty;
    private ReceiveDisplayMode _receiveDisplayMode = ReceiveDisplayMode.Expanded;
    private bool _isClosing;

    public MainForm()
    {
        _receiveDisplayMode = _displayState.ReceiveTicker
            ? ReceiveDisplayMode.Ticker
            : _displayState.ReceiveExpanded
                ? ReceiveDisplayMode.Expanded
                : ReceiveDisplayMode.Collapsed;
        _plotModules = PlotDefinitions
            .Select(definition => new PlotModule(definition.Title))
            .ToArray();
        _pressurePlotWindowManager = new PressurePlotWindowManager(_plotModules);
        _receiveHost = new DockableModuleHost("receive", "接收帧", _receiveModule);
        _tickerHost = new DockableModuleHost("ticker", "单条信息", _tickerModule);
        _sendHost = new DockableModuleHost("send", "发送帧", _sendModule);
        _supplyControlHost = new DockableModuleHost(
            "supply-control",
            "储供控制",
            _supplyControlModule);
        _supplyControlHost.PreventDocking = true;
        _receiveHost.PreventDocking = true;
        _tickerHost.PreventDocking = true;
        _sendHost.PreventDocking = true;
        _supplyControlHost.ShowModuleHeader = false;
        _receiveHost.ShowModuleHeader = false;
        _tickerHost.ShowModuleHeader = false;
        _sendHost.ShowModuleHeader = false;
        _receiveHost.MinimumSize = new Size(660, 460);
        _tickerHost.MinimumSize = new Size(300, 80);
        _sendHost.MinimumSize = new Size(620, 245);
        _supplyControlHost.MinimumSize = new Size(620, 540);
        _connectionOptions = ConnectionOptions.CreateDefault(VciAdapter.FindControlCanDll());

        Text = AppInfo.DisplayName;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1220, 760);
        Size = new Size(1260, 860);
        BackColor = BackgroundColor;
        ForeColor = TextColor;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        DoubleBuffered = true;

        BuildInterface();
        BindEvents();
        SetConnectedState(null);
        ThemeApplicator.Apply(this);
        Shown += (_, _) =>
        {
            _workspace.InitializeLayout();
            ApplyModuleVisibility();
        };
    }

    private void BuildInterface()
    {
        var menu = BuildMenuBar();
        var root = new Panel { Dock = DockStyle.Fill };
        var body = new Panel { Dock = DockStyle.Fill };
        var workspace = BuildWorkspace();
        var statusBar = BuildStatusBar();

        body.Controls.Add(workspace);
        body.Controls.Add(_connectionPanel);
        body.Controls.Add(statusBar);
        root.Controls.Add(body);
        root.Controls.Add(menu);
        Controls.Add(root);
    }

    private MenuStrip BuildMenuBar()
    {
        var menu = new MenuStrip
        {
            Dock = DockStyle.Top,
            BackColor = Color.FromArgb(10, 16, 28),
            ForeColor = TextColor,
            Font = new Font("Microsoft YaHei UI", 9.2F),
            Padding = new Padding(14, 3, 0, 3),
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColorTable())
        };

        var displayMenu = new ToolStripMenuItem("显示") { ForeColor = TextColor };
        var receiveMenu = new ToolStripMenuItem("接收帧显示")
        {
            ForeColor = TextColor,
            BackColor = FieldColor
        };
        _receiveExpandedItem = ReceiveModeItem("全展开", ReceiveDisplayMode.Expanded);
        _receiveTickerItem = ReceiveModeItem("单条滚动", ReceiveDisplayMode.Ticker);
        _receiveCollapsedItem = ReceiveModeItem("完全折叠", ReceiveDisplayMode.Collapsed);
        receiveMenu.DropDownItems.AddRange(
        [
            _receiveExpandedItem,
            _receiveTickerItem,
            _receiveCollapsedItem
        ]);

        _showSupplyControlItem = VisibilityItem("储供控制", _displayState.SupplyControlVisible);
        _showSendItem = VisibilityItem("发送帧", _displayState.SendVisible);
        var restoreLayoutItem = new ToolStripMenuItem("恢复默认布局")
        {
            ForeColor = TextColor,
            BackColor = FieldColor
        };
        restoreLayoutItem.Click += (_, _) => RestoreLayout();
        displayMenu.DropDownItems.Add(receiveMenu);
        displayMenu.DropDownItems.Add(_showSupplyControlItem);
        displayMenu.DropDownItems.Add(_showSendItem);
        displayMenu.DropDownItems.Add(new ToolStripSeparator());
        displayMenu.DropDownItems.Add(restoreLayoutItem);

        var helpMenu = new ToolStripMenuItem("帮助") { ForeColor = TextColor };
        var aboutItem = new ToolStripMenuItem("关于")
        {
            ForeColor = TextColor,
            BackColor = FieldColor
        };
        aboutItem.Click += (_, _) =>
        {
            using var dialog = new AboutForm();
            dialog.ShowDialog(this);
        };
        helpMenu.DropDownItems.Add(aboutItem);

        var settingsMenu = new ToolStripMenuItem("设置") { ForeColor = TextColor };
        var themeMenu = new ToolStripMenuItem("主题")
        {
            ForeColor = TextColor,
            BackColor = FieldColor
        };
        _darkThemeItem = new ToolStripMenuItem("深色")
        {
            ForeColor = TextColor,
            BackColor = FieldColor,
            Checked = AppTheme.Mode == AppThemeMode.Dark
        };
        _lightThemeItem = new ToolStripMenuItem("浅色")
        {
            ForeColor = TextColor,
            BackColor = FieldColor,
            Checked = AppTheme.Mode == AppThemeMode.Light
        };
        _darkThemeItem.Click += (_, _) => ChangeTheme(AppThemeMode.Dark);
        _lightThemeItem.Click += (_, _) => ChangeTheme(AppThemeMode.Light);
        themeMenu.DropDownItems.AddRange([_darkThemeItem, _lightThemeItem]);
        settingsMenu.DropDownItems.Add(themeMenu);

        menu.Items.AddRange([displayMenu, settingsMenu, helpMenu]);
        MainMenuStrip = menu;
        return menu;
    }

    private void ChangeTheme(AppThemeMode mode)
    {
        AppTheme.SetTheme(mode);
        if (_darkThemeItem is not null)
        {
            _darkThemeItem.Checked = mode == AppThemeMode.Dark;
        }
        if (_lightThemeItem is not null)
        {
            _lightThemeItem.Checked = mode == AppThemeMode.Light;
        }
    }

    private ToolStripMenuItem ReceiveModeItem(string text, ReceiveDisplayMode mode)
    {
        var item = new ToolStripMenuItem(text)
        {
            Checked = _receiveDisplayMode == mode,
            ForeColor = TextColor,
            BackColor = FieldColor
        };
        item.Click += (_, _) => SetReceiveDisplayMode(mode);
        return item;
    }

    private ToolStripMenuItem VisibilityItem(string text, bool isChecked)
    {
        var item = new ToolStripMenuItem(text)
        {
            CheckOnClick = true,
            Checked = isChecked,
            ForeColor = TextColor,
            BackColor = FieldColor
        };
        item.Click += (_, _) => ApplyModuleVisibility(item);
        return item;
    }

    private Control BuildWorkspace()
    {
        var workspace = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            Padding = new Padding(20, 16, 20, 16)
        };

        _workspace.AddModule(_receiveHost, Rectangle.Empty);
        _workspace.AddModule(_tickerHost, Rectangle.Empty);
        _workspace.AddModule(_sendHost, Rectangle.Empty);
        _workspace.AddModule(_supplyControlHost, Rectangle.Empty);
        workspace.Controls.Add(_workspace);
        return workspace;
    }

    private Control BuildStatusBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 31,
            BackColor = Color.FromArgb(10, 16, 28),
            Padding = new Padding(22, 0, 22, 0)
        };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(34, 48, 68));
            e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500F));

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Margin = new Padding(0);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = MutedColor;
        _statusLabel.Font = new Font("Microsoft YaHei UI", 8.6F);
        _statusLabel.AutoEllipsis = true;
        _statusLabel.Text = "就绪 · 请在左上角选择板卡与波特率后连接";

        _connectionLabel.Dock = DockStyle.Fill;
        _connectionLabel.Margin = new Padding(0);
        _connectionLabel.TextAlign = ContentAlignment.MiddleRight;
        _connectionLabel.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
        _connectionLabel.AutoEllipsis = true;
        _connectionLabel.Text = "●  未连接";
        _connectionLabel.ForeColor = DangerColor;

        layout.Controls.Add(_statusLabel, 0, 0);
        layout.Controls.Add(_connectionLabel, 1, 0);
        bar.Controls.Add(layout);
        return bar;
    }

    private void BindEvents()
    {
        _connectionPanel.ConnectRequested += options =>
        {
            var error = ConnectAdapter(options);
            UpdateConnectionDisplay();
            return error;
        };
        _connectionPanel.DisconnectRequested += DisconnectAdapter;
        _receiveModule.FrameActivated += frame => _sendModule.LoadFrame(frame);
        _receiveModule.FramesCleared += (_, _) =>
        {
            _tickerModule.ClearMessage();
            SetStatus(string.Empty);
        };
        _sendModule.SendAction = frame =>
        {
            if (_adapter?.IsConnected != true)
            {
                return "CAN 适配器尚未连接。";
            }
            _adapter.Send(frame);
            return null;
        };
        _supplyControlModule.SendAction = _sendModule.SendAction;
        _sendModule.StatusChanged += SetStatus;
        _supplyControlModule.StatusChanged += SetStatus;
        _supplyControlModule.DeviceSnChanged += _connectionPanel.SetDeviceSn;
        _supplyControlModule.PressurePlotConfigurationRequested +=
            () => _pressurePlotWindowManager.ShowConfiguration(this);
        _workspace.ModuleVisibilityChanged += WorkspaceOnModuleVisibilityChanged;

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                _sendModule.SendCurrent();
                e.SuppressKeyPress = true;
            }
        };
        FormClosing += (_, _) =>
        {
            _isClosing = true;
            _workspace.SaveLayout();
            _workspace.CloseAllFloatingWindows();
            _pressurePlotWindowManager.CloseAll();
            _adapter?.Disconnect();
            _adapter?.Dispose();
        };
    }

    private void WorkspaceOnModuleVisibilityChanged(
        object? sender,
        ModuleVisibilityChangedEventArgs e)
    {
        if (e.Visible)
        {
            return;
        }

        if (e.ModuleId is "receive" or "ticker")
        {
            _receiveDisplayMode = ReceiveDisplayMode.Collapsed;
            UpdateReceiveMenuChecks();
        }
        else if (e.ModuleId == "supply-control")
        {
            if (_showSupplyControlItem is not null)
            {
                _showSupplyControlItem.Checked = false;
            }
        }
        else if (e.ModuleId == "send")
        {
            if (_showSendItem is not null)
            {
                _showSendItem.Checked = false;
            }
        }
        ApplyModuleVisibility();
    }

    private void SetReceiveDisplayMode(ReceiveDisplayMode mode)
    {
        _receiveDisplayMode = mode;
        UpdateReceiveMenuChecks();
        ApplyModuleVisibility();
    }

    private void UpdateReceiveMenuChecks()
    {
        if (_receiveExpandedItem is not null)
        {
            _receiveExpandedItem.Checked = _receiveDisplayMode == ReceiveDisplayMode.Expanded;
        }
        if (_receiveTickerItem is not null)
        {
            _receiveTickerItem.Checked = _receiveDisplayMode == ReceiveDisplayMode.Ticker;
        }
        if (_receiveCollapsedItem is not null)
        {
            _receiveCollapsedItem.Checked = _receiveDisplayMode == ReceiveDisplayMode.Collapsed;
        }
    }

    private void ApplyModuleVisibility(ToolStripMenuItem? sourceItem = null)
    {
        var showReceive = _receiveDisplayMode == ReceiveDisplayMode.Expanded;
        var showTicker = _receiveDisplayMode == ReceiveDisplayMode.Ticker;
        var showSupplyControl = _showSupplyControlItem?.Checked ?? true;
        var showSend = _showSendItem?.Checked ?? true;
        _workspace.SetModuleVisible(_receiveHost, showReceive);
        if (showReceive && !_workspace.IsModuleFloating(_receiveHost))
        {
            _workspace.FloatModule(
                _receiveHost,
                PointToScreen(new Point(340, 190)));
        }
        _workspace.SetModuleVisible(_tickerHost, showTicker);
        if (showTicker && !_workspace.IsModuleFloating(_tickerHost))
        {
            _workspace.FloatModule(
                _tickerHost,
                PointToScreen(new Point(
                    Math.Max(360, ClientSize.Width - 390),
                    150)));
        }
        _workspace.SetModuleVisible(_supplyControlHost, showSupplyControl);
        if (showSupplyControl &&
            !_workspace.IsModuleFloating(_supplyControlHost))
        {
            _workspace.FloatModule(
                _supplyControlHost,
                PointToScreen(new Point(ClientSize.Width / 2, ClientSize.Height / 2)));
        }
        _workspace.SetModuleVisible(_sendHost, showSend);
        if (showSend && !_workspace.IsModuleFloating(_sendHost))
        {
            _workspace.FloatModule(
                _sendHost,
                PointToScreen(new Point(
                    Math.Max(340, ClientSize.Width / 4),
                    Math.Max(260, ClientSize.Height - 300))));
        }
        _displayState = new DisplayState(
            showReceive,
            showTicker,
            false,
            false,
            false,
            false,
            false,
            showSupplyControl,
            showSend);
        DisplayStateStore.Save(_displayState);
        if (!showReceive &&
            !showTicker &&
            !showSupplyControl &&
            !showSend)
        {
            SetStatus("所有模块已隐藏，请从“显示”菜单选择需要的模块。");
        }
        else
        {
            SetStatus(string.Empty);
        }
    }

    private void RestoreLayout()
    {
        _workspace.RestoreDefaultLayout();
        ApplyModuleVisibility();
    }

    private string? ConnectAdapter(ConnectionOptions options)
    {
        DisconnectAdapter();
        ICanAdapter? candidate = null;
        try
        {
            candidate = options.AdapterIndex switch
            {
                0 => new SlcanAdapter(),
                1 => new PcanAdapter(),
                2 => new VciAdapter(),
                _ => throw new InvalidOperationException("未知的 CAN 适配器类型。")
            };

            var settings = new AdapterSettings(
                candidate.AdapterName,
                options.Port,
                options.CanBitrate,
                options.SerialBitrate,
                options.Channel,
                options.AdapterIndex == 2
                    ? VciAdapter.FindControlCanDll()
                    : options.ControlCanDllPath);

            candidate.FrameReceived += AdapterOnFrameReceived;
            candidate.AdapterError += AdapterOnError;
            candidate.Connect(settings);
            _adapter = candidate;
            _connectionOptions = options;
            _connectedSummary =
                $"{options.AdapterName} · {options.ChannelText} · {options.BitrateText}";
            SetConnectedState(candidate);
            SetStatus(string.Empty);
            return null;
        }
        catch (Exception ex)
        {
            if (candidate is not null)
            {
                candidate.FrameReceived -= AdapterOnFrameReceived;
                candidate.AdapterError -= AdapterOnError;
                candidate.Dispose();
            }
            _adapter = null;
            SetConnectedState(null);
            return ex.Message;
        }
    }

    private void DisconnectAdapter()
    {
        if (_adapter is null)
        {
            SetConnectedState(null);
            return;
        }

        try
        {
            _adapter.Disconnect();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
        finally
        {
            _adapter.FrameReceived -= AdapterOnFrameReceived;
            _adapter.AdapterError -= AdapterOnError;
            _adapter.Dispose();
            _adapter = null;
            SetConnectedState(null);
        }
    }

    private void SetConnectedState(ICanAdapter? adapter)
    {
        var connected = adapter?.IsConnected == true;
        _sendModule.SetConnected(connected);
        _supplyControlModule.SetCanAvailable(connected);
        _connectionPanel.SetConnectionState(connected, _connectedSummary);
        if (!connected)
        {
            _connectionPanel.SetDeviceSn("设备 SN：--");
        }
        UpdateConnectionDisplay();
    }

    private void UpdateConnectionDisplay()
    {
        var connected = _adapter?.IsConnected == true;
        _connectionLabel.Text = connected
            ? $"●  已连接  ·  {_connectedSummary}"
            : "●  未连接";
        _connectionLabel.ForeColor = connected ? SuccessColor : DangerColor;
    }

    private void AdapterOnFrameReceived(object? sender, CanFrameReceivedEventArgs e)
    {
        if (_isClosing || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(() =>
        {
            if (_isClosing || e.Frame.Direction != FrameDirection.Rx)
            {
                return;
            }

            _receiveModule.AddFrame(e.Frame);
            _tickerModule.ShowFrame(e.Frame);
            _supplyControlModule.ProcessFrame(e.Frame);
            _connectionPanel.SetDeviceSn(_supplyControlModule.ConnectedSnText);
            if (SensorReportParser.TryParse(e.Frame, out var report))
            {
                AppendPressurePlotValues(report);
            }
        });
    }

    private void AppendPressurePlotValues(SensorReport report)
    {
        if (report.Id == 0x06)
        {
            _plotModules[0].AppendValue(report.Values[0]);
            return;
        }
        if (report.Id != 0x07)
        {
            return;
        }

        for (var branch = 1; branch <= 4; branch++)
        {
            _plotModules[branch].AppendValue(report.Values[branch - 1]);
        }
    }

    private void AdapterOnError(object? sender, string message)
    {
        if (_isClosing || !IsHandleCreated)
        {
            return;
        }
        BeginInvoke(() => SetStatus(message));
    }

    private void SetStatus(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            _statusLabel.ForeColor = MutedColor;
            _statusLabel.Text = "就绪 · 功能窗口可独立最小化、最大化和关闭";
        }
        else
        {
            _statusLabel.ForeColor = DangerColor;
            _statusLabel.Text = "最近事件：" + message.Replace('\r', ' ').Replace('\n', ' ');
        }
    }

    private enum ReceiveDisplayMode
    {
        Expanded,
        Ticker,
        Collapsed
    }

    private sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(30, 48, 70);
        public override Color MenuItemBorder => AccentColor;
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(30, 48, 70);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(30, 48, 70);
        public override Color ToolStripDropDownBackground => FieldColor;
        public override Color ImageMarginGradientBegin => FieldColor;
        public override Color ImageMarginGradientMiddle => FieldColor;
        public override Color ImageMarginGradientEnd => FieldColor;
        public override Color MenuBorder => BorderColor;
    }
}
