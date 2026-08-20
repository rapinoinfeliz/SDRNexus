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
            if (message.Type == "context.candidates")
            {
                var candidates = message.Payload.Deserialize<CandidateContextResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (candidates is not null) CandidatesReceived?.Invoke(this, candidates);
            }
            else if (message.Type == "command.result")
            {
                var result = message.Payload.Deserialize<CommandResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (result is not null) CommandCompleted?.Invoke(this, result);
            }
            else if (message.Type == "command.tune")
            {
                var command = message.Payload.Deserialize<RemoteTuneCommand>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (command is not null) TuneRequested?.Invoke(this, command);
            }
        };
    }

    public bool IsConnected => _client.IsConnected;
    public event EventHandler<PipeEnvelope>? MessageReceived;
    public event EventHandler<CandidateContextResponse>? CandidatesReceived;
    public event EventHandler<CommandResult>? CommandCompleted;
    public event EventHandler<RemoteTuneCommand>? TuneRequested;
    public Task ConnectAsync(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);
    public Task SendAsync(SequencedRadioSnapshot snapshot, CancellationToken cancellationToken) =>
        _client.SendAsync("radio.snapshot", snapshot.Sequence, snapshot, cancellationToken);
    public Task SetWishlistAsync(StationCandidate candidate, bool wishlisted, long sequence, CancellationToken cancellationToken = default) =>
        _client.SendAsync("command.wishlist", sequence, new WishlistCommand(Guid.CreateVersion7(), candidate, wishlisted), cancellationToken);
    public Task LogAsync(QuickLogCommand command, long sequence, CancellationToken cancellationToken = default) =>
        _client.SendAsync("command.logbook", sequence, command, cancellationToken);
    public Task SendTuneResultAsync(RemoteTuneResult result, long sequence, CancellationToken cancellationToken = default) =>
        _client.SendAsync("command.tune.result", sequence, result, cancellationToken);
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
