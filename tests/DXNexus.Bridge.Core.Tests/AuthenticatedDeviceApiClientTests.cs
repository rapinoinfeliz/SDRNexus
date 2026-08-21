using System.Net;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace DXNexus.Bridge.Core.Tests;

public sealed class AuthenticatedDeviceApiClientTests
{
    [Fact]
    public async Task ReloadCredentialUsesNewlyPairedCredentialWithoutBridgeRestart()
    {
        var store = new MemoryCredentialStore();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = key.ExportPkcs8PrivateKey();
        using var handler = new DeviceStatusHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = PairingApiClient.ProductionBaseUri,
        };
        using var client = new AuthenticatedDeviceApiClient(httpClient, store);
        try
        {
            await store.SaveAsync(Credential("old-access-token", privateKey));
            await client.GetStatusAsync();

            await store.SaveAsync(Credential("new-access-token", privateKey));
            await client.ReloadCredentialAsync();
            await client.GetStatusAsync();

            Assert.Equal(["old-access-token", "new-access-token"], handler.AccessTokens);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    private static DeviceCredential Credential(string accessToken, byte[] privateKey) => new(
        Guid.Parse("28cf4501-2b6a-4118-80fc-a6059776275a"),
        accessToken,
        $"refresh-{accessToken}",
        DateTimeOffset.UtcNow.AddMinutes(5),
        ["sdr:context:read"],
        privateKey.ToArray());

    private sealed class DeviceStatusHandler : HttpMessageHandler
    {
        public List<string> AccessTokens { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AccessTokens.Add(request.Headers.Authorization?.Parameter ?? string.Empty);
            const string json = """
                {
                  "device": {
                    "deviceId": "28cf4501-2b6a-4118-80fc-a6059776275a",
                    "userId": "38cf4501-2b6a-4118-80fc-a6059776275b",
                    "deviceName": "Test SDR",
                    "scopes": ["sdr:context:read"]
                  },
                  "connected": true
                }
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class MemoryCredentialStore : IDeviceCredentialStore
    {
        private DeviceCredential? _credential;

        public bool Exists => _credential is not null;

        public Task SaveAsync(DeviceCredential credential, CancellationToken cancellationToken = default)
        {
            _credential = Clone(credential);
            return Task.CompletedTask;
        }

        public Task<DeviceCredential?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_credential is null ? null : Clone(_credential));

        public void Delete() => _credential = null;

        private static DeviceCredential Clone(DeviceCredential credential) => credential with
        {
            Scopes = credential.Scopes.ToArray(),
            PrivateKeyPkcs8 = credential.PrivateKeyPkcs8.ToArray(),
        };
    }
}
