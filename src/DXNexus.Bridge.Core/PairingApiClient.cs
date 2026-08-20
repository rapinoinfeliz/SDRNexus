using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DXNexus.Bridge.Core;

public sealed class PairingApiClient(HttpClient httpClient)
{
    public static readonly Uri ProductionBaseUri = new("https://dxnexus.rapinoinfeliz.workers.dev/api/sdr/v1/");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = httpClient;

    public async Task<PairingSession> StartAsync(
        string deviceName,
        PairingClientInfo client,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(false);
        var publicJwk = new PublicDeviceJwk(
            "EC",
            "P-256",
            Base64Url(parameters.Q.X ?? throw new CryptographicException("Missing P-256 X coordinate.")),
            Base64Url(parameters.Q.Y ?? throw new CryptographicException("Missing P-256 Y coordinate.")));
        var request = new PairingStartRequest(
            "pairing.start.request",
            deviceName.Trim(),
            publicJwk,
            ["sdr:state:write", "sdr:context:read", "logbook:create", "wishlist:write"],
            client);
        using var response = await _httpClient.PostAsJsonAsync("pairing/start", request, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var start = await response.Content.ReadFromJsonAsync<PairingStartResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("DXNexus returned an empty pairing response.");
        return new PairingSession(start, key.ExportPkcs8PrivateKey(), publicJwk);
    }

    public async Task<DeviceCredential> PollAsync(
        PairingSession pairing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairing);
        var endpoint = new Uri(_httpClient.BaseAddress ?? ProductionBaseUri, "pairing/token");
        var proof = CreateNonceProof(pairing.PrivateKeyPkcs8, pairing.PublicKey, endpoint, pairing.Start.DeviceCode);
        using var response = await _httpClient.PostAsJsonAsync(
            "pairing/token",
            new { type = "pairing.token.request", deviceCode = pairing.Start.DeviceCode, proof },
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is (HttpStatusCode)428 or HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter?.Delta
                ?? TimeSpan.FromSeconds(Math.Max(5, pairing.Start.PollIntervalSeconds));
            throw new PairingPendingException("Waiting for approval in DXNexus.", retry);
        }
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<PairingTokenResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("DXNexus returned an empty device credential.");
        if (!string.Equals(token.TokenType, "DPoP", StringComparison.Ordinal))
        {
            throw new InvalidDataException("DXNexus returned an unsupported token type.");
        }
        return new DeviceCredential(
            token.DeviceId,
            token.AccessToken,
            token.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(token.ExpiresInSeconds),
            token.Scopes,
            pairing.PrivateKeyPkcs8.ToArray());
    }

    public static string CreateNonceProof(
        byte[] privateKeyPkcs8,
        PublicDeviceJwk publicJwk,
        Uri endpoint,
        string deviceCode,
        DateTimeOffset? now = null,
        string? jti = null)
    {
        ArgumentNullException.ThrowIfNull(privateKeyPkcs8);
        ArgumentNullException.ThrowIfNull(publicJwk);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceCode);
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(
            new { alg = "ES256", typ = "dpop+jwt", jwk = publicJwk }, JsonOptions));
        var proofTime = now ?? DateTimeOffset.UtcNow;
        var canonicalEndpoint = endpoint.GetLeftPart(UriPartial.Authority) + endpoint.AbsolutePath;
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            htm = "POST",
            htu = canonicalEndpoint,
            iat = proofTime.ToUnixTimeSeconds(),
            jti = jti ?? Guid.NewGuid().ToString(),
            nonce = deviceCode,
        }, JsonOptions));
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);
        var signature = key.SignData(
            Encoding.ASCII.GetBytes($"{header}.{payload}"),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{header}.{payload}.{Base64Url(signature)}";
    }

    public static string CreateAccessProof(
        byte[] privateKeyPkcs8,
        Uri endpoint,
        HttpMethod method,
        string accessToken,
        DateTimeOffset? now = null,
        string? jti = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);
        var parameters = key.ExportParameters(false);
        var publicJwk = new PublicDeviceJwk(
            "EC",
            "P-256",
            Base64Url(parameters.Q.X ?? throw new CryptographicException("Missing P-256 X coordinate.")),
            Base64Url(parameters.Q.Y ?? throw new CryptographicException("Missing P-256 Y coordinate.")));
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(
            new { alg = "ES256", typ = "dpop+jwt", jwk = publicJwk }, JsonOptions));
        var proofTime = now ?? DateTimeOffset.UtcNow;
        var canonicalEndpoint = endpoint.GetLeftPart(UriPartial.Authority) + endpoint.AbsolutePath;
        var accessTokenHash = SHA256.HashData(Encoding.ASCII.GetBytes(accessToken));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            htm = method.Method.ToUpperInvariant(),
            htu = canonicalEndpoint,
            iat = proofTime.ToUnixTimeSeconds(),
            jti = jti ?? Guid.NewGuid().ToString(),
            ath = Base64Url(accessTokenHash),
        }, JsonOptions));
        var signature = key.SignData(
            Encoding.ASCII.GetBytes($"{header}.{payload}"),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{header}.{payload}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

public sealed class PairingSession(
    PairingStartResponse start,
    byte[] privateKeyPkcs8,
    PublicDeviceJwk publicKey) : IDisposable
{
    public PairingStartResponse Start { get; } = start;
    public byte[] PrivateKeyPkcs8 { get; } = privateKeyPkcs8;
    public PublicDeviceJwk PublicKey { get; } = publicKey;

    public void Dispose() => CryptographicOperations.ZeroMemory(PrivateKeyPkcs8);
}
