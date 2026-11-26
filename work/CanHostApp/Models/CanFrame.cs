namespace CanHost.Models;

public enum FrameDirection
{
    Rx,
    Tx
}

public sealed record CanFrame(
    uint Id,
    bool Extended,
    bool Remote,
    byte Dlc,
    byte[] Data,
    DateTime Timestamp,
    FrameDirection Direction)
{
    public string IdText => Extended ? $"{Id:X8}" : $"{Id:X3}";

    public string TypeText => Remote
        ? Extended ? "扩展远程帧" : "标准远程帧"
        : Extended ? "扩展帧" : "标准帧";

    public string DataText => Remote
        ? "(远程帧)"
        : string.Join(" ", Data.Select(value => value.ToString("X2")));
}

public sealed record AdapterSettings(
    string Kind,
    string Port,
    int CanBitrate,
    int SerialBitrate,
    int Channel,
    string? ControlCanDllPath = null);
