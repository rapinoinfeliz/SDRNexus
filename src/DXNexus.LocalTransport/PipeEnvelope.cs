using System.Text.Json;

namespace DXNexus.LocalTransport;

public sealed record PipeEnvelope(
    string Protocol,
    string Type,
    Guid Id,
    long Sequence,
    DateTimeOffset SentAt,
    string SessionNonce,
    JsonElement Payload)
{
    public static PipeEnvelope Create<T>(string type, long sequence, string sessionNonce, T payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionNonce);
        return new PipeEnvelope(
            Contracts.Protocol.Version,
            type,
            Guid.CreateVersion7(),
            sequence,
            DateTimeOffset.UtcNow,
            sessionNonce,
            JsonSerializer.SerializeToElement(payload, PipeJson.Options));
    }
}

internal static class PipeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
    };
}

