using System.Diagnostics;
using System.Globalization;
using CanHost.Models;

namespace CanHost.Adapters;

public sealed class VciAdapter : ICanAdapter
{
    private const uint ControlCanDeviceType = 4;
    private readonly object _commandLock = new();
    private Process? _bridge;
    private StreamReader? _bridgeOutput;
    private StreamWriter? _bridgeInput;
    private CancellationTokenSource? _pollCancellation;
    private Task? _pollTask;
    private volatile bool _connected;

    public event EventHandler<CanFrameReceivedEventArgs>? FrameReceived;
    public event EventHandler<string>? AdapterError;

    public bool IsConnected => _connected;
    public string AdapterName => "创芯科技 ControlCAN";

    public static string? FindControlCanDll()
    {
        var localPath = Path.Combine(AppContext.BaseDirectory, "ControlCAN.dll");
        return File.Exists(localPath) ? localPath : null;
    }

    public static string? FindBridgeExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "CanHost.VciBridge.exe"),
            Path.Combine(AppContext.BaseDirectory, "VciBridge", "CanHost.VciBridge.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public void Connect(AdapterSettings settings)
    {
        Disconnect();

        var dllPath = settings.ControlCanDllPath;
        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
        {
            throw new InvalidOperationException(
                "未找到 ControlCAN.dll。请将 ControlCAN.dll 复制到本程序所在目录。");
        }

        var bridgePath = FindBridgeExecutable();
        if (string.IsNullOrWhiteSpace(bridgePath) || !File.Exists(bridgePath))
        {
            throw new InvalidOperationException(
                "缺少 32 位桥接程序 CanHost.VciBridge.exe，无法加载 x86 版 ControlCAN.dll。请重新解压完整发布包。");
        }

        var timing = settings.CanBitrate switch
        {
            1_000_000 => 0x1400,
            800_000 => 0x1600,
            500_000 => 0x1C00,
            250_000 => 0x1C01,
            125_000 => 0x1C03,
            100_000 => 0x1C04,
            50_000 => 0x1C09,
            20_000 => 0x1C18,
            10_000 => 0x1C31,
            _ => throw new InvalidOperationException("ControlCAN 不支持所选 CAN 波特率。")
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = bridgePath,
            Arguments = $"--dll \"{dllPath.Replace("\"", string.Empty)}\"",
            WorkingDirectory = Path.GetDirectoryName(dllPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = System.Text.Encoding.ASCII,
            StandardOutputEncoding = System.Text.Encoding.ASCII,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        _bridge = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 ControlCAN 32 位桥接程序。");
        _bridgeInput = _bridge.StandardInput;
        _bridgeOutput = _bridge.StandardOutput;

        lock (_commandLock)
        {
            var ready = _bridgeOutput.ReadLine();
            if (ready != "READY")
            {
                var error = _bridge.StandardError.ReadToEnd();
                StopBridge();
                throw new InvalidOperationException(
                    $"ControlCAN 桥接程序启动失败：{ready ?? error}".Trim());
            }
        }

        var canIndex = (uint)Math.Clamp(settings.Channel, 1, 2) - 1;
        try
        {
            ExecuteCommand(
                $"OPEN {ControlCanDeviceType} 0 {canIndex} {timing}",
                emitFrames: false);
        }
        catch (Exception ex)
        {
            StopBridge();
            throw new InvalidOperationException(TranslateError(ex.Message), ex);
        }
        _connected = true;
        _pollCancellation = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoop(_pollCancellation.Token));
    }

    public void Send(CanFrame frame)
    {
        if (!_connected)
        {
            throw new InvalidOperationException("ControlCAN 适配器尚未连接。");
        }

        var hex = frame.Data.Length == 0
            ? "-"
            : Convert.ToHexString(frame.Data);
        try
        {
            ExecuteCommand(
                $"SEND {frame.Id} {(frame.Extended ? 1 : 0)} {(frame.Remote ? 1 : 0)} {Math.Clamp((int)frame.Dlc, 0, 8)} {hex}",
                emitFrames: false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(TranslateError(ex.Message), ex);
        }
    }

    private void PollLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _connected)
        {
            try
            {
                var received = ExecuteCommand("POLL 128 10", emitFrames: true);
                if (received == 0)
                {
                    cancellationToken.WaitHandle.WaitOne(2);
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    AdapterError?.Invoke(this, $"ControlCAN 接收中断：{TranslateError(ex.Message)}");
                }
                break;
            }
        }
    }

    private int ExecuteCommand(string command, bool emitFrames)
    {
        if (_bridgeInput is null || _bridgeOutput is null || _bridge is null || _bridge.HasExited)
        {
            throw new IOException("ControlCAN 桥接程序已退出。");
        }

        lock (_commandLock)
        {
            try
            {
                _bridgeInput.WriteLine(command);
                _bridgeInput.Flush();

                while (true)
                {
                    var line = _bridgeOutput.ReadLine();
                    if (line is null)
                    {
                        throw new IOException("ControlCAN 桥接程序没有响应。");
                    }

                    if (line.StartsWith("RX ", StringComparison.Ordinal))
                    {
                        if (emitFrames && TryParseFrame(line, out var frame) && frame is not null)
                        {
                            FrameReceived?.Invoke(this, new CanFrameReceivedEventArgs(frame));
                        }
                        continue;
                    }

                    if (line.StartsWith("DONE ", StringComparison.Ordinal))
                    {
                        return int.TryParse(
                            line.AsSpan(5),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var count)
                            ? count
                            : 0;
                    }

                    if (line.StartsWith("OK ", StringComparison.Ordinal))
                    {
                        return 0;
                    }

                    if (line.StartsWith("ERR ", StringComparison.Ordinal))
                    {
                        throw new IOException(line);
                    }

                    if (line.Length > 0)
                    {
                        throw new IOException($"ControlCAN 返回未知响应：{line}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                StopBridge();
                throw;
            }
        }
    }

    private static bool TryParseFrame(string line, out CanFrame? frame)
    {
        frame = null;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 7 ||
            !uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var extended) ||
            !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var remote) ||
            !byte.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dlc))
        {
            return false;
        }

        byte[] data = [];
        if (parts[5] != "-")
        {
            try
            {
                data = Convert.FromHexString(parts[5]);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        frame = new CanFrame(
            id,
            extended != 0,
            remote != 0,
            dlc,
            data,
            DateTime.Now,
            FrameDirection.Rx);
        return true;
    }

    private static string TranslateError(string message)
    {
        if (message.Contains("E00", StringComparison.Ordinal))
        {
            return "未找到 ControlCAN.dll，请将其复制到程序所在目录。";
        }
        if (message.Contains("E10", StringComparison.Ordinal))
        {
            return "无法打开 USB-CAN 设备，可能被 USB_CAN TOOL 占用或驱动未安装。";
        }
        if (message.Contains("E11", StringComparison.Ordinal))
        {
            return "CAN 通道初始化失败，请检查通道号和波特率。";
        }
        if (message.Contains("E12", StringComparison.Ordinal))
        {
            return "CAN 通道启动失败。";
        }
        if (message.Contains("E14", StringComparison.Ordinal))
        {
            return "USB-CAN 设备已断开。";
        }
        if (message.Contains("E16", StringComparison.Ordinal))
        {
            return "CAN 发送失败，通常表示总线无其他节点应答或总线关闭。";
        }
        if (message.Contains("E99", StringComparison.Ordinal) &&
            message.Contains("BadImageFormatException", StringComparison.OrdinalIgnoreCase))
        {
            return "ControlCAN.dll 架构不匹配。请使用软件安装目录中的 32 位 DLL。";
        }
        return message;
    }

    public void Disconnect()
    {
        _connected = false;
        _pollCancellation?.Cancel();

        try
        {
            _pollTask?.Wait(500);
        }
        catch
        {
            // Ignore polling shutdown errors.
        }

        try
        {
            if (_bridgeInput is not null)
            {
                _bridgeInput.WriteLine("CLOSE");
                _bridgeInput.Flush();
                _bridge?.WaitForExit(500);
            }
        }
        catch
        {
            // The helper may already be gone.
        }

        StopBridge();
        _pollCancellation?.Dispose();
        _pollCancellation = null;
        _pollTask = null;
    }

    private void StopBridge()
    {
        try
        {
            if (_bridge is { HasExited: false })
            {
                _bridge.Kill();
                _bridge.WaitForExit(500);
            }
        }
        catch
        {
            // Ignore shutdown failures.
        }

        try
        {
            _bridgeInput?.Dispose();
            _bridgeOutput?.Dispose();
            _bridge?.Dispose();
        }
        catch
        {
            // Ignore pipe disposal errors.
        }

        _bridgeInput = null;
        _bridgeOutput = null;
        _bridge = null;
    }

    public void Dispose()
    {
        Disconnect();
    }
}
