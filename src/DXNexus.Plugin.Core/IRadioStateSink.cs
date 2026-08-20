using DXNexus.Contracts;

namespace DXNexus.Plugin.Core;

public interface IRadioStateSink : IAsyncDisposable
{
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken);
    Task SendAsync(SequencedRadioSnapshot snapshot, CancellationToken cancellationToken);
}

public enum BridgeConnectionState
{
    Offline,
    Connecting,
    Connected,
}

