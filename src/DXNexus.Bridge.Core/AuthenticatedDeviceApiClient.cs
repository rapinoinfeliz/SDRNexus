using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DXNexus.Contracts;

namespace DXNexus.Bridge.Core;

public sealed class AuthenticatedDeviceApiClient(
    HttpClient httpClient,
    IDeviceCredentialStore credentialStore) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = httpClient;
    private readonly IDeviceCredentialStore _credentialStore = credentialStore;
    private readonly SemaphoreSlim _credentialGate = new(1, 1);
    private DeviceCredential? _credential;

    public async Task<DeviceConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "device"),
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<DeviceConnectionStatus>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("DXNexus returned an empty device status.");
    }

    public async Task<ReceptionSetupResponse> GetReceptionSetupAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "setup"),
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<ReceptionSetupResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("DXNexus returned an empty reception setup.");
    }

    public async Task<CandidateContextResponse> GetCandidatesAsync(
        CandidateContextRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(request, JsonOptions);
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "candidates")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            },
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<CandidateContextResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("DXNexus returned an empty candidate response.");
    }

    public async Task<Guid> GetDeviceIdAsync(CancellationToken cancellationToken = default) =>
        (await CurrentCredentialAsync(cancellationToken).ConfigureAwait(false)).DeviceId;

    public async Task<WebSocketAuthentication> PrepareLiveWebSocketAsync(
        CancellationToken cancellationToken = default)
    {
        var credential = await CurrentCredentialAsync(cancellationToken).ConfigureAwait(false);
        if (credential.AccessTokenExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
        {
            credential = await RefreshAsync(credential, cancellationToken).ConfigureAwait(false);
        }
        var httpEndpoint = new Uri(_httpClient.BaseAddress ?? PairingApiClient.ProductionBaseUri, "live/bridge");
        var socketBuilder = new UriBuilder(httpEndpoint)
        {
            Scheme = httpEndpoint.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Port = httpEndpoint.IsDefaultPort ? -1 : httpEndpoint.Port,
        };
        return new WebSocketAuthentication(
            socketBuilder.Uri,
            $"DPoP {credential.AccessToken}",
            PairingApiClient.CreateAccessProof(
                credential.PrivateKeyPkcs8,
                httpEndpoint,
                HttpMethod.Get,
                credential.AccessToken),
            credential.AccessTokenExpiresAt);
    }

    public async Task<WishlistMutationResponse> SetWishlistAsync(
        WishlistMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(request, JsonOptions);
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "wishlist")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            }, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<WishlistMutationResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("DXNexus returned an empty wishlist response.");
    }

    public async Task<MutationResponse> CreateLogbookEntryAsync(
        LogbookMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(request, JsonOptions);
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "logbook")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            }, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<MutationResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("DXNexus returned an empty logbook response.");
    }

    public async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestFactory);
        var credential = await CurrentCredentialAsync(cancellationToken).ConfigureAwait(false);
        if (credential.AccessTokenExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
        {
            credential = await RefreshAsync(credential, cancellationToken).ConfigureAwait(false);
        }
        var response = await SendOnceAsync(requestFactory(), credential, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
        response.Dispose();
        credential = await RefreshAsync(credential, cancellationToken, force: true).ConfigureAwait(false);
        return await SendOnceAsync(requestFactory(), credential, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpRequestMessage request,
        DeviceCredential credential,
        CancellationToken cancellationToken)
    {
        var requestUri = request.RequestUri
            ?? throw new InvalidOperationException("An authenticated DXNexus request requires a URI.");
        var endpoint = requestUri.IsAbsoluteUri
            ? requestUri
            : new Uri(_httpClient.BaseAddress ?? PairingApiClient.ProductionBaseUri, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", credential.AccessToken);
        request.Headers.Add("DPoP", PairingApiClient.CreateAccessProof(
            credential.PrivateKeyPkcs8,
            endpoint,
            request.Method,
            credential.AccessToken));
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<DeviceCredential> CurrentCredentialAsync(CancellationToken cancellationToken)
    {
        if (_credential is not null) return _credential;
        _credential = await _credentialStore.LoadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("This Bridge is not paired with DXNexus.");
        return _credential;
    }

    public async Task ReloadCredentialAsync(CancellationToken cancellationToken = default)
    {
        await _credentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var reloaded = await _credentialStore.LoadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("This Bridge is not paired with DXNexus.");
            var previous = _credential;
            _credential = reloaded;
            if (previous is not null && !ReferenceEquals(previous, reloaded))
                CryptographicOperations.ZeroMemory(previous.PrivateKeyPkcs8);
        }
        finally
        {
            _credentialGate.Release();
        }
    }

    private async Task<DeviceCredential> RefreshAsync(
        DeviceCredential observed,
        CancellationToken cancellationToken,
        bool force = false)
    {
        await _credentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await CurrentCredentialAsync(cancellationToken).ConfigureAwait(false);
            if (!ReferenceEquals(current, observed)
                && current.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30)) return current;
            var endpoint = new Uri(_httpClient.BaseAddress ?? PairingApiClient.ProductionBaseUri, "token/refresh");
            using var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(current.PrivateKeyPkcs8, out _);
            var parameters = key.ExportParameters(false);
            var publicJwk = new PublicDeviceJwk(
                "EC",
                "P-256",
                Base64Url(parameters.Q.X ?? throw new CryptographicException("Missing P-256 X coordinate.")),
                Base64Url(parameters.Q.Y ?? throw new CryptographicException("Missing P-256 Y coordinate.")));
            var proof = PairingApiClient.CreateNonceProof(
                current.PrivateKeyPkcs8,
                publicJwk,
                endpoint,
                current.RefreshToken);
            using var response = await _httpClient.PostAsJsonAsync(
                "token/refresh",
                new { type = "token.refresh.request", refreshToken = current.RefreshToken, proof },
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            var token = await response.Content.ReadFromJsonAsync<PairingTokenResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException("DXNexus returned an empty refreshed credential.");
            var refreshed = new DeviceCredential(
                token.DeviceId,
                token.AccessToken,
                token.RefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(token.ExpiresInSeconds),
                token.Scopes,
                current.PrivateKeyPkcs8.ToArray());
            await _credentialStore.SaveAsync(refreshed, cancellationToken).ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(current.PrivateKeyPkcs8);
            _credential = refreshed;
            return refreshed;
        }
        finally
        {
            _credentialGate.Release();
        }
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        string? code = null;
        string? apiMessage = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("code", out var codeElement)) code = codeElement.GetString();
            if (json.RootElement.TryGetProperty("error", out var errorElement)) apiMessage = errorElement.GetString();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
        }

        var message = string.IsNullOrWhiteSpace(apiMessage)
            ? $"DXNexus returned HTTP {(int)response.StatusCode}."
            : apiMessage;
        throw new DxnexusApiException(message, response.StatusCode, code);
    }

    public void Dispose()
    {
        if (_credential is not null) CryptographicOperations.ZeroMemory(_credential.PrivateKeyPkcs8);
        _credentialGate.Dispose();
    }
}

public sealed record DeviceConnectionStatus(
    DeviceConnectionPrincipal Device,
    bool Connected);

public sealed record DeviceConnectionPrincipal(
    Guid DeviceId,
    Guid UserId,
    string DeviceName,
    string[] Scopes);

public sealed record WebSocketAuthentication(
    Uri Endpoint,
    string Authorization,
    string Dpop,
    DateTimeOffset ExpiresAt);

public sealed class DxnexusApiException(
    string message,
    HttpStatusCode statusCode,
    string? code) : HttpRequestException(message, null, statusCode)
{
    public string? Code { get; } = code;
}
