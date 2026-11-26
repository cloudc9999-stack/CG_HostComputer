using System.Reflection;
using System.Runtime.InteropServices;
using CanHost.Adapters;
using CanHost.Controls;
using CanHost.Docking;
using CanHost.Models;
using CanHost.Modules;
using CanHost;

DockableWorkspace.LayoutPathOverride = Path.Combine(
    Environment.CurrentDirectory,
    "dock-layout-test.json");
File.Delete(DockableWorkspace.LayoutPathOverride);
AppTheme.SettingPathOverride = Path.Combine(
    Environment.CurrentDirectory,
    "theme-test.txt");
File.Delete(AppTheme.SettingPathOverride);
AppTheme.SetTheme(AppThemeMode.Dark);
DisplayStateStore.PathOverride = Path.Combine(
    Environment.CurrentDirectory,
    "display-state-test.json");
File.Delete(DisplayStateStore.PathOverride);
SupplySchematicControl.AnimationEnabled = false;

var adapterType = typeof(SlcanAdapter);
var parseMethod = adapterType.GetMethod(
    "TryParseFrame",
    BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("找不到 SLCAN 帧解析器。");

void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"测试失败：{message}");
    }
}

CanFrame Parse(string line)
{
    object?[] arguments = [line, null];
    var success = (bool)(parseMethod.Invoke(null, arguments) ?? false);
    Assert(success, $"无法解析帧：{line}");
    return (CanFrame)(arguments[1] ?? throw new InvalidOperationException($"帧为空：{line}"));
}

var standard = Parse("t12320111");
Assert(standard.Id == 0x123, "标准帧 ID");
Assert(!standard.Extended && !standard.Remote, "标准数据帧类型");
Assert(standard.Dlc == 2 && standard.Data.SequenceEqual(new byte[] { 0x01, 0x11 }), "标准帧 DLC/数据");

var extended = Parse("T18DAF11080112233445566778");
Assert(extended.Id == 0x18DAF110, "扩展帧 ID");
Assert(extended.Extended && !extended.Remote, "扩展数据帧类型");
Assert(extended.Dlc == 8 && extended.Data.Length == 8, "扩展帧 DLC/数据");

var remote = Parse("r1234");
Assert(remote.Id == 0x123 && remote.Remote && remote.Dlc == 4, "标准远程帧");

object?[] malformedArguments = ["t1239", null];
var malformedAccepted = (bool)(parseMethod.Invoke(null, malformedArguments) ?? false);
Assert(!malformedAccepted, "非法帧不应被接受");

var pcanMessageType = typeof(PcanAdapter).GetNestedType("TpcanMsg", BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("找不到 PCAN 消息结构体。");
Assert(Marshal.SizeOf(pcanMessageType) == 14, "PCAN 消息结构体大小必须为 14 字节");
Assert(
    ConnectionOptions.CreateDefault(VciAdapter.FindControlCanDll()).CanBitrate == 1_000_000,
    "默认 CAN 波特率必须为 1 Mbit/s");

var protocolParser = new CanProtocolV1Parser();
CanFrame SingleByteFrame(byte value) => new(
    0x123,
    false,
    false,
    1,
    [value],
    DateTime.Now,
    FrameDirection.Rx);

Assert(!protocolParser.TryConsume(SingleByteFrame(0x01), out _), "协议帧头");
Assert(!protocolParser.TryConsume(SingleByteFrame(0x12), out _), "协议高字节");
Assert(
    protocolParser.TryConsume(SingleByteFrame(0x34), out var combined) &&
    combined == 0x1234,
    "协议 16 位合并");

var singleFrameParser = new CanProtocolV1Parser();
var compactProtocolFrame = new CanFrame(
    0x123,
    false,
    false,
    3,
    [0x01, 0xAB, 0xCD],
    DateTime.Now,
    FrameDirection.Rx);
Assert(
    singleFrameParser.TryConsume(compactProtocolFrame, out var compactValue) &&
    compactValue == 0xABCD,
    "协议单帧 01 HH LL 合并");

var mainPressureReport = new CanFrame(
    0x06,
    false,
    false,
    2,
    [0x12, 0x34],
    DateTime.Now,
    FrameDirection.Rx);
Assert(
    SensorReportParser.TryParse(mainPressureReport, out var mainPressure) &&
    mainPressure.Values.SequenceEqual(new ushort[] { 0x1234 }),
    "0x06 主气压 UINT16 解析");

var branchPressureReport = new CanFrame(
    0x07,
    false,
    false,
    8,
    [0x00, 0x64, 0x00, 0xC8, 0x01, 0x2C, 0x01, 0x90],
    DateTime.Now,
    FrameDirection.Rx);
Assert(
    SensorReportParser.TryParse(branchPressureReport, out var branchPressures) &&
    branchPressures.Values.SequenceEqual(new ushort[] { 100, 200, 300, 400 }),
    "0x07 四路气压 UINT16 解析");

var temperatureReport = new CanFrame(
    0x08,
    false,
    false,
    8,
    [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08],
    DateTime.Now,
    FrameDirection.Rx);
Assert(
    SensorReportParser.TryParse(temperatureReport, out var temperatures) &&
    temperatures.Values.SequenceEqual(new ushort[] { 0x0102, 0x0304, 0x0506, 0x0708 }),
    "0x08 四路温度 UINT16 解析");
Assert(
    Pt1000TemperatureConverter.TryConvertRaw(0, out var zeroCelsius) &&
    Math.Abs(zeroCelsius) < 0.11,
    "PT1000 数字量 0 转换为 0℃");
Assert(
    Pt1000TemperatureConverter.TryConvertRaw(1914, out var temperature1914) &&
    Math.Abs(temperature1914 - 28.0015) < 0.11,
    "PT1000 指定公式计算数字量 1914");
Assert(
    Pt1000TemperatureConverter.TryGetTemperature(
        Pt1000TemperatureConverter.GetResistance(100),
        out var lookupTemperature) &&
    Math.Abs(lookupTemperature - 100) < 0.11,
    "PT1000 电阻查表得到温度");
Assert(
    !Pt1000TemperatureConverter.TryConvertRaw(4095, out _),
    "PT1000 数字量 4095 判定为开路");

using var analysisChart = new LineChartControl();
analysisChart.ConfigureAxes(0, 100, 5);
analysisChart.AddPoint(1, 10);
analysisChart.AddPoint(2, 20);
analysisChart.AddPoint(3, 30);
analysisChart.AddPoint(7, 70);
var analysis = analysisChart.GetVisibleStatistics();
Assert(analysis is not null, "绘图分析结果");
Assert(
    analysis!.Count == 3 &&
    Math.Abs(analysis.Average - 40) < 0.001 &&
    analysis.Minimum == 20 &&
    analysis.Maximum == 70,
    "绘图窗口平均值、最大值、最小值");
using var defaultAxesChart = new LineChartControl();
Assert(
    defaultAxesChart.YMinimum == 0 && defaultAxesChart.YMaximum == 4095,
    "默认纵坐标范围必须为 0-4095");
using var zoomChart = new LineChartControl();
zoomChart.ConfigureAxes(0, 1_000, 40);
zoomChart.ZoomYIn();
zoomChart.ZoomXIn();
Assert(
    zoomChart.YMaximum - zoomChart.YMinimum == 500 &&
    zoomChart.TimeWindowSeconds == 20,
    "纵坐标和横坐标放大工具");
zoomChart.ResetToConfiguredAxes();
Assert(
    zoomChart.YMinimum == 0 &&
    zoomChart.YMaximum == 1_000 &&
    zoomChart.TimeWindowSeconds == 40 &&
    zoomChart.ConfiguredYMaximum == 1_000 &&
    zoomChart.ConfiguredTimeWindowSeconds == 40,
    "一键恢复配置中的坐标范围");

using var rollingChart = new LineChartControl();
rollingChart.ConfigureAxes(0, 100, 30);
for (var index = 0; index <= 40; index++)
{
    rollingChart.AddPoint(index, index);
}
var visibleWindowMethod = typeof(LineChartControl).GetMethod(
    "GetVisibleWindow",
    BindingFlags.NonPublic | BindingFlags.Instance);
var toScreenXMethod = typeof(LineChartControl).GetMethod(
    "ToScreenX",
    BindingFlags.NonPublic | BindingFlags.Static);
Assert(
    visibleWindowMethod is not null && toScreenXMethod is not null,
    "压力图右移时间轴接口");
var rollingWindow = visibleWindowMethod!.Invoke(rollingChart, null)
    ?? throw new InvalidOperationException("压力图可见范围为空。");
var visibleWindowType = rollingWindow.GetType();
var visibleEndTime = (double)visibleWindowType
    .GetProperty("EndTime", BindingFlags.Public | BindingFlags.Instance)!
    .GetValue(rollingWindow)!;
var visiblePoints = ((System.Collections.IEnumerable)visibleWindowType
        .GetProperty("Points", BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(rollingWindow)!)
    .Cast<object>()
    .ToArray();
var latestAge = (double)visiblePoints[^1].GetType()
    .GetProperty("Seconds", BindingFlags.Public | BindingFlags.Instance)!
    .GetValue(visiblePoints[^1])!;
var latestScreenX = (float)toScreenXMethod!.Invoke(
    null,
    [new RectangleF(10, 10, 200, 100), latestAge, 0d, 30d])!;
Assert(
    visibleEndTime == 30 &&
    Math.Abs(latestAge) < 0.001 &&
    Math.Abs(latestScreenX - 210) < 0.01,
    "最新压力数据固定在右侧 0s");
using var rightEdgeChart = new LineChartControl { Size = new Size(600, 400) };
using var rightEdgeBitmap = new Bitmap(600, 400);
rightEdgeChart.DrawToBitmap(rightEdgeBitmap, new Rectangle(0, 0, 600, 400));
var plotBounds = (RectangleF?)typeof(LineChartControl)
    .GetField("_plotBounds", BindingFlags.NonPublic | BindingFlags.Instance)
    ?.GetValue(rightEdgeChart);
Assert(
    plotBounds.HasValue &&
    Math.Abs(plotBounds.Value.Right - 594) < 0.1,
    "压力图右侧不再预留大面积空白");

using var selectionZoomChart = new LineChartControl();
selectionZoomChart.ConfigureAxes(0, 1_000, 30);
for (var index = 0; index <= 100; index++)
{
    selectionZoomChart.AddPoint(index, index);
}
var plotBoundsField = typeof(LineChartControl).GetField(
    "_plotBounds",
    BindingFlags.NonPublic | BindingFlags.Instance);
var applySelectionMethod = typeof(LineChartControl).GetMethod(
    "ApplyZoomSelection",
    BindingFlags.NonPublic | BindingFlags.Instance);
Assert(
    plotBoundsField is not null && applySelectionMethod is not null,
    "鼠标框选缩放接口");
plotBoundsField!.SetValue(
    selectionZoomChart,
    new RectangleF(80, 30, 400, 200));
var verticalSelection = new Rectangle(80, 80, 400, 80);
Assert(
    (bool)applySelectionMethod!.Invoke(
        selectionZoomChart,
        [verticalSelection, ChartZoomAxis.Vertical])! &&
    selectionZoomChart.YMaximum < 1_000 &&
    selectionZoomChart.YMinimum > 0,
    "鼠标上下框选仅放大纵坐标");
var horizontalSelection = new Rectangle(180, 30, 160, 200);
Assert(
    (bool)applySelectionMethod.Invoke(
        selectionZoomChart,
        [horizontalSelection, ChartZoomAxis.Horizontal])!,
    "鼠标左右框选仅放大横坐标");
var customStartField = typeof(LineChartControl).GetField(
    "_customViewStartTime",
    BindingFlags.NonPublic | BindingFlags.Instance);
var customEndField = typeof(LineChartControl).GetField(
    "_customViewEndTime",
    BindingFlags.NonPublic | BindingFlags.Instance);
Assert(
    customStartField?.GetValue(selectionZoomChart) is double customStart &&
    customEndField?.GetValue(selectionZoomChart) is double customEnd &&
    customEnd - customStart < 30,
    "横坐标框选范围生效");

Console.WriteLine("SLCAN 标准帧解析：PASS");
Console.WriteLine("SLCAN 扩展帧解析：PASS");
Console.WriteLine("SLCAN 远程帧解析：PASS");
Console.WriteLine("SLCAN 非法帧拒绝：PASS");
Console.WriteLine("PCAN 消息结构体布局：PASS");
Console.WriteLine("CAN 协议 0x01 + 高低字节合并：PASS");
Console.WriteLine("CAN 协议单帧 01 HH LL 合并：PASS");
Console.WriteLine("传感器主动上报 0x06/0x07/0x08 解析：PASS");
Console.WriteLine("绘图固定坐标与窗口统计：PASS");
Console.WriteLine("绘图默认纵坐标 0-4095：PASS");

if (args.Contains("--hardware", StringComparer.OrdinalIgnoreCase))
{
    var dllPath = VciAdapter.FindControlCanDll()
        ?? throw new InvalidOperationException("未找到 ControlCAN.dll。");
    Assert(VciAdapter.FindBridgeExecutable() is not null, "未找到 32 位 ControlCAN 桥接器");

    using var vci = new VciAdapter();
    var adapterError = string.Empty;
    vci.AdapterError += (_, message) => adapterError = message;
    vci.Connect(new AdapterSettings("ControlCAN", string.Empty, 500_000, 115_200, 1, dllPath));
    Assert(vci.IsConnected, "ControlCAN 适配器连接状态");
    await Task.Delay(300);
    vci.Disconnect();
    Assert(!vci.IsConnected, "ControlCAN 适配器断开状态");
    Assert(string.IsNullOrEmpty(adapterError), $"ControlCAN 通信错误：{adapterError}");
    Console.WriteLine("ControlCAN 实际设备连接/接收轮询：PASS");
}

if (args.Contains("--render", StringComparer.OrdinalIgnoreCase))
{
    Exception? renderError = null;
    var renderThread = new Thread(() =>
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            void RenderForm(Form form, string fileName, Action? afterShow = null)
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.Opacity = 0;
                form.Show();
                Application.DoEvents();
                afterShow?.Invoke();
                Application.DoEvents();
                form.Refresh();
                Application.DoEvents();

                using var image = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                var outputPath = Path.Combine(Environment.CurrentDirectory, fileName);
                image.Save(outputPath);
                form.Close();
                Console.WriteLine($"界面预览：{outputPath}");
            }

            using (var buttonTestForm = new Form { Size = new Size(900, 650) })
            using (var buttonTestSchematic = new SupplySchematicControl { Dock = DockStyle.Fill })
            {
                buttonTestForm.Controls.Add(buttonTestSchematic);
                var surfaceField = typeof(SupplySchematicControl).GetField(
                    "_surface",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var surface = surfaceField?.GetValue(buttonTestSchematic);
                var handleButtonMethod = surface?.GetType().GetMethod(
                    "TryHandleSelfLockClick",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var openRectField = surface?.GetType().GetField(
                    "SelfLockOpenRect",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var closeRectField = surface?.GetType().GetField(
                    "SelfLockCloseRect",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert(
                    surface is not null &&
                    handleButtonMethod is not null &&
                    openRectField is not null &&
                    closeRectField is not null,
                    "自锁阀双按钮测试接口");
                var requestedStates = new List<bool>();
                buttonTestSchematic.SelfLockCommandRequested += open => requestedStates.Add(open);
                surface!.GetType().GetField(
                        "_canControl",
                        BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(surface, true);
                surface.GetType().GetField(
                        "_selfLockControlEnabled",
                        BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(surface, true);
                var openRect = (Rectangle)openRectField!.GetValue(null)!;
                var closeRect = (Rectangle)closeRectField!.GetValue(null)!;
                handleButtonMethod!.Invoke(
                    surface,
                    [new Point(openRect.Left + openRect.Width / 2, openRect.Top + openRect.Height / 2)]);
                handleButtonMethod.Invoke(
                    surface,
                    [new Point(closeRect.Left + closeRect.Width / 2, closeRect.Top + closeRect.Height / 2)]);
                Assert(
                    requestedStates.SequenceEqual(new[] { true, false }),
                    "自锁阀开启和关闭按钮分别请求对应操作");
                buttonTestSchematic.SetSelfLockValveState("开启");
                buttonTestSchematic.SetSolenoidRatio(1, "50.0%");
                buttonTestSchematic.SetGasFlowEnabled(true);
                surface.GetType().GetField(
                        "_selfLockVisualOpen",
                        BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(surface, 1f);
                var visualPercents = (float[])surface.GetType().GetField(
                        "_solenoidVisualPercent",
                        BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(surface)!;
                visualPercents[0] = 50f;
                surface.GetType().GetField(
                        "_valveAnimationPhase",
                        BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(surface, 0.45f);
                RenderForm(buttonTestForm, "self-lock-buttons-test.png");
            }

            var sentSupplyFrames = new List<CanFrame>();
            using (var supplyModule = new SupplyConnectionModule())
            {
                supplyModule.SetCanAvailable(true);
                supplyModule.SendAction = frame =>
                {
                    sentSupplyFrames.Add(frame);
                    return null;
                };
                supplyModule.StartConnection();
                Assert(
                    sentSupplyFrames.Count == 1 &&
                    sentSupplyFrames[0].Id == 0x01 &&
                    sentSupplyFrames[0].Dlc == 2 &&
                    sentSupplyFrames[0].Data.SequenceEqual(new byte[] { 0x01, 0x01 }),
                    "储供连接请求帧");

                supplyModule.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    8,
                    [0x01, 0x02, 0x00, 0x00, 0x78, 0xC2, 0x75, 0x91],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(supplyModule.IsLinkConnected, "SN 正确时连接成功");
                Assert(
                    supplyModule.ConnectedSnDisplayText == "设备 SN：0x78C27591",
                    "储供连接仅显示设备十六进制 SN");
                var connectionControls = EnumerateControls(supplyModule).ToArray();
                Assert(
                    connectionControls.OfType<Button>().Count() == 2 &&
                    connectionControls.OfType<Label>().Count() == 1,
                    "储供连接仅保留两个按钮和 SN");
                Assert(
                    sentSupplyFrames.Count == 2 &&
                    sentSupplyFrames[1].Dlc == 2 &&
                    sentSupplyFrames[1].Data.SequenceEqual(new byte[] { 0x01, 0x03 }),
                    "连接后心跳帧");

                supplyModule.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x01, 0x03],
                    DateTime.Now,
                    FrameDirection.Rx));
                var ackCountField = typeof(SupplyConnectionModule).GetField(
                    "_ackCount",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert(
                    (int?)ackCountField?.GetValue(supplyModule) == 1,
                    "心跳应答处理");

                var heartbeatField = typeof(SupplyConnectionModule).GetField(
                    "_lastHeartbeatSentAt",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var timerTickMethod = typeof(SupplyConnectionModule).GetMethod(
                    "OnTimerTick",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert(heartbeatField is not null && timerTickMethod is not null, "储供连接状态机");
                heartbeatField!.SetValue(supplyModule, DateTime.Now.AddSeconds(-2.2));
                timerTickMethod!.Invoke(supplyModule, null);
                Assert(
                    sentSupplyFrames.Count == 3 &&
                    supplyModule.IsLinkConnected,
                    "每 2 秒心跳发送");
                heartbeatField.SetValue(supplyModule, DateTime.Now.AddSeconds(-2.2));
                timerTickMethod.Invoke(supplyModule, null);
                Assert(!supplyModule.IsLinkConnected, "心跳应答丢失后连接中断");
            }

            using (var wrongSnModule = new SupplyConnectionModule())
            {
                wrongSnModule.SetCanAvailable(true);
                wrongSnModule.SendAction = _ => null;
                wrongSnModule.StartConnection();
                wrongSnModule.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    8,
                    [0x01, 0x02, 0x00, 0x00, 0x78, 0xC2, 0x75, 0x92],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(!wrongSnModule.IsLinkConnected, "错误 SN 应拒绝连接");
            }

            var disconnectFrames = new List<CanFrame>();
            using (var disconnectModule = new SupplyConnectionModule())
            {
                disconnectModule.SetCanAvailable(true);
                disconnectModule.SendAction = frame =>
                {
                    disconnectFrames.Add(frame);
                    return null;
                };
                disconnectModule.StartConnection();
                disconnectModule.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    8,
                    [0x01, 0x02, 0x00, 0x00, 0x78, 0xC2, 0x75, 0x91],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(disconnectModule.IsLinkConnected, "断开测试前置连接");
                disconnectModule.DisconnectLink();
                Assert(!disconnectModule.IsLinkConnected, "断开协议后本地显示未连接");
                Assert(
                    disconnectFrames.Count >= 3 &&
                    disconnectFrames[^1].Id == 0x01 &&
                    disconnectFrames[^1].Dlc == 2 &&
                    disconnectFrames[^1].Data.SequenceEqual(new byte[] { 0x01, 0x04 }),
                    "断开协议发送帧");
            }

            var branchFrames = new List<CanFrame>();
            using (var supplyControl = new SupplyControlModule())
            {
                supplyControl.SetCanAvailable(true);
                supplyControl.SendAction = frame =>
                {
                    branchFrames.Add(frame);
                    return null;
                };
                var connectionField = typeof(SupplyControlModule).GetField(
                    "_connection",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var connection = (SupplyConnectionModule?)connectionField?.GetValue(supplyControl);
                Assert(connection is not null, "储供控制内嵌连接模块");
                connection!.StartConnection();
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    8,
                    [0x01, 0x02, 0x00, 0x00, 0x78, 0xC2, 0x75, 0x91],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(connection.IsLinkConnected, "储供控制连接前置状态");
                branchFrames.Clear();
                var supplySchematic = (SupplySchematicControl?)typeof(SupplyControlModule)
                    .GetField("_schematic", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(supplyControl);
                var schematicSurface = (Control?)typeof(SupplySchematicControl)
                    .GetField("_surface", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(supplySchematic);
                var inlineDutyInputs = (NumericUpDown[]?)schematicSurface?.GetType()
                    .GetField(
                        "_inlineDutyInputs",
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(schematicSurface);
                var inlineDutyButtons = (Button[]?)schematicSurface?.GetType()
                    .GetField(
                        "_inlineDutyButtons",
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(schematicSurface);
                var inlineTargetInputs = (NumericUpDown[]?)schematicSurface?.GetType()
                    .GetField(
                        "_inlineTargetInputs",
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(schematicSurface);
                var inlineTargetButtons = (Button[]?)schematicSurface?.GetType()
                    .GetField(
                        "_inlineTargetButtons",
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(schematicSurface);
                Assert(
                    inlineDutyInputs is not null &&
                    inlineDutyButtons is not null &&
                    inlineTargetInputs is not null &&
                    inlineTargetButtons is not null,
                    "部件下方内嵌参数输入框");
                var selfLockCommandMethod = typeof(SupplyControlModule).GetMethod(
                    "SendSelfLockCommand",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert(selfLockCommandMethod is not null, "自锁阀命令接口");
                selfLockCommandMethod!.Invoke(supplyControl, [true]);
                Assert(
                    branchFrames.Count == 1 &&
                    branchFrames[^1].Id == 0x01 &&
                    branchFrames[^1].Dlc == 4 &&
                    branchFrames[^1].Data.SequenceEqual(new byte[] { 0x04, 0x01, 0x01, 0x01 }),
                    "开启自锁阀发送帧");
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x02, 0x01],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(
                    supplyControl.IsSelfLockOpen && supplyControl.IsGasFlowEnabled,
                    "自锁阀开启后气体流动");
                Assert(
                    supplyControl.GetSolenoidDuty(1) == 0 &&
                    supplyControl.GetSolenoidDuty(2) == 0 &&
                    supplyControl.GetSolenoidDuty(3) == 0 &&
                    supplyControl.GetSolenoidDuty(4) == 0 &&
                    !supplyControl.IsBranchFlowEnabled(1) &&
                    !supplyControl.IsBranchFlowEnabled(2) &&
                    !supplyControl.IsBranchFlowEnabled(3) &&
                    !supplyControl.IsBranchFlowEnabled(4),
                    "自锁阀开启后电磁阀默认占空比为 0");

                var inputsField = typeof(SupplyControlModule).GetField(
                    "_dutyInputs",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var buttonsField = typeof(SupplyControlModule).GetField(
                    "_dutyButtons",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var targetInputsField = typeof(SupplyControlModule).GetField(
                    "_pressureTargetInputs",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var targetButtonsField = typeof(SupplyControlModule).GetField(
                    "_pressureTargetButtons",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var solenoidManualField = typeof(SupplyControlModule).GetField(
                    "_solenoidManualButtons",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var solenoidAutoField = typeof(SupplyControlModule).GetField(
                    "_solenoidAutoButtons",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var onclickMethod = typeof(Button).GetMethod(
                    "OnClick",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert(
                    inputsField is not null &&
                    buttonsField is not null &&
                    targetInputsField is not null &&
                    targetButtonsField is not null &&
                    solenoidManualField is not null &&
                    solenoidAutoField is not null &&
                    onclickMethod is not null,
                    "电磁阀独立占空比输入控件");
                var dutyInputs = (NumericUpDown[])inputsField!.GetValue(supplyControl)!;
                var dutyButtons = (Button[])buttonsField!.GetValue(supplyControl)!;
                var targetInputs =
                    (NumericUpDown[])targetInputsField!.GetValue(supplyControl)!;
                var targetButtons =
                    (Button[])targetButtonsField!.GetValue(supplyControl)!;
                var solenoidManualButtons =
                    (Button[])solenoidManualField!.GetValue(supplyControl)!;
                var solenoidAutoButtons =
                    (Button[])solenoidAutoField!.GetValue(supplyControl)!;
                Assert(
                    dutyInputs.All(input => input.Enabled) &&
                    dutyButtons.All(button => button.Enabled) &&
                    targetInputs.All(input => !input.Enabled) &&
                    solenoidManualButtons.All(button => button.Enabled) &&
                    solenoidAutoButtons.All(button => button.Enabled),
                    "默认手动模式下可设置占空比且目标值不可设置");
                var dutyValues = new[] { 50.1d, 25.2d, 10.3d, 0.4d };
                var dutyBigEndian = new[]
                {
                    new byte[] { 0x00, 0x00, 0x01, 0xF5 },
                    new byte[] { 0x00, 0x00, 0x00, 0xFC },
                    new byte[] { 0x00, 0x00, 0x00, 0x67 },
                    new byte[] { 0x00, 0x00, 0x00, 0x04 }
                };
                for (var index = 0; index < 4; index++)
                {
                    dutyInputs[index].Value = (decimal)dutyValues[index];
                    onclickMethod!.Invoke(dutyButtons[index], [EventArgs.Empty]);
                    var sent = branchFrames[^1];
                    Assert(
                        sent.Id == 0x01 &&
                        sent.Dlc == 8 &&
                        sent.Data[0] == 0x04 &&
                        sent.Data[1] == 0x03 &&
                        sent.Data[2] == index + 1 &&
                        sent.Data[3] == 0x00 &&
                        sent.Data.Skip(4).SequenceEqual(dutyBigEndian[index]),
                        $"电磁阀 {index + 1} 独立占空比发送帧");
                    supplyControl.ProcessFrame(new CanFrame(
                        0x05,
                        false,
                        false,
                        2,
                        [0x02, 0x01],
                        DateTime.Now,
                        FrameDirection.Rx));
                    Assert(
                        Math.Abs(supplyControl.GetSolenoidDuty(index + 1) - dutyValues[index]) < 0.001,
                        $"电磁阀 {index + 1} 占空比应答确认");
                    Assert(
                        supplyControl.GetSolenoidRatioText(index + 1) ==
                        $"{dutyValues[index]:0.0}%" &&
                        (dutyValues[index] <= 0 || supplyControl.IsBranchFlowEnabled(index + 1)),
                        $"电磁阀 {index + 1} 画面数值和动画状态");
                }
                Assert(
                    supplyControl.GetSolenoidDuty(1) > 50 &&
                    supplyControl.GetSolenoidDuty(2) > 25 &&
                    supplyControl.GetSolenoidDuty(3) > 10 &&
                    supplyControl.GetSolenoidDuty(4) > 0,
                    "四路占空比相互独立");

                var setSolenoidModeMethod = typeof(SupplyControlModule).GetMethod(
                    "SetSolenoidMode",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert(setSolenoidModeMethod is not null, "电磁阀模式切换接口");
                setSolenoidModeMethod!.Invoke(supplyControl, [1, true]);
                Assert(
                    branchFrames[^1].Data.SequenceEqual(
                        new byte[] { 0x04, 0x02, 0x01, 0x00 }),
                    "电磁阀1自动模式切换下发帧");
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x02, 0x01],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(
                    !dutyInputs[0].Enabled &&
                    targetInputs[0].Enabled &&
                    dutyButtons[0].Enabled == false &&
                    targetButtons[0].Enabled &&
                    inlineTargetInputs![0].Visible &&
                    inlineTargetInputs[0].Enabled &&
                    !inlineDutyInputs![0].Visible &&
                    supplyControl.GetSolenoidDuty(1) == 0 &&
                    supplyControl.IsSelfLockOpen,
                    "自动模式 ACK 后占空比清零且自锁阀保持不变");
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    8,
                    [0x03, 0x03, 0x01, 0x00, 0x00, 0x00, 0x01, 0xF4],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(
                    supplyControl.GetSolenoidRatioText(1) == "50.0%" &&
                    supplyControl.IsBranchFlowEnabled(1),
                    "自动模式显示并采用上传占空比");
                var countBeforeAutoDutyClick = branchFrames.Count;
                onclickMethod!.Invoke(dutyButtons[0], [EventArgs.Empty]);
                Assert(
                    branchFrames.Count == countBeforeAutoDutyClick,
                    "自动模式不发送电磁阀占空比协议");

                inlineTargetInputs![0].Value = 12_345;
                onclickMethod.Invoke(inlineTargetButtons![0], [EventArgs.Empty]);
                Assert(
                    branchFrames[^1].Data.SequenceEqual(
                        new byte[] { 0x04, 0x03, 0x05, 0x00, 0x00, 0x00, 0x30, 0x39 }),
                    "气压传感器1目标值参数下发帧");
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x02, 0x01],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(
                    targetInputs[0].Value == 12_345,
                    "气压目标值 ACK 后确认");

                inlineTargetInputs[0].Value = 22_222;
                onclickMethod.Invoke(inlineTargetButtons[0], [EventArgs.Empty]);
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x02, 0x02],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(
                    targetInputs[0].Value == 12_345,
                    "气压目标值 NAK 后恢复上一确认值");

                setSolenoidModeMethod.Invoke(supplyControl, [1, false]);
                Assert(
                    branchFrames[^1].Data.SequenceEqual(
                        new byte[] { 0x04, 0x02, 0x01, 0x01 }),
                    "电磁阀1手动模式切换下发帧");
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x02, 0x01],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(
                    dutyInputs[0].Enabled &&
                    !targetInputs[0].Enabled &&
                    inlineDutyInputs![0].Visible &&
                    inlineDutyInputs[0].Enabled &&
                    !inlineTargetInputs[0].Visible &&
                    supplyControl.GetSolenoidRatioText(1) == "0.0%",
                    "手动模式 ACK 后允许占空比并默认清零");

                selfLockCommandMethod.Invoke(supplyControl, [false]);
                Assert(
                    supplyControl.GetSolenoidDuty(1) == 0 &&
                    supplyControl.GetSolenoidDuty(2) == 0 &&
                    supplyControl.GetSolenoidDuty(3) == 0 &&
                    supplyControl.GetSolenoidDuty(4) == 0,
                    "发送关闭自锁阀命令后四路占空比本地归零");
                Assert(
                    branchFrames[^1].Data.SequenceEqual(new byte[] { 0x04, 0x01, 0x01, 0x00 }),
                    "关闭自锁阀发送帧");
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x02, 0x01],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(
                    !supplyControl.IsSelfLockOpen && !supplyControl.IsGasFlowEnabled,
                    "自锁阀关闭后停止气体流动");
                selfLockCommandMethod.Invoke(supplyControl, [true]);
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x02, 0x02],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(
                    !supplyControl.IsSelfLockOpen && !supplyControl.IsGasFlowEnabled,
                    "自锁阀 NAK 后保持关闭状态");
                var dutyHintField = typeof(SupplyControlModule).GetField(
                    "_dutyLockHintLabel",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var dutyHint = (Label?)dutyHintField?.GetValue(supplyControl);
                Assert(
                    dutyInputs.All(input => !input.Enabled) &&
                    dutyHint?.Text == "请打开自锁阀后设置电磁阀占空比",
                    "自锁阀关闭后禁止设置占空比并显示提示");

                var ptcManualField = typeof(SupplyControlModule).GetField(
                    "_ptcManualButtons",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var ptcAutoField = typeof(SupplyControlModule).GetField(
                    "_ptcAutoButtons",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var ptcOnField = typeof(SupplyControlModule).GetField(
                    "_ptcOnButtons",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var ptcOffField = typeof(SupplyControlModule).GetField(
                    "_ptcOffButtons",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var ptcStatesField = typeof(SupplyControlModule).GetField(
                    "_ptcOnStates",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var setPtcModeMethod = typeof(SupplyControlModule).GetMethod(
                    "SetPtcMode",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert(
                    ptcManualField is not null &&
                    ptcAutoField is not null &&
                    ptcOnField is not null &&
                    ptcOffField is not null &&
                    ptcStatesField is not null &&
                    setPtcModeMethod is not null,
                    "PTC 模式和开关控件");
                var ptcOnButtons = (Button[])ptcOnField!.GetValue(supplyControl)!;
                var ptcOffButtons = (Button[])ptcOffField!.GetValue(supplyControl)!;
                var ptcStates = (bool[])ptcStatesField!.GetValue(supplyControl)!;
                setPtcModeMethod!.Invoke(supplyControl, [0, true]);
                Assert(
                    branchFrames[^1].Data.SequenceEqual(
                        new byte[] { 0x04, 0x02, 0x05, 0x00 }),
                    "PTC1 自动模式切换下发帧");
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x02, 0x01],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(
                    !ptcOnButtons[0].Enabled && !ptcOffButtons[0].Enabled,
                    "PTC 自动模式下禁用开关按钮");
                var countBeforeAutoPtc = branchFrames.Count;
                typeof(SupplyControlModule).GetMethod(
                        "SetPtcSwitch",
                        BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(supplyControl, [0, true]);
                Assert(
                    branchFrames.Count == countBeforeAutoPtc,
                    "PTC 自动模式不发送开关协议");

                setPtcModeMethod.Invoke(supplyControl, [0, false]);
                Assert(
                    branchFrames[^1].Data.SequenceEqual(
                        new byte[] { 0x04, 0x02, 0x05, 0x01 }),
                    "PTC1 手动模式切换下发帧");
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x02, 0x01],
                    DateTime.Now,
                    FrameDirection.Rx));
                onclickMethod.Invoke(ptcOnButtons[0], [EventArgs.Empty]);
                Assert(
                    branchFrames[^1].Data.SequenceEqual(
                        new byte[] { 0x04, 0x01, 0x02, 0x01 }),
                    "PTC1 开启协议编号和状态");
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x02, 0x01],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(ptcStates[0], "PTC1 ACK 后确认开启");
                onclickMethod.Invoke(ptcOffButtons[0], [EventArgs.Empty]);
                Assert(
                    branchFrames[^1].Data.SequenceEqual(
                        new byte[] { 0x04, 0x01, 0x02, 0x00 }),
                    "PTC1 关闭协议使用状态 0x00");
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x02, 0x02],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(ptcStates[0], "PTC1 NAK 后保持上一状态");
                onclickMethod.Invoke(ptcOffButtons[0], [EventArgs.Empty]);
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x02, 0x01],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(!ptcStates[0], "PTC1 ACK 后确认关闭");

                for (var ptcIndex = 1; ptcIndex < 4; ptcIndex++)
                {
                    onclickMethod.Invoke(ptcOnButtons[ptcIndex], [EventArgs.Empty]);
                    Assert(
                        branchFrames[^1].Data.SequenceEqual(
                            new byte[] { 0x04, 0x01, (byte)(ptcIndex + 2), 0x01 }),
                        $"PTC{ptcIndex + 1} 协议编号");
                    supplyControl.ProcessFrame(new CanFrame(
                        0x05,
                        false,
                        false,
                        2,
                        [0x02, 0x01],
                        DateTime.Now,
                        FrameDirection.Rx));
                }
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    8,
                    [0x03, 0x03, 0x05, 0x00, 0x00, 0x00, 0x00, 0x01],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(ptcStates[0], "PTC1 状态上传协议确认开启");
                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    8,
                    [0x03, 0x03, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(!ptcStates[3], "PTC4 状态上传协议确认关闭");

                supplyControl.ProcessFrame(new CanFrame(
                    0x06,
                    false,
                    false,
                    2,
                    [0x12, 0x34],
                    DateTime.Now,
                    FrameDirection.Rx));
                supplyControl.ProcessFrame(new CanFrame(
                    0x07,
                    false,
                    false,
                    8,
                    [0x00, 0x64, 0x00, 0xC8, 0x01, 0x2C, 0x01, 0x90],
                    DateTime.Now,
                    FrameDirection.Rx));
                supplyControl.ProcessFrame(new CanFrame(
                    0x08,
                    false,
                    false,
                    8,
                    [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08],
                    DateTime.Now,
                    FrameDirection.Rx));
                Pt1000TemperatureConverter.TryConvertRaw(258, out var temperature1);
                Pt1000TemperatureConverter.TryConvertRaw(1800, out var temperature4);
                Assert(
                    supplyControl.GetMainPressureText() == "4660" &&
                    supplyControl.GetPressureText(1) == "100" &&
                    supplyControl.GetPressureText(2) == "200" &&
                    supplyControl.GetPressureText(3) == "300" &&
                    supplyControl.GetPressureText(4) == "400" &&
                    supplyControl.GetTemperatureText(1) == $"{temperature1:0.0}°C" &&
                    supplyControl.GetTemperatureText(4) == $"{temperature4:0.0}°C",
                    "储供界面显示压力原始值和 PT1000 温度");
                supplyControl.ProcessFrame(new CanFrame(
                    0x08,
                    false,
                    false,
                    8,
                    [0x03, 0xE8, 0x03, 0xE8, 0x03, 0xE8, 0x03, 0xE8],
                    DateTime.Now,
                    FrameDirection.Rx));
                Pt1000TemperatureConverter.TryConvertRaw(1000, out var updatedTemperature);
                Assert(
                    Enumerable.Range(1, 4)
                        .All(branch =>
                            supplyControl.GetTemperatureText(branch) ==
                            $"{updatedTemperature:0.0}°C"),
                    "连续收到温度报文时实时刷新四路温度");

                var sensorTimeoutMethod = typeof(SupplyControlModule).GetMethod(
                    "CheckSensorTimeouts",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert(sensorTimeoutMethod is not null, "传感器超时接口");
                foreach (var fieldName in new[]
                         {
                             "_lastMainPressureAt",
                             "_lastBranchPressureAt",
                             "_lastTemperatureAt"
                         })
                {
                    typeof(SupplyControlModule).GetField(
                            fieldName,
                            BindingFlags.NonPublic | BindingFlags.Instance)!
                        .SetValue(supplyControl, DateTime.Now.AddSeconds(-2.2));
                }
                sensorTimeoutMethod!.Invoke(supplyControl, [DateTime.Now]);
                Assert(
                    supplyControl.GetMainPressureText() == "--" &&
                    Enumerable.Range(1, 4)
                        .All(branch =>
                            supplyControl.GetPressureText(branch) == "--" &&
                            supplyControl.GetTemperatureText(branch) == "--"),
                    "超过 2 秒未收到传感器报文后清空旧值");

                supplyControl.ProcessFrame(new CanFrame(
                    0x06,
                    false,
                    false,
                    2,
                    [0x00, 0x00],
                    DateTime.Now,
                    FrameDirection.Rx));
                supplyControl.ProcessFrame(new CanFrame(
                    0x07,
                    false,
                    false,
                    8,
                    [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(
                    supplyControl.GetMainPressureText() == "--" &&
                    Enumerable.Range(1, 4)
                        .All(branch => supplyControl.GetPressureText(branch) == "--"),
                    "传感器数据 0000 时显示 --");

                supplyControl.ProcessFrame(new CanFrame(
                    0x08,
                    false,
                    false,
                    8,
                    [0x0F, 0xFF, 0x0F, 0xFF, 0x0F, 0xFF, 0x0F, 0xFF],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(
                    Enumerable.Range(1, 4)
                        .All(branch =>
                            supplyControl.GetTemperatureText(branch) == "未连接传感器"),
                    "PT1000 数字量持续为 4095 时显示未连接传感器");

                supplyControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x01, 0x03],
                    DateTime.Now,
                    FrameDirection.Rx));
                supplyControl.ProcessFrame(new CanFrame(
                    0x123,
                    false,
                    false,
                    1,
                    [0x7F],
                    DateTime.Now,
                    FrameDirection.Rx));
                var responseTypes = supplySchematic!.ResponseLog
                    .Select(entry => entry.Type)
                    .ToHashSet();
                Assert(
                    responseTypes.IsSupersetOf(new[]
                    {
                        ResponseLogType.ConnectionSn,
                        ResponseLogType.HeartbeatAck,
                        ResponseLogType.Ack,
                        ResponseLogType.Nak,
                        ResponseLogType.StatusUpload,
                        ResponseLogType.PressureReport,
                        ResponseLogType.TemperatureReport,
                        ResponseLogType.Other
                    }),
                    "上传应答日志分类");

                supplyControl.SetSelfLockValveState("开启");
                supplyControl.SetMainPressure("3.5 MPa");
                supplyControl.SetSolenoidRatio(1, "50%");
                supplyControl.SetPressure(1, "3.2 MPa");
                supplyControl.SetTemperature(1, "25.6°C");
            }

            using (var reconnectControl = new SupplyControlModule())
            {
                var reconnectFrames = new List<CanFrame>();
                reconnectControl.SetCanAvailable(true);
                reconnectControl.SendAction = frame =>
                {
                    reconnectFrames.Add(frame);
                    return null;
                };
                var connectionField = typeof(SupplyControlModule).GetField(
                    "_connection",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var connection = (SupplyConnectionModule?)connectionField?.GetValue(reconnectControl);
                var selfLockMethod = typeof(SupplyControlModule).GetMethod(
                    "SendSelfLockCommand",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert(
                    connection is not null && selfLockMethod is not null,
                    "连接丢失自动关闭测试接口");

                connection!.StartConnection();
                reconnectControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    8,
                    [0x01, 0x02, 0x00, 0x00, 0x78, 0xC2, 0x75, 0x91],
                    DateTime.Now,
                    FrameDirection.Rx));
                selfLockMethod!.Invoke(reconnectControl, [true]);
                reconnectControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x02, 0x01],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(
                    reconnectControl.IsSelfLockOpen &&
                    reconnectControl.IsGasFlowEnabled,
                    "重新连接前自锁阀开启");

                connection.DisconnectLink();
                Assert(
                    !reconnectControl.IsSelfLockOpen &&
                    !reconnectControl.IsGasFlowEnabled &&
                    reconnectControl.GetSolenoidDuty(1) == 0 &&
                    reconnectControl.GetSolenoidDuty(2) == 0 &&
                    reconnectControl.GetSolenoidDuty(3) == 0 &&
                    reconnectControl.GetSolenoidDuty(4) == 0,
                    "连接丢失后自动关闭自锁阀并归零四路");

                var frameCountAfterLoss = reconnectFrames.Count;
                selfLockMethod.Invoke(reconnectControl, [true]);
                Assert(
                    reconnectFrames.Count == frameCountAfterLoss &&
                    !reconnectControl.IsSelfLockOpen,
                    "未重新连接时不能打开自锁阀");

                connection.StartConnection();
                reconnectControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    8,
                    [0x01, 0x02, 0x00, 0x00, 0x78, 0xC2, 0x75, 0x91],
                    DateTime.Now,
                    FrameDirection.Rx));
                selfLockMethod.Invoke(reconnectControl, [true]);
                reconnectControl.ProcessFrame(new CanFrame(
                    0x05,
                    false,
                    false,
                    2,
                    [0x02, 0x01],
                    DateTime.Now,
                    FrameDirection.Rx));
                Assert(reconnectControl.IsSelfLockOpen, "重新连接后可以再次打开自锁阀");
            }

            using var dockTestWorkspace = new DockableWorkspace { Size = new Size(900, 620) };
            using var dockTestHost = new DockableModuleHost("test", "测试模块", new Panel());
            dockTestWorkspace.AddModule(dockTestHost, new Rectangle(10, 10, 400, 250));
            dockTestWorkspace.DockModule(dockTestHost, DockSide.Right);
            Assert(
                dockTestHost.Bounds.Right <= dockTestWorkspace.ClientSize.Width &&
                dockTestHost.Bounds.Height > 250,
                "模块右侧停靠布局");
            dockTestWorkspace.DockModule(dockTestHost, DockSide.Maximize);
            Assert(
                dockTestHost.Bounds.Width >= dockTestWorkspace.ClientSize.Width - 10,
                "模块最大化布局");
            var minimizeMethod = typeof(DockableModuleHost).GetMethod(
                "ToggleMinimize",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var maximizeMethod = typeof(DockableModuleHost).GetMethod(
                "ToggleMaximize",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert(
                minimizeMethod is not null && maximizeMethod is not null,
                "模块独立最小化和最大化按钮");
            minimizeMethod!.Invoke(dockTestHost, null);
            Assert(dockTestHost.IsMinimized, "模块最小化");
            maximizeMethod!.Invoke(dockTestHost, null);
            Assert(dockTestHost.IsMaximized && !dockTestHost.IsMinimized, "模块最大化");
            maximizeMethod.Invoke(dockTestHost, null);
            Assert(!dockTestHost.IsMaximized, "模块还原");

            using var zeroWorkspace = new DockableWorkspace { Size = Size.Empty };
            using var zeroHost = new DockableModuleHost("zero-size", "零尺寸检查", new Panel());
            zeroHost.MinimumSize = new Size(250, 150);
            zeroWorkspace.AddModule(zeroHost, Rectangle.Empty);
            var zeroBounds = zeroWorkspace.SnapBounds(
                zeroHost,
                new Rectangle(0, 0, 250, 150),
                false);
            Assert(
                zeroBounds.Width >= 250 && zeroBounds.Height >= 150,
                "工作区尺寸为 0 时不触发 Clamp 异常");

            using var blankForm = new MainForm();
            var blankDisplayMenu = blankForm.MainMenuStrip?.Items
                .OfType<ToolStripMenuItem>()
                .FirstOrDefault(item => item.Text == "显示");
            var pressurePlotTitles = new[]
            {
                "主气压传感器",
                "支路 1 气压传感器",
                "支路 2 气压传感器",
                "支路 3 气压传感器",
                "支路 4 气压传感器"
            };
            Assert(
                blankDisplayMenu?.DropDownItems
                    .OfType<ToolStripMenuItem>()
                    .Where(item => item.Text is "储供控制" or "发送帧")
                    .All(item => !item.Checked) == true,
                "默认模块应全部隐藏");
            RenderForm(blankForm, "ui-blank-preview.png");

            using var mainForm = new MainForm();
            var plotModules = (PlotModule[]?)typeof(MainForm)
                .GetField("_plotModules", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainForm);
            Assert(
                plotModules is not null &&
                plotModules.Length == 5 &&
                plotModules.Select(module => module.Title).SequenceEqual(pressurePlotTitles),
                "五个气压传感器绘图模块");
            var pressureWindowManager = typeof(MainForm)
                .GetField(
                    "_pressurePlotWindowManager",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainForm);
            Assert(pressureWindowManager is not null, "压力波形独立窗口管理器");
            var pressureWindowMethod = pressureWindowManager!.GetType().GetMethod(
                "SetWindowVisible",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert(pressureWindowMethod is not null, "压力波形窗口打开接口");
            pressureWindowMethod!.Invoke(pressureWindowManager, [0, true]);
            var pressureWindows = (Form?[]?)pressureWindowManager.GetType()
                .GetField("_windows", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(pressureWindowManager);
            Assert(
                pressureWindows?[0] is { Visible: true } &&
                pressureWindows[0]!.MinimumSize.Width >= 500,
                "压力波形永久独立窗口");
            Assert(
                plotModules!
                    .SelectMany(EnumerateControls)
                    .OfType<Label>()
                    .All(label =>
                        !label.Text.Contains("平均值", StringComparison.Ordinal) &&
                        !label.Text.Contains("最大值", StringComparison.Ordinal) &&
                        !label.Text.Contains("最小值", StringComparison.Ordinal)),
                "气压绘图不显示平均值、最大值和最小值");
            Assert(
                plotModules!
                    .SelectMany(EnumerateControls)
                    .OfType<Button>()
                    .Select(button => button.Text)
                    .ToHashSet()
                    .IsSupersetOf(new[] { "Y轴放大", "X轴放大", "恢复" }),
                "气压绘图坐标放大和恢复工具");
            var chart = plotModules![0].Chart;
            for (var index = 0; index < 40; index++)
            {
                var value = 1800 + 900 * Math.Sin(index * 0.23) + index * 30;
                foreach (var plotModule in plotModules)
                {
                    plotModule.AppendValue(value + plotModule.Title.Length);
                }
            }
            var appendPressurePlotValuesMethod = typeof(MainForm).GetMethod(
                "AppendPressurePlotValues",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert(appendPressurePlotValuesMethod is not null, "气压绘图报文分流接口");
            SensorReportParser.TryParse(mainPressureReport, out var mainPlotReport);
            SensorReportParser.TryParse(branchPressureReport, out var branchPlotReport);
            var mainPointCount = plotModules[0].Chart.PointCount;
            var branchPointCount = plotModules[1].Chart.PointCount;
            appendPressurePlotValuesMethod!.Invoke(mainForm, [mainPlotReport]);
            appendPressurePlotValuesMethod.Invoke(mainForm, [branchPlotReport]);
            Assert(
                plotModules[0].Chart.PointCount == mainPointCount + 1 &&
                Enumerable.Range(1, 4)
                    .All(index => plotModules[index].Chart.PointCount == branchPointCount + 1),
                "0x06/0x07 报文进入对应气压绘图");

            var workspace = (DockableWorkspace?)typeof(MainForm)
                .GetField("_workspace", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainForm);
            var receiveHost = (DockableModuleHost?)typeof(MainForm)
                .GetField("_receiveHost", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainForm);
            var tickerHost = (DockableModuleHost?)typeof(MainForm)
                .GetField("_tickerHost", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainForm);
            var supplyHost = (DockableModuleHost?)typeof(MainForm)
                .GetField("_supplyControlHost", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainForm);
            var sendHost = (DockableModuleHost?)typeof(MainForm)
                .GetField("_sendHost", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainForm);
            var receiveModeType = typeof(MainForm).GetNestedType(
                "ReceiveDisplayMode",
                BindingFlags.NonPublic);
            var setReceiveModeMethod = typeof(MainForm).GetMethod(
                "SetReceiveDisplayMode",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var ticker = (FrameTickerControl?)typeof(MainForm)
                .GetField("_tickerModule", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainForm);
            var receiveModeField = typeof(MainForm).GetField(
                "_receiveDisplayMode",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var connectionPanel = (ConnectionDockPanel?)typeof(MainForm)
                .GetField("_connectionPanel", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainForm);
            Assert(
                workspace is not null &&
                receiveHost is not null &&
                tickerHost is not null &&
                supplyHost is not null &&
                sendHost is not null &&
                receiveModeType is not null &&
                setReceiveModeMethod is not null &&
                ticker is not null &&
                receiveModeField is not null &&
                connectionPanel is not null &&
                receiveHost.PreventDocking &&
                tickerHost.PreventDocking &&
                supplyHost.PreventDocking &&
                sendHost.PreventDocking &&
                !receiveHost.ShowModuleHeader &&
                !tickerHost.ShowModuleHeader &&
                !supplyHost.ShowModuleHeader &&
                !sendHost.ShowModuleHeader,
                "未找到可停靠模块控件");
            Assert(
                connectionPanel!.Dock == DockStyle.Left &&
                connectionPanel.CurrentOptions.CanBitrate == 1_000_000,
                "USB-CAN 盒通信配置永久显示在主界面左侧");
            Assert(
                EnumerateControls(connectionPanel)
                    .OfType<Label>()
                    .All(label =>
                        !label.Text.Contains("设备 SN", StringComparison.Ordinal) &&
                        !label.Text.Contains("开发板", StringComparison.Ordinal)),
                "主界面只显示 USB-CAN 盒通信状态");

            setReceiveModeMethod!.Invoke(
                mainForm,
                [Enum.Parse(receiveModeType!, "Collapsed")]);
            Assert(
                receiveModeField!.GetValue(mainForm)?.ToString() == "Collapsed",
                "接收区完全折叠状态");
            Assert(
                !workspace!.IsModuleVisible(receiveHost!) &&
                !workspace.IsModuleVisible(tickerHost!),
                "接收区完全折叠时两个接收窗口均隐藏");

            setReceiveModeMethod.Invoke(
                mainForm,
                [Enum.Parse(receiveModeType!, "Ticker")]);
            Assert(
                receiveModeField!.GetValue(mainForm)?.ToString() == "Ticker",
                "接收区单条滚动状态");
            Assert(
                tickerHost!.IsFloating && tickerHost.Parent is Form,
                "单条信息窗口永久独立");

            setReceiveModeMethod.Invoke(
                mainForm,
                [Enum.Parse(receiveModeType!, "Expanded")]);
            Assert(
                receiveModeField!.GetValue(mainForm)?.ToString() == "Expanded",
                "接收区全展开状态");
            Assert(
                receiveHost!.IsFloating && receiveHost.Parent is Form,
                "接收帧窗口永久独立");

            var receiveModule = (ReceiveFramesModule?)typeof(MainForm)
                .GetField("_receiveModule", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainForm);
            var receiveGrid = (DataGridView?)typeof(ReceiveFramesModule)
                .GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(receiveModule);
            var idFilterEnabled = (CheckBox?)typeof(ReceiveFramesModule)
                .GetField("_idFilterEnabled", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(receiveModule);
            var idFilterText = (TextBox?)typeof(ReceiveFramesModule)
                .GetField("_idFilterText", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(receiveModule);
            var dlcFilterEnabled = (CheckBox?)typeof(ReceiveFramesModule)
                .GetField("_dlcFilterEnabled", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(receiveModule);
            var dlcFilterValue = (NumericUpDown?)typeof(ReceiveFramesModule)
                .GetField("_dlcFilterValue", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(receiveModule);
            Assert(
                receiveModule is not null &&
                receiveGrid is not null &&
                idFilterEnabled is not null &&
                idFilterText is not null &&
                dlcFilterEnabled is not null &&
                dlcFilterValue is not null,
                "接收帧 ID/DLC 过滤控件");
            idFilterEnabled!.Checked = true;
            idFilterText!.Text = "0x05";
            receiveModule!.AddFrame(new CanFrame(
                0x05,
                false,
                false,
                2,
                [0x01, 0x03],
                DateTime.Now,
                FrameDirection.Rx));
            receiveModule.AddFrame(new CanFrame(
                0x06,
                false,
                false,
                2,
                [0x12, 0x34],
                DateTime.Now,
                FrameDirection.Rx));
            Assert(
                receiveGrid!.Rows.Count == 1,
                "接收帧 ID 过滤");
            idFilterEnabled.Checked = false;
            dlcFilterEnabled!.Checked = true;
            dlcFilterValue!.Value = 8;
            receiveModule.AddFrame(new CanFrame(
                0x06,
                false,
                false,
                8,
                [1, 2, 3, 4, 5, 6, 7, 8],
                DateTime.Now,
                FrameDirection.Rx));
            Assert(
                receiveGrid.Rows.Count == 1,
                "接收帧 DLC 过滤");
            if (receiveHost.Parent is Form floatingReceiveForm)
            {
                Assert(
                    floatingReceiveForm.MinimizeBox &&
                    floatingReceiveForm.MaximizeBox,
                    "独立接收窗使用 Windows 原生最小化和最大化按钮");
                RenderForm(floatingReceiveForm, "receive-window-preview.png");
            }

            var displayMenu = mainForm.MainMenuStrip?.Items
                .OfType<ToolStripMenuItem>()
                .FirstOrDefault(item => item.Text == "显示");
            Assert(
                mainForm.MainMenuStrip?.Items
                    .OfType<ToolStripMenuItem>()
                    .Any(item => item.Text == "选项") == false,
                "通信配置和连接操作不再放在选项菜单");
            Assert(
                displayMenu?.DropDownItems
                    .OfType<ToolStripMenuItem>()
                    .Any(item => item.Text == "储供控制") == true,
                "储供控制显示菜单");
            var sendVisibilityItem = displayMenu?.DropDownItems
                .OfType<ToolStripMenuItem>()
                .FirstOrDefault(item => item.Text == "发送帧");
            var applyVisibilityMethod = typeof(MainForm).GetMethod(
                "ApplyModuleVisibility",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert(
                sendVisibilityItem is not null &&
                displayMenu?.DropDownItems
                    .OfType<ToolStripMenuItem>()
                    .Any(item => pressurePlotTitles.Contains(item.Text)) == false &&
                applyVisibilityMethod is not null,
                "未找到显示菜单");
            sendVisibilityItem!.Checked = false;
            applyVisibilityMethod!.Invoke(mainForm, [sendVisibilityItem]);
            sendVisibilityItem.Checked = true;
            applyVisibilityMethod.Invoke(mainForm, [sendVisibilityItem]);

            var sendModuleHost = sendHost!;
            workspace!.SetModuleVisible(sendModuleHost, true);
            Assert(
                workspace.FloatModule(sendModuleHost, new Point(160, 160)) &&
                sendModuleHost.IsFloating &&
                sendModuleHost.Parent is Form,
                "发送帧模块可浮动为独立窗口");
            var floatingSendForm = (Form)sendModuleHost.Parent!;
            floatingSendForm.Close();
            Application.DoEvents();
            Assert(
                !floatingSendForm.Visible &&
                sendModuleHost.IsFloating &&
                sendVisibilityItem.Checked == false,
                "关闭浮动窗口后隐藏模块并同步显示菜单");
            sendVisibilityItem.Checked = true;
            applyVisibilityMethod.Invoke(mainForm, [sendVisibilityItem]);
            Assert(floatingSendForm.Visible, "重新勾选后可恢复浮动窗口");
            workspace.DockFloatingModule(sendModuleHost);
            Assert(
                sendModuleHost.IsFloating &&
                sendModuleHost.Parent is Form,
                "发送帧窗口永久独立且不能停靠回主窗口");

            foreach (var module in workspace.Modules)
            {
                workspace.SetModuleVisible(module, true);
                Assert(
                    workspace.FloatModule(module, new Point(120, 120)) &&
                    module.IsFloating,
                    $"模块 {module.ModuleTitle} 独立浮动");
                if (module.PreventDocking)
                {
                    workspace.DockFloatingModule(module);
                    Assert(
                        module.IsFloating,
                        $"模块 {module.ModuleTitle} 禁止停靠回主窗口");
                    continue;
                }
                workspace.DockFloatingModule(module);
                Assert(
                    !module.IsFloating && module.Parent == workspace,
                    $"模块 {module.ModuleTitle} 重新停靠");
            }

            using var floatingPreviewForm = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Size = new Size(900, 620),
                BackColor = AppTheme.Background
            };
            using var floatingPreviewWorkspace = new DockableWorkspace { Dock = DockStyle.Fill };
            using var floatingPreviewPlot = new PlotModule("主气压传感器");
            using var floatingPreviewHost = new DockableModuleHost(
                "floating-preview",
                "主气压传感器",
                floatingPreviewPlot);
            floatingPreviewWorkspace.AddModule(
                floatingPreviewHost,
                new Rectangle(20, 20, 520, 360));
            floatingPreviewHost.Bounds = new Rectangle(20, 20, 520, 360);
            floatingPreviewWorkspace.SetModuleVisible(floatingPreviewHost, true);
            floatingPreviewForm.Controls.Add(floatingPreviewWorkspace);
            floatingPreviewForm.Show();
            Application.DoEvents();
            Assert(
                floatingPreviewWorkspace.FloatModule(
                    floatingPreviewHost,
                    new Point(300, 220)),
                "独立窗口预览创建");
            var detachedPreview = (Form)floatingPreviewHost.Parent!;
            for (var index = 0; index < 28; index++)
            {
                floatingPreviewPlot.AppendValue(1700 + 700 * Math.Sin(index * 0.2) + index * 18);
            }
            var previewZoomButton = EnumerateControls(floatingPreviewPlot)
                .OfType<Button>()
                .First(button => button.Text == "Y轴放大");
            previewZoomButton.PerformClick();
            var previewChart = floatingPreviewPlot.Chart;
            previewChart.PerformLayout();
            var previewPlotBounds = (RectangleF?)typeof(LineChartControl)
                .GetField("_plotBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(previewChart);
            if (previewPlotBounds.HasValue)
            {
                typeof(LineChartControl)
                    .GetField("_selectionStart", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(
                        previewChart,
                        new Point(
                            (int)previewPlotBounds.Value.Left + 20,
                            (int)previewPlotBounds.Value.Top + 35));
                typeof(LineChartControl)
                    .GetField("_selectionCurrent", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(
                        previewChart,
                        new Point(
                            (int)previewPlotBounds.Value.Right - 20,
                            (int)previewPlotBounds.Value.Bottom - 35));
                previewChart.Invalidate();
            }
            detachedPreview.Refresh();
            RenderForm(detachedPreview, "floating-module-preview.png");
            floatingPreviewWorkspace.CloseAllFloatingWindows();

            var settingsMenu = mainForm.MainMenuStrip?.Items
                .OfType<ToolStripMenuItem>()
                .FirstOrDefault(item => item.Text == "设置");
            var themeMenu = settingsMenu?.DropDownItems
                .OfType<ToolStripMenuItem>()
                .FirstOrDefault(item => item.Text == "主题");
            Assert(
                themeMenu?.DropDownItems
                    .OfType<ToolStripMenuItem>()
                    .Select(item => item.Text)
                    .SequenceEqual(new[] { "深色", "浅色" }) == true,
                "设置菜单主题选项");

            workspace!.Size = new Size(900, 650);
            var snapped = workspace.SnapBounds(receiveHost!, new Rectangle(-5, 3, 350, 240), false);
            Assert(snapped.X == 0 && snapped.Y == 0, "模块拖动边缘吸附");

            RenderForm(mainForm, "ui-preview.png");

            using var pressurePlotsForm = new Form
            {
                Text = "主气压传感器",
                Size = new Size(900, 560),
                BackColor = AppTheme.Background
            };
            using var pressurePlotPreview = new PlotModule("主气压传感器")
            {
                Dock = DockStyle.Fill
            };
            for (var index = 0; index < 30; index++)
            {
                pressurePlotPreview.Chart.AddPoint(index, 1000 + index * 35);
            }
            pressurePlotsForm.Controls.Add(pressurePlotPreview);
            ThemeApplicator.Apply(pressurePlotsForm);
            RenderForm(pressurePlotsForm, "pressure-plots-preview.png");

            using var maximizeModule = new SupplyControlModule { Dock = DockStyle.Fill };
            using var maximizeHost = new DockableModuleHost(
                "maximized-supply",
                "储供控制",
                maximizeModule);
            using var maximizeWorkspace = new DockableWorkspace { Dock = DockStyle.Fill };
            maximizeWorkspace.AddModule(maximizeHost, new Rectangle(10, 10, 700, 600));
            using var maximizeForm = new Form
            {
                Text = "储供控制最大化检查",
                StartPosition = FormStartPosition.Manual,
                Size = new Size(1660, 950),
                BackColor = AppTheme.Background
            };
            maximizeForm.Controls.Add(maximizeWorkspace);
            ThemeApplicator.Apply(maximizeForm);
            var maximizeMethodForPreview = typeof(DockableModuleHost).GetMethod(
                "ToggleMaximize",
                BindingFlags.NonPublic | BindingFlags.Instance);
            RenderForm(
                maximizeForm,
                "supply-maximized-preview.png",
                () => maximizeMethodForPreview!.Invoke(maximizeHost, null));

            using var supplyControlPreview = new SupplyControlModule { Dock = DockStyle.Fill };
            var toggleConnectionMethod = typeof(SupplyControlModule).GetMethod(
                "ToggleConnectionSection",
                BindingFlags.NonPublic | BindingFlags.Instance);
            toggleConnectionMethod?.Invoke(supplyControlPreview, null);
            using var supplyControlForm = new Form
            {
                Text = "储供控制",
                StartPosition = FormStartPosition.Manual,
                Size = new Size(620, 680),
                BackColor = AppTheme.Background
            };
            supplyControlForm.Controls.Add(supplyControlPreview);
            ThemeApplicator.Apply(supplyControlForm);
            RenderForm(supplyControlForm, "supply-control-preview.png");

            using var ptcControlPreview = new SupplyControlModule { Dock = DockStyle.Fill };
            var ptcTabs = FindControl<TabControl>(ptcControlPreview);
            if (ptcTabs is not null)
            {
                ptcTabs.SelectedIndex = 1;
            }
            using var ptcControlForm = new Form
            {
                Text = "PTC 参数",
                StartPosition = FormStartPosition.Manual,
                Size = new Size(620, 680),
                BackColor = AppTheme.Background
            };
            ptcControlForm.Controls.Add(ptcControlPreview);
            ThemeApplicator.Apply(ptcControlForm);
            RenderForm(ptcControlForm, "ptc-parameters-preview.png");

            using var fullDiagramModule = new SupplyControlModule { Dock = DockStyle.Fill };
            fullDiagramModule.SetSelfLockValveState("开启");
            fullDiagramModule.SetSolenoidRatio(1, "50.0%");
            fullDiagramModule.SetSolenoidRatio(2, "25.0%");
            fullDiagramModule.SetTemperature(1, "25.6°C");
            fullDiagramModule.SetTemperature(2, "未连接传感器");
            typeof(SupplyControlModule).GetMethod(
                    "ApplyModeSwitch",
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(fullDiagramModule, [1, true]);
            var fullDiagramTargets = (uint?[])typeof(SupplyControlModule)
                .GetField(
                    "_confirmedPressureTargets",
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(fullDiagramModule)!;
            fullDiagramTargets[0] = 12_345;
            fullDiagramModule.ProcessFrame(new CanFrame(
                0x05,
                false,
                false,
                8,
                [0x03, 0x03, 0x01, 0x00, 0x00, 0x00, 0x01, 0xF4],
                DateTime.Now,
                FrameDirection.Rx));
            typeof(SupplyControlModule).GetMethod(
                    "ApplyModeSwitch",
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(fullDiagramModule, [5, true]);
            fullDiagramModule.ProcessFrame(new CanFrame(
                0x05,
                false,
                false,
                8,
                [0x03, 0x03, 0x05, 0x00, 0x00, 0x00, 0x00, 0x01],
                DateTime.Now,
                FrameDirection.Rx));
            var fullDiagramSchematic = (SupplySchematicControl?)typeof(SupplyControlModule)
                .GetField("_schematic", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(fullDiagramModule);
            fullDiagramSchematic!.SetGasFlowEnabled(true);
            var fullDiagramSurface = (Control?)typeof(SupplySchematicControl)
                .GetField("_surface", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(fullDiagramSchematic);
            if (fullDiagramSurface is not null)
            {
                fullDiagramSurface.GetType().GetField(
                        "_selfLockVisualOpen",
                        BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(fullDiagramSurface, 1f);
                var fullDiagramPercents = (float[])fullDiagramSurface.GetType().GetField(
                        "_solenoidVisualPercent",
                        BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(fullDiagramSurface)!;
                fullDiagramPercents[0] = 50f;
                fullDiagramPercents[1] = 25f;
                fullDiagramSurface.GetType().GetField(
                        "_valveAnimationPhase",
                        BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(fullDiagramSurface, 0.35f);
            }
            using var fullDiagramForm = new Form
            {
                Text = "储供控制 - 气路全图",
                StartPosition = FormStartPosition.Manual,
                Size = new Size(1900, 1000),
                BackColor = AppTheme.Background
            };
            fullDiagramForm.Controls.Add(fullDiagramModule);
            ThemeApplicator.Apply(fullDiagramForm);
            RenderForm(fullDiagramForm, "supply-diagram-preview.png");

            AppTheme.SetTheme(AppThemeMode.Light);
            using var lightForm = new MainForm();
            var lightChart = ((PlotModule[]?)typeof(MainForm)
                    .GetField("_plotModules", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(lightForm))
                ?[0]
                .Chart;
            Assert(
                AppTheme.IsLight &&
                lightForm.BackColor.ToArgb() == Color.FromArgb(242, 245, 249).ToArgb(),
                "浅色主题");
            for (var index = 0; index < 40; index++)
            {
                var value = 1800 + 900 * Math.Sin(index * 0.23) + index * 30;
                lightChart!.AddPoint(index, value);
            }
            RenderForm(lightForm, "ui-light-preview.png");
            AppTheme.SetTheme(AppThemeMode.Dark);

            using var tickerForm = new MainForm();
            var tickerControl = (FrameTickerControl?)typeof(MainForm)
                .GetField("_tickerModule", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(tickerForm);
            tickerControl!.ShowFrame(new CanFrame(
                0x321,
                false,
                false,
                8,
                [0x01, 0xAB, 0xCD, 0x00, 0x11, 0x22, 0x33, 0x44],
                DateTime.Now,
                FrameDirection.Rx));
            setReceiveModeMethod.Invoke(
                tickerForm,
                [Enum.Parse(receiveModeType!, "Ticker")]);
            RenderForm(tickerForm, "ticker-preview.png");

            using var settingsForm = new ConnectionSettingsForm(
                ConnectionOptions.CreateDefault(VciAdapter.FindControlCanDll()),
                false,
                string.Empty,
                _ => null,
                () => { });
            RenderForm(settingsForm, "settings-preview.png");

            using var aboutForm = new AboutForm();
            Assert(
                EnumerateControls(aboutForm)
                    .OfType<LinkLabel>()
                    .Any(link =>
                        link.Text ==
                        "https://github.com/cloudc9999-stack/CG_HostComputer"),
                "关于窗口显示最新版本链接");

            using var plotSettingsForm = new PlotSettingsForm(
                chart!.YMinimum,
                chart.YMaximum,
                chart.TimeWindowSeconds);
            RenderForm(plotSettingsForm, "plot-settings-preview.png");
        }
        catch (Exception ex)
        {
            renderError = ex;
        }
    });
    renderThread.SetApartmentState(ApartmentState.STA);
    renderThread.Start();
    renderThread.Join();

    if (renderError is not null)
    {
        throw new InvalidOperationException("界面离屏渲染失败。", renderError);
    }
}

static IEnumerable<Control> EnumerateControls(Control parent)
{
    foreach (Control child in parent.Controls)
    {
        yield return child;
        foreach (var nested in EnumerateControls(child))
        {
            yield return nested;
        }
    }
}

static T? FindControl<T>(Control parent, Func<T, bool>? predicate = null) where T : Control
{
    foreach (Control child in parent.Controls)
    {
        if (child is T match && (predicate is null || predicate(match)))
        {
            return match;
        }
        var nested = FindControl(child, predicate);
        if (nested is not null)
        {
            return nested;
        }
    }
    return null;
}
