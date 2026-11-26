using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CanHostVciBridge
{
    internal static class Program
    {
        private static bool _opened;
        private static uint _deviceType;
        private static uint _deviceIndex;
        private static uint _canIndex;

        [STAThread]
        private static int Main(string[] args)
        {
            Console.InputEncoding = Encoding.ASCII;
            Console.OutputEncoding = Encoding.ASCII;

            var dllPath = FindArgument(args, "--dll");
            if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
            {
                WriteError("E00", "ControlCAN.dll_NOT_FOUND");
                return 2;
            }

            var dllDirectory = Path.GetDirectoryName(Path.GetFullPath(dllPath));
            if (!string.IsNullOrEmpty(dllDirectory))
            {
                SetDllDirectory(dllDirectory);
                Directory.SetCurrentDirectory(dllDirectory);
            }

            Console.WriteLine("READY");
            Console.Out.Flush();

            string line;
            while ((line = Console.ReadLine()) != null)
            {
                try
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0)
                    {
                        continue;
                    }

                    switch (parts[0].ToUpperInvariant())
                    {
                        case "OPEN":
                            Open(parts);
                            break;
                        case "POLL":
                            Poll(parts);
                            break;
                        case "SEND":
                            Send(parts);
                            break;
                        case "CLOSE":
                            Close();
                            Console.WriteLine("OK CLOSED");
                            Console.Out.Flush();
                            return 0;
                        case "PING":
                            Console.WriteLine("OK PONG");
                            Console.Out.Flush();
                            break;
                        default:
                            WriteError("E03", "UNKNOWN_COMMAND");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    WriteError("E99", ex.GetType().Name + ":" + ex.Message);
                }
            }

            Close();
            return 0;
        }

        private static void Open(string[] parts)
        {
            if (parts.Length != 5)
            {
                WriteError("E04", "OPEN_USAGE");
                return;
            }

            _deviceType = ParseUInt(parts[1]);
            _deviceIndex = ParseUInt(parts[2]);
            _canIndex = ParseUInt(parts[3]);
            var timing = ParseUInt(parts[4]);
            var timing0 = (byte)(timing & 0xFF);
            var timing1 = (byte)((timing >> 8) & 0xFF);

            Close();

            var openResult = VCI_OpenDevice(_deviceType, _deviceIndex, 0);
            if (openResult != 1)
            {
                WriteError("E10", "VCI_OpenDevice:" + openResult.ToString(CultureInfo.InvariantCulture));
                return;
            }
            _opened = true;

            var config = new VCI_INIT_CONFIG
            {
                AccCode = 0x80000000,
                AccMask = 0xFFFFFFFF,
                Reserved = 0,
                Filter = 1,
                Timing0 = timing0,
                Timing1 = timing1,
                Mode = 0
            };

            var initResult = VCI_InitCAN(_deviceType, _deviceIndex, _canIndex, ref config);
            if (initResult != 1)
            {
                Close();
                WriteError("E11", "VCI_InitCAN:" + initResult.ToString(CultureInfo.InvariantCulture));
                return;
            }

            VCI_ClearBuffer(_deviceType, _deviceIndex, _canIndex);
            var startResult = VCI_StartCAN(_deviceType, _deviceIndex, _canIndex);
            if (startResult != 1)
            {
                Close();
                WriteError("E12", "VCI_StartCAN:" + startResult.ToString(CultureInfo.InvariantCulture));
                return;
            }

            Console.WriteLine(
                "OK OPENED " +
                timing0.ToString("X2", CultureInfo.InvariantCulture) +
                timing1.ToString("X2", CultureInfo.InvariantCulture));
            Console.Out.Flush();
        }

        private static void Poll(string[] parts)
        {
            if (!_opened)
            {
                WriteError("E13", "DEVICE_NOT_OPEN");
                return;
            }

            var size = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 128;
            var waitTime = parts.Length > 2 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 10;
            size = Math.Max(1, Math.Min(size, 2000));

            var messages = new VCI_CAN_OBJ[size];
            var result = VCI_Receive(
                _deviceType,
                _deviceIndex,
                _canIndex,
                messages,
                (uint)size,
                waitTime);

            if (result == uint.MaxValue)
            {
                WriteError("E14", "USB_DEVICE_DISCONNECTED");
                return;
            }
            if (result > (uint)size)
            {
                WriteError("E15", "VCI_Receive_INVALID_COUNT:" + result.ToString(CultureInfo.InvariantCulture));
                return;
            }

            for (var index = 0; index < result; index++)
            {
                var message = messages[index];
                var dataLength = Math.Max(0, Math.Min(8, (int)message.DataLen));
                var hex = new StringBuilder(dataLength * 2);
                for (var dataIndex = 0; dataIndex < dataLength; dataIndex++)
                {
                    hex.Append(message.Data[dataIndex].ToString("X2", CultureInfo.InvariantCulture));
                }

                Console.WriteLine(
                    "RX " +
                    message.ID.ToString(CultureInfo.InvariantCulture) + " " +
                    message.ExternFlag.ToString(CultureInfo.InvariantCulture) + " " +
                    message.RemoteFlag.ToString(CultureInfo.InvariantCulture) + " " +
                    dataLength.ToString(CultureInfo.InvariantCulture) + " " +
                    (hex.Length == 0 ? "-" : hex.ToString()) + " " +
                    message.TimeStamp.ToString(CultureInfo.InvariantCulture));
            }

            Console.WriteLine("DONE " + result.ToString(CultureInfo.InvariantCulture));
            Console.Out.Flush();
        }

        private static void Send(string[] parts)
        {
            if (!_opened)
            {
                WriteError("E13", "DEVICE_NOT_OPEN");
                return;
            }
            if (parts.Length != 6)
            {
                WriteError("E05", "SEND_USAGE");
                return;
            }

            var id = ParseUInt(parts[1]);
            var extended = parts[2] == "1";
            var remote = parts[3] == "1";
            var dataLength = int.Parse(parts[4], CultureInfo.InvariantCulture);
            if (dataLength < 0 || dataLength > 8)
            {
                WriteError("E06", "INVALID_DLC");
                return;
            }

            var message = new VCI_CAN_OBJ
            {
                ID = id,
                TimeStamp = 0,
                TimeFlag = 0,
                SendType = 0,
                RemoteFlag = (byte)(remote ? 1 : 0),
                ExternFlag = (byte)(extended ? 1 : 0),
                DataLen = (byte)dataLength,
                Data = new byte[8],
                Reserved = new byte[3]
            };

            if (!remote && dataLength > 0 && parts[5] != "-")
            {
                var data = parts[5];
                if (data.Length != dataLength * 2)
                {
                    WriteError("E07", "DATA_LENGTH_MISMATCH");
                    return;
                }
                for (var index = 0; index < dataLength; index++)
                {
                    message.Data[index] = byte.Parse(
                        data.Substring(index * 2, 2),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture);
                }
            }

            var messages = new[] { message };
            var result = VCI_Transmit(
                _deviceType,
                _deviceIndex,
                _canIndex,
                messages,
                1);

            if (result == 1)
            {
                Console.WriteLine("OK TRANSMITTED");
                Console.Out.Flush();
            }
            else if (result == uint.MaxValue)
            {
                WriteError("E14", "USB_DEVICE_DISCONNECTED");
            }
            else
            {
                WriteError("E16", "VCI_Transmit:" + result.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void Close()
        {
            if (_opened)
            {
                try
                {
                    VCI_CloseDevice(_deviceType, _deviceIndex);
                }
                catch
                {
                    // The USB device may already be unplugged.
                }
                _opened = false;
            }
        }

        private static uint ParseUInt(string value)
        {
            uint result;
            if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                return result;
            }
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result))
            {
                return result;
            }
            throw new FormatException("Invalid integer: " + value);
        }

        private static string FindArgument(string[] args, string name)
        {
            for (var index = 0; index < args.Length - 1; index++)
            {
                if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }
            return null;
        }

        private static void WriteError(string code, string message)
        {
            Console.WriteLine("ERR " + code + " " + message.Replace('\r', ' ').Replace('\n', ' '));
            Console.Out.Flush();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VCI_INIT_CONFIG
        {
            public uint AccCode;
            public uint AccMask;
            public uint Reserved;
            public byte Filter;
            public byte Timing0;
            public byte Timing1;
            public byte Mode;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VCI_CAN_OBJ
        {
            public uint ID;
            public uint TimeStamp;
            public byte TimeFlag;
            public byte SendType;
            public byte RemoteFlag;
            public byte ExternFlag;
            public byte DataLen;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] Data;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public byte[] Reserved;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string pathName);

        [DllImport("ControlCAN.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint VCI_OpenDevice(uint deviceType, uint deviceIndex, uint reserved);

        [DllImport("ControlCAN.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint VCI_CloseDevice(uint deviceType, uint deviceIndex);

        [DllImport("ControlCAN.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint VCI_InitCAN(
            uint deviceType,
            uint deviceIndex,
            uint canIndex,
            ref VCI_INIT_CONFIG initConfig);

        [DllImport("ControlCAN.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint VCI_StartCAN(uint deviceType, uint deviceIndex, uint canIndex);

        [DllImport("ControlCAN.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint VCI_Transmit(
            uint deviceType,
            uint deviceIndex,
            uint canIndex,
            VCI_CAN_OBJ[] messages,
            uint length);

        [DllImport("ControlCAN.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint VCI_Receive(
            uint deviceType,
            uint deviceIndex,
            uint canIndex,
            [Out] VCI_CAN_OBJ[] messages,
            uint length,
            int waitTime);

        [DllImport("ControlCAN.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint VCI_ClearBuffer(
            uint deviceType,
            uint deviceIndex,
            uint canIndex);
    }
}
