namespace CanHost.Models;

public sealed class CanProtocolV1Parser
{
    private int _state;
    private byte _highByte;

    public void Reset()
    {
        _state = 0;
        _highByte = 0;
    }

    public bool TryConsume(CanFrame frame, out ushort value)
    {
        value = 0;
        if (frame.Direction != FrameDirection.Rx || frame.Remote || frame.Data.Length == 0)
        {
            return false;
        }

        // Common protocol layout for this controller: one CAN frame contains
        // 01 <high byte> <low byte>. Emit immediately so every received protocol
        // frame updates the chart.
        if (frame.Data.Length >= 3 && frame.Data[0] == 0x01)
        {
            value = (ushort)((frame.Data[1] << 8) | frame.Data[2]);
            _state = 0;
            _highByte = 0;
            return true;
        }

        // Compatibility for the original three-frame form: a frame whose first
        // data byte is 01, followed by separate high-byte and low-byte frames.
        var sample = frame.Data[0];
        if (_state == 0)
        {
            if (sample == 0x01)
            {
                _state = 1;
            }
            return false;
        }

        if (_state == 1)
        {
            _highByte = sample;
            _state = 2;
            return false;
        }

        value = (ushort)((_highByte << 8) | sample);
        _state = 0;
        _highByte = 0;
        return true;
    }
}
