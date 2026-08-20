using DXNexus.Contracts;
using DXNexus.LocalTransport;
using DXNexus.Plugin.Core;

namespace DXNexus.SdrSharp.Plugin;

internal sealed class PipeRadioStateSink : IRadioStateSink
{
    private readonly PluginBridgeClient _client;

    public PipeRadioStateSink(string pipeName)
    {
        _client = new PluginBridgeClient(pipeName);
    }

    public bool IsConnected => _client.IsConnected;
    public Task ConnectAsync(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);
    public Task SendAsync(SequencedRadioSnapshot snapshot, CancellationToken cancellationToken) =>
        _client.SendAsync("radio.snapshot", snapshot.Sequence, snapshot, cancellationToken);
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}

