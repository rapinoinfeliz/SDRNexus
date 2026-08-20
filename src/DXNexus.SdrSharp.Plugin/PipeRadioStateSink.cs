using DXNexus.Contracts;
using DXNexus.LocalTransport;
using DXNexus.Plugin.Core;
using System.Text.Json;

namespace DXNexus.SdrSharp.Plugin;

internal sealed class PipeRadioStateSink : IRadioStateSink
{
    private readonly PluginBridgeClient _client;

    public PipeRadioStateSink(string pipeName)
    {
        _client = new PluginBridgeClient(pipeName);
        _client.MessageReceived += (_, message) =>
        {
            MessageReceived?.Invoke(this, message);
            if (message.Type != "context.candidates") return;
            var candidates = message.Payload.Deserialize<CandidateContextResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (candidates is not null) CandidatesReceived?.Invoke(this, candidates);
        };
    }

    public bool IsConnected => _client.IsConnected;
    public event EventHandler<PipeEnvelope>? MessageReceived;
    public event EventHandler<CandidateContextResponse>? CandidatesReceived;
    public Task ConnectAsync(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);
    public Task SendAsync(SequencedRadioSnapshot snapshot, CancellationToken cancellationToken) =>
        _client.SendAsync("radio.snapshot", snapshot.Sequence, snapshot, cancellationToken);
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
