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
            else if (message.Type == "context.station-logo")
            {
                var logo = message.Payload.Deserialize<StationLogoImage>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (logo is not null) StationLogoReceived?.Invoke(this, logo);
            }
            else if (message.Type == "context.error")
            {
                var error = message.Payload.Deserialize<BridgeError>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (error is not null) ContextErrorReceived?.Invoke(this, error);
            }
            else if (message.Type == "bridge.status")
            {
                var status = message.Payload.Deserialize<BridgeServiceStatus>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (status is not null) BridgeStatusReceived?.Invoke(this, status);
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
    public event EventHandler<StationLogoImage>? StationLogoReceived;
    public event EventHandler<BridgeError>? ContextErrorReceived;
    public event EventHandler<BridgeServiceStatus>? BridgeStatusReceived;
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
    public Task RequestRemoteTuningAsync(long sequence, CancellationToken cancellationToken = default) =>
        _client.SendAsync(
            "command.remote-tune.request",
            sequence,
            new RemoteTuningAuthorizationRequest(Guid.CreateVersion7()),
            cancellationToken);
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
