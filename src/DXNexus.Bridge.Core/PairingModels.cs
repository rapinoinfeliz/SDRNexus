using System.Text.Json.Serialization;

namespace DXNexus.Bridge.Core;

public sealed record PairingClientInfo(
    string BridgeVersion,
    string PluginVersion,
    int SdrSharpRevision);

public sealed record PairingStartRequest(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("deviceName")] string DeviceName,
    [property: JsonPropertyName("publicKeyJwk")] PublicDeviceJwk PublicKeyJwk,
    [property: JsonPropertyName("requestedScopes")] string[] RequestedScopes,
    [property: JsonPropertyName("client")] PairingClientInfo Client);

public sealed record PublicDeviceJwk(
    [property: JsonPropertyName("kty")] string Kty,
    [property: JsonPropertyName("crv")] string Crv,
    [property: JsonPropertyName("x")] string X,
    [property: JsonPropertyName("y")] string Y);

public sealed record PairingStartResponse(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("deviceCode")] string DeviceCode,
    [property: JsonPropertyName("userCode")] string UserCode,
    [property: JsonPropertyName("verificationUri")] Uri VerificationUri,
    [property: JsonPropertyName("expiresInSeconds")] int ExpiresInSeconds,
    [property: JsonPropertyName("pollIntervalSeconds")] int PollIntervalSeconds);

public sealed record PairingTokenResponse(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("deviceId")] Guid DeviceId,
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("refreshToken")] string RefreshToken,
    [property: JsonPropertyName("tokenType")] string TokenType,
    [property: JsonPropertyName("expiresInSeconds")] int ExpiresInSeconds,
    [property: JsonPropertyName("scopes")] string[] Scopes);

public sealed record DeviceCredential(
    Guid DeviceId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    string[] Scopes,
    byte[] PrivateKeyPkcs8);

public sealed class PairingPendingException(string message, TimeSpan retryAfter) : Exception(message)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
