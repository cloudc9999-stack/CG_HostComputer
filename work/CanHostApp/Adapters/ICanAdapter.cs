using CanHost.Models;

namespace CanHost.Adapters;

public sealed class CanFrameReceivedEventArgs(CanFrame frame) : EventArgs
{
    public CanFrame Frame { get; } = frame;
}

public interface ICanAdapter : IDisposable
{
    event EventHandler<CanFrameReceivedEventArgs>? FrameReceived;
    event EventHandler<string>? AdapterError;

    bool IsConnected { get; }
    string AdapterName { get; }

    void Connect(AdapterSettings settings);
    void Disconnect();
    void Send(CanFrame frame);
}
