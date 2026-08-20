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
    private CancellationTokenSource? _receiveStop;
    private Task? _receiveTask;
    private DateTimeOffset _reconnectBefore;

    public event EventHandler<RemoteTuneCommand>? TuneCommandReceived;

    public async Task<bool> PublishAsync(LiveBrowserState state, CancellationToken cancellationToken = default)
        => await SendPayloadAsync(state, cancellationToken).ConfigureAwait(false);

    public async Task<bool> PublishCommandResultAsync(RemoteTuneResult result, CancellationToken cancellationToken = default)
        => await SendPayloadAsync(result, cancellationToken).ConfigureAwait(false);

    private async Task<bool> SendPayloadAsync<T>(T state, CancellationToken cancellationToken)
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
            _receiveStop = new CancellationTokenSource();
            _receiveTask = ReceiveAsync(socket, _receiveStop.Token);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4_096];
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var payload = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Live relay sent a non-text command.");
                    await payload.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken).ConfigureAwait(false);
                    if (payload.Length > Protocol.MaximumCloudMessageBytes) throw new InvalidDataException("Live relay command is too large.");
                } while (!result.EndOfMessage);
                var command = JsonSerializer.Deserialize<RemoteTuneCommand>(payload.ToArray(), JsonOptions);
                if (command?.Type == "live.command.tune" && command.Protocol == Protocol.Version)
                    TuneCommandReceived?.Invoke(this, command);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) when (error is WebSocketException or IOException or InvalidDataException or JsonException) { }
    }

    private void DisposeSocket()
    {
        _receiveStop?.Cancel();
        _socket?.Dispose();
        _socket = null;
        _receiveStop?.Dispose();
        _receiveStop = null;
        _receiveTask = null;
        _reconnectBefore = default;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        TuneCommandReceived = null;
        _sendGate.Dispose();
    }
}
