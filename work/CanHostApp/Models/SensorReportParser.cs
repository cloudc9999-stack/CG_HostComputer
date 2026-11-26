namespace CanHost.Models;

public sealed record SensorReport(ushort Id, ushort[] Values);

public static class SensorReportParser
{
    public static bool TryParse(CanFrame frame, out SensorReport report)
    {
        report = default!;
        if (frame.Direction != FrameDirection.Rx ||
            frame.Remote ||
            frame.Data.Length == 0)
        {
            return false;
        }

        var valueCount = frame.Id switch
        {
            0x06 => 1,
            0x07 or 0x08 => 4,
            _ => 0
        };
        if (valueCount == 0 || frame.Data.Length < valueCount * 2)
        {
            return false;
        }

        var values = new ushort[valueCount];
        for (var index = 0; index < valueCount; index++)
        {
            values[index] = (ushort)(
                (frame.Data[index * 2] << 8) |
                frame.Data[index * 2 + 1]);
        }

        report = new SensorReport((ushort)frame.Id, values);
        return true;
    }
}
