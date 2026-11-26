namespace CanHost.Models;

public sealed record ConnectionOptions(
    int AdapterIndex,
    int CanBitrate,
    int Channel,
    string Port,
    int SerialBitrate,
    string? ControlCanDllPath)
{
    public static ConnectionOptions CreateDefault(string? controlCanDllPath) => new(
        AdapterIndex: 2,
        CanBitrate: 1_000_000,
        Channel: 1,
        Port: string.Empty,
        SerialBitrate: 115_200,
        ControlCanDllPath: controlCanDllPath);

    public string AdapterName => AdapterIndex switch
    {
        0 => "SLCAN",
        1 => "PEAK PCAN",
        2 => "创芯科技 ControlCAN",
        _ => "CAN 适配器"
    };

    public string BitrateText => CanBitrate switch
    {
        125_000 => "125 kbit/s",
        250_000 => "250 kbit/s",
        500_000 => "500 kbit/s",
        1_000_000 => "1 Mbit/s",
        _ => $"{CanBitrate} bit/s"
    };

    public string ChannelText => AdapterIndex switch
    {
        0 => Port,
        1 => $"USB {Channel}",
        2 => $"CAN {Channel}",
        _ => $"通道 {Channel}"
    };
}
