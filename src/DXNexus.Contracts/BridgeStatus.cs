namespace DXNexus.Contracts;

public sealed record BridgeError(
    string Code,
    string Message,
    bool Transient = true);

public sealed record BridgeServiceStatus(
    string Type,
    string Protocol,
    bool Paired,
    string CloudState,
    bool LiveEnabled,
    bool LiveConnected,
    DateTimeOffset? RemoteTuningUntil,
    string? Code,
    string Message);

public sealed record RemoteTuningAuthorizationRequest(Guid RequestId);
