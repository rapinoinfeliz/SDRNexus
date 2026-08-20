using System.Net.WebSockets;
using System.Text.Json;
using DXNexus.Contracts;

namespace DXNexus.Bridge.Core;

public sealed class LiveCompanionClient(AuthenticatedDeviceApiClient apiClient) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AuthenticatedDeviceApiClient _apiClient = apiClient;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private ClientWebSocket? _socket;
    private DateTimeOffset _reconnectBefore;

    public async Task<bool> PublishAsync(LiveBrowserState state, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        if (payload.Length > Protocol.MaximumCloudMessageBytes)
            throw new InvalidDataException($"Live companion state exceeds {Protocol.MaximumCloudMessageBytes} bytes.");
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _socket!.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }
            catch (Exception error) when (error is WebSocketException or IOException or ObjectDisposedException)
            {
                DisposeSocket();
                return false;
            }
        }
        finally { _sendGate.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_socket?.State == WebSocketState.Open)
            {
                try
                {
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Live companion disabled", cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (WebSocketException) { }
            }
            DisposeSocket();
        }
        finally { _sendGate.Release(); }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_socket?.State == WebSocketState.Open && DateTimeOffset.UtcNow < _reconnectBefore) return;
        DisposeSocket();
        var authentication = await _apiClient.PrepareLiveWebSocketAsync(cancellationToken).ConfigureAwait(false);
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", authentication.Authorization);
        socket.Options.SetRequestHeader("DPoP", authentication.Dpop);
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        try
        {
            await socket.ConnectAsync(authentication.Endpoint, cancellationToken).ConfigureAwait(false);
            _socket = socket;
            _reconnectBefore = DateTimeOffset.UtcNow.AddMinutes(4);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private void DisposeSocket()
    {
        _socket?.Dispose();
        _socket = null;
        _reconnectBefore = default;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _sendGate.Dispose();
    }
}
