using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using CanHost.Models;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace CanHost.Adapters;

public sealed class SlcanAdapter : ICanAdapter
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x80;
    private const uint PurgeTxClear = 0x0004;
    private const uint PurgeRxClear = 0x0008;

    private readonly object _writeLock = new();
    private readonly object _lifecycleLock = new();
    private FileStream? _stream;
    private Thread? _readThread;
    private CancellationTokenSource? _readCancellation;
    private volatile bool _connected;

    public event EventHandler<CanFrameReceivedEventArgs>? FrameReceived;
    public event EventHandler<string>? AdapterError;

    public bool IsConnected => _connected;
    public string AdapterName => "SLCAN";

    public static IReadOnlyList<string> GetPortNames()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key is null)
            {
                return Array.Empty<string>();
            }

            return key.GetValueNames()
                .Select(name => key.GetValue(name)?.ToString())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Connect(AdapterSettings settings)
    {
        lock (_lifecycleLock)
        {
            DisconnectCore(notifyDevice: false);

            if (string.IsNullOrWhiteSpace(settings.Port))
            {
                throw new InvalidOperationException("未找到可用串口，请先插入 USB-CAN 适配器并刷新端口。");
            }

            var slcanCode = settings.CanBitrate switch
            {
                10_000 => 0,
                20_000 => 1,
                50_000 => 2,
                100_000 => 3,
                125_000 => 4,
                250_000 => 5,
                500_000 => 6,
                800_000 => 7,
                1_000_000 => 8,
                _ => throw new InvalidOperationException("SLCAN 不支持所选 CAN 波特率。")
            };

            var handle = CreateFile(
                @"\\." + settings.Port,
                GenericRead | GenericWrite,
                0,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"无法打开串口 {settings.Port}。请检查适配器是否被其他软件占用。");
            }

            try
            {
                ConfigureSerialPort(handle, settings.SerialBitrate);
                _stream = new FileStream(handle, FileAccess.ReadWrite, 4096, isAsync: false);
            }
            catch
            {
                handle.Dispose();
                throw;
            }

            try
            {
                WriteCommand("C");
                WriteCommand($"S{slcanCode}");
                WriteCommand("O");
                Thread.Sleep(180);

                var response = new byte[512];
                var received = _stream.Read(response, 0, response.Length);
                if (received > 0 && response.AsSpan(0, received).Contains((byte)0x07))
                {
                    throw new InvalidOperationException(
                        "适配器拒绝了 CAN 打开命令。请检查 CAN 波特率、总线接线及 120 Ω 终端电阻。");
                }

                PurgeComm(handle, PurgeRxClear | PurgeTxClear);
            }
            catch
            {
                CloseStream();
                throw;
            }

            _connected = true;
            _readCancellation = new CancellationTokenSource();
            _readThread = new Thread(() => ReadLoop(_readCancellation.Token))
            {
                IsBackground = true,
                Name = "SLCAN receive"
            };
            _readThread.Start();
        }
    }

    public void Send(CanFrame frame)
    {
        if (!_connected || _stream is null)
        {
            throw new InvalidOperationException("CAN 适配器尚未连接。");
        }

        var dlc = Math.Clamp((int)frame.Dlc, 0, 8);
        var data = frame.Data.Take(dlc).ToArray();
        var hexData = Convert.ToHexString(data);

        string command;
        if (frame.Remote)
        {
            command = frame.Extended
                ? $"R{frame.Id:X8}{dlc}"
                : $"r{frame.Id:X3}{dlc}";
        }
        else
        {
            command = frame.Extended
                ? $"T{frame.Id:X8}{dlc}{hexData}"
                : $"t{frame.Id:X3}{dlc}{hexData}";
        }

        WriteCommand(command);
    }

    public void Disconnect()
    {
        lock (_lifecycleLock)
        {
            DisconnectCore(notifyDevice: true);
        }
    }

    private void DisconnectCore(bool notifyDevice)
    {
        _connected = false;
        var wasOpen = _stream is not null;

        if (notifyDevice && wasOpen)
        {
            try
            {
                WriteCommand("C");
            }
            catch
            {
                // The adapter may already be gone. Closing the handle is still required.
            }
        }

        _readCancellation?.Cancel();
        CloseStream();
        _readThread?.Join(600);
        _readThread = null;
        _readCancellation?.Dispose();
        _readCancellation = null;
    }

    private void ReadLoop(CancellationToken cancellationToken)
    {
        var buffer = new byte[2048];
        var pending = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested && _connected)
        {
            try
            {
                var count = _stream?.Read(buffer, 0, buffer.Length) ?? 0;
                if (count <= 0)
                {
                    continue;
                }

                pending.Append(Encoding.ASCII.GetString(buffer, 0, count));
                ProcessLines(pending);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (IOException ex)
            {
                if (_connected)
                {
                    AdapterError?.Invoke(this, $"串口读取中断：{ex.Message}");
                }
                break;
            }
            catch (Exception ex)
            {
                if (_connected)
                {
                    AdapterError?.Invoke(this, $"SLCAN 接收错误：{ex.Message}");
                }
                break;
            }
        }
    }

    private void ProcessLines(StringBuilder pending)
    {
        while (true)
        {
            var text = pending.ToString();
            var separator = text.IndexOfAny(['\r', '\n']);
            if (separator < 0)
            {
                if (pending.Length > 8192)
                {
                    pending.Clear();
                }
                return;
            }

            var line = text[..separator].Trim('\0', ' ');
            pending.Remove(0, separator + 1);

            if (line.Contains('\a'))
            {
                AdapterError?.Invoke(this, "SLCAN 返回 BEL：适配器无法发送，通常表示 CAN 总线无其他节点应答或总线错误。");
                continue;
            }

            if (TryParseFrame(line, out var frame) && frame is not null)
            {
                FrameReceived?.Invoke(this, new CanFrameReceivedEventArgs(frame));
            }
        }
    }

    private static bool TryParseFrame(string line, out CanFrame? frame)
    {
        frame = null;
        if (line.Length < 5)
        {
            return false;
        }

        var command = line[0];
        var extended = command is 'T' or 'R';
        var remote = command is 'r' or 'R';
        if (command is not ('t' or 'T' or 'r' or 'R'))
        {
            return false;
        }

        var idLength = extended ? 8 : 3;
        if (line.Length < idLength + 2)
        {
            return false;
        }

        if (!uint.TryParse(
                line.AsSpan(1, idLength),
                System.Globalization.NumberStyles.HexNumber,
                null,
                out var id))
        {
            return false;
        }

        var dlcPosition = 1 + idLength;
        if (!byte.TryParse(line.AsSpan(dlcPosition, 1), out var dlc) || dlc > 8)
        {
            return false;
        }

        var dataStart = dlcPosition + 1;
        var expectedHexLength = remote ? 0 : dlc * 2;
        if (line.Length < dataStart + expectedHexLength)
        {
            return false;
        }

        byte[] data = [];
        if (!remote && dlc > 0)
        {
            var hex = line.AsSpan(dataStart, expectedHexLength);
            try
            {
                data = Convert.FromHexString(hex);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        frame = new CanFrame(
            id,
            extended,
            remote,
            dlc,
            data,
            DateTime.Now,
            FrameDirection.Rx);
        return true;
    }

    private void WriteCommand(string command)
    {
        if (_stream is null)
        {
            throw new IOException("串口尚未打开。");
        }

        var bytes = Encoding.ASCII.GetBytes(command + "\r");
        lock (_writeLock)
        {
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
        }
    }

    private void CloseStream()
    {
        var stream = _stream;
        _stream = null;
        try
        {
            stream?.Dispose();
        }
        catch
        {
            // Ignore errors while closing a removed USB device.
        }
    }

    private static void ConfigureSerialPort(SafeFileHandle handle, int baudRate)
    {
        var dcb = new Dcb
        {
            DCBlength = (uint)Marshal.SizeOf<Dcb>(),
            BaudRate = (uint)baudRate,
            Flags = 0x00001011, // Binary mode, DTR enabled, RTS enabled.
            ByteSize = 8,
            Parity = 0, // NOPARITY
            StopBits = 0 // ONESTOPBIT
        };

        if (!GetCommState(handle, ref dcb))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "读取串口配置失败。");
        }

        dcb.DCBlength = (uint)Marshal.SizeOf<Dcb>();
        dcb.BaudRate = (uint)baudRate;
        dcb.Flags = 0x00001011;
        dcb.ByteSize = 8;
        dcb.Parity = 0;
        dcb.StopBits = 0;

        if (!SetCommState(handle, ref dcb))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "设置串口参数失败。");
        }

        var timeouts = new CommTimeouts
        {
            ReadIntervalTimeout = uint.MaxValue,
            ReadTotalTimeoutMultiplier = 0,
            ReadTotalTimeoutConstant = 50,
            WriteTotalTimeoutMultiplier = 0,
            WriteTotalTimeoutConstant = 500
        };

        if (!SetCommTimeouts(handle, ref timeouts))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "设置串口超时失败。");
        }

        PurgeComm(handle, PurgeRxClear | PurgeTxClear);
    }

    public void Dispose()
    {
        Disconnect();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dcb
    {
        public uint DCBlength;
        public uint BaudRate;
        public uint Flags;
        public ushort Reserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EvtChar;
        public ushort Reserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommTimeouts
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCommState(SafeFileHandle file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommState(SafeFileHandle file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommTimeouts(SafeFileHandle file, ref CommTimeouts timeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PurgeComm(SafeFileHandle file, uint flags);
}
