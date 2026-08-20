using System.IO.Pipes;
using System.Security.Cryptography;

namespace DXNexus.LocalTransport;

public sealed class PluginBridgeClient : IAsyncDisposable
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private string? _sessionNonce;
    private CancellationTokenSource? _receiveStop;
    private Task? _receiveTask;

    public PluginBridgeClient(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
    }

    public bool IsConnected => _pipe?.IsConnected == true;
    public event EventHandler<PipeEnvelope>? MessageReceived;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await DisposePipeAsync().ConfigureAwait(false);
        var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            _sessionNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            await PipeFrameCodec.WriteAsync(
                pipe,
                PipeEnvelope.Create("hello", 0, _sessionNonce, new { processId = Environment.ProcessId }),
                cancellationToken).ConfigureAwait(false);
            var welcome = await PipeFrameCodec.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
            if (welcome is null || welcome.Type != "welcome" || welcome.Protocol != Contracts.Protocol.Version || welcome.SessionNonce != _sessionNonce)
            {
                throw new InvalidDataException("The DXNexus Bridge returned an invalid handshake.");
            }

            _pipe = pipe;
            _receiveStop = new CancellationTokenSource();
            _receiveTask = ReceiveAsync(pipe, _sessionNonce, _receiveStop.Token);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            _sessionNonce = null;
            throw;
        }
    }

    private async Task ReceiveAsync(NamedPipeClientStream pipe, string sessionNonce, CancellationToken cancellationToken)
    {
        try
        {
            while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                var message = await PipeFrameCodec.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
                if (message is null) return;
                if (message.Protocol != Contracts.Protocol.Version || message.SessionNonce != sessionNonce)
                {
                    throw new InvalidDataException("The Bridge changed protocol or session nonce.");
                }
                MessageReceived?.Invoke(this, message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
    }

    public async Task SendAsync<T>(string type, long sequence, T payload, CancellationToken cancellationToken)
    {
        var pipe = _pipe;
        var nonce = _sessionNonce;
        if (pipe?.IsConnected != true || nonce is null)
        {
            throw new InvalidOperationException("The DXNexus Bridge is not connected.");
        }

        await PipeFrameCodec.WriteAsync(
            pipe,
            PipeEnvelope.Create(type, sequence, nonce, payload),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask DisposePipeAsync()
    {
        if (_receiveStop is not null)
        {
            await _receiveStop.CancelAsync().ConfigureAwait(false);
        }
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
            _pipe = null;
        }

        if (_receiveTask is not null)
        {
            try { await _receiveTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _sessionNonce = null;
        _receiveTask = null;
        _receiveStop?.Dispose();
        _receiveStop = null;
    }

    public ValueTask DisposeAsync() => DisposePipeAsync();
}
