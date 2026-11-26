using System.Runtime.InteropServices;
using CanHost.Models;

namespace CanHost.Adapters;

public sealed class PcanAdapter : ICanAdapter
{
    private const ushort PcanUsbBus1 = 0x51;
    private const uint PcanErrorOk = 0x00000;
    private const uint PcanErrorQrcvEmpty = 0x00020;

    private readonly object _sendLock = new();
    private ushort _channel = PcanUsbBus1;
    private CancellationTokenSource? _readCancellation;
    private Task? _readTask;
    private volatile bool _connected;

    public event EventHandler<CanFrameReceivedEventArgs>? FrameReceived;
    public event EventHandler<string>? AdapterError;

    public bool IsConnected => _connected;
    public string AdapterName => "PEAK PCAN";

    public void Connect(AdapterSettings settings)
    {
        Disconnect();

        _channel = checked((ushort)(PcanUsbBus1 + Math.Clamp(settings.Channel, 1, 4) - 1));
        var bitrateCode = settings.CanBitrate switch
        {
            1_000_000 => 0x0014,
            800_000 => 0x0016,
            500_000 => 0x001C,
            250_000 => 0x011C,
            125_000 => 0x031C,
            100_000 => 0x432F,
            50_000 => 0x472F,
            20_000 => 0x532F,
            10_000 => 0x672F,
            _ => throw new InvalidOperationException("PCAN 不支持所选 CAN 波特率。")
        };

        uint result;
        try
        {
            result = CanInitialize(_channel, (ushort)bitrateCode, 0, 0, 0);
        }
        catch (DllNotFoundException)
        {
            throw new InvalidOperationException(
                "未找到 PCANBasic.dll。请安装 PEAK PCAN-Basic 驱动，并确认驱动与上位机同为 64 位。");
        }
        catch (BadImageFormatException)
        {
            throw new InvalidOperationException(
                "PCANBasic.dll 位数不匹配。请安装 64 位 PEAK PCAN-Basic 驱动。");
        }

        if (result != PcanErrorOk)
        {
            throw new InvalidOperationException($"PCAN 初始化失败：{DescribePcanError(result)}");
        }

        _connected = true;
        _readCancellation = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoop(_readCancellation.Token));
    }

    public void Send(CanFrame frame)
    {
        if (!_connected)
        {
            throw new InvalidOperationException("PCAN 适配器尚未连接。");
        }

        var data = new byte[8];
        Array.Copy(frame.Data, data, Math.Min(frame.Data.Length, 8));
        var message = new TpcanMsg
        {
            Id = frame.Id,
            Type = frame.Remote
                ? frame.Extended ? (byte)2 : (byte)1
                : frame.Extended ? (byte)2 : (byte)0,
            Len = (byte)Math.Clamp((int)frame.Dlc, 0, 8),
            Data = data
        };

        uint result;
        lock (_sendLock)
        {
            result = CanWrite(_channel, ref message);
        }

        if (result != PcanErrorOk)
        {
            throw new InvalidOperationException($"PCAN 发送失败：{DescribePcanError(result)}");
        }
    }

    private void ReadLoop(CancellationToken cancellationToken)
    {
        var lastError = string.Empty;

        while (!cancellationToken.IsCancellationRequested && _connected)
        {
            var result = CanRead(_channel, out var message, IntPtr.Zero);
            if (result == PcanErrorOk)
            {
                var extended = (message.Type & 0x02) != 0;
                var remote = (message.Type & 0x01) != 0;
                var data = message.Data.Take(Math.Min((int)message.Len, 8)).ToArray();
                var frame = new CanFrame(
                    message.Id,
                    extended,
                    remote,
                    message.Len,
                    data,
                    DateTime.Now,
                    FrameDirection.Rx);
                FrameReceived?.Invoke(this, new CanFrameReceivedEventArgs(frame));
                continue;
            }

            if (result != PcanErrorQrcvEmpty)
            {
                var error = DescribePcanError(result);
                if (!string.Equals(error, lastError, StringComparison.Ordinal))
                {
                    AdapterError?.Invoke(this, error);
                    lastError = error;
                }
            }

            cancellationToken.WaitHandle.WaitOne(8);
        }
    }

    public void Disconnect()
    {
        if (!_connected && _readCancellation is null)
        {
            return;
        }

        _connected = false;
        _readCancellation?.Cancel();

        try
        {
            _readTask?.Wait(500);
        }
        catch
        {
            // Ignore receive task shutdown failures.
        }

        try
        {
            CanUninitialize(_channel);
        }
        catch
        {
            // The native driver may already be unavailable.
        }

        _readCancellation?.Dispose();
        _readCancellation = null;
        _readTask = null;
    }

    private static string DescribePcanError(uint code) => code switch
    {
        0x00001 => "发送队列已满",
        0x00002 => "接收数据被覆盖",
        0x00004 => "CAN 总线警告（Bus Light）",
        0x00008 => "CAN 总线严重警告（Bus Heavy）",
        0x00010 => "CAN 总线关闭（Bus Off）",
        0x00040 => "接收队列溢出",
        0x00080 => "发送队列溢出",
        0x00100 => "硬件未接入",
        0x00200 => "硬件已接入但未初始化",
        0x00400 => "硬件被其他程序占用",
        0x00800 => "PCAN 驱动安装的版本过旧",
        0x02000 => "参数错误",
        0x04000 => "CAN 控制器离线",
        0x08000 => "硬件不被支持",
        0x10000 => "CAN 通道已打开",
        0x20000 => "CAN 通道未打开",
        0x40000 => "CAN 总线处于被动错误状态",
        _ => $"未知错误 0x{code:X}"
    };

    public void Dispose()
    {
        Disconnect();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TpcanMsg
    {
        public uint Id;
        public byte Type;
        public byte Len;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Data;
    }

    [DllImport("PCANBasic.dll", EntryPoint = "CAN_Initialize")]
    private static extern uint CanInitialize(
        ushort channel,
        ushort btr0Btr1,
        uint hardwareType,
        uint ioPort,
        ushort interrupt);

    [DllImport("PCANBasic.dll", EntryPoint = "CAN_Uninitialize")]
    private static extern uint CanUninitialize(ushort channel);

    [DllImport("PCANBasic.dll", EntryPoint = "CAN_Write")]
    private static extern uint CanWrite(ushort channel, ref TpcanMsg message);

    [DllImport("PCANBasic.dll", EntryPoint = "CAN_Read")]
    private static extern uint CanRead(
        ushort channel,
        out TpcanMsg message,
        IntPtr timestamp);
}
