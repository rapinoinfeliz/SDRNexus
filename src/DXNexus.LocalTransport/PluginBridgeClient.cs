using System.IO.Pipes;
using System.Security.Cryptography;

namespace DXNexus.LocalTransport;

public sealed class PluginBridgeClient : IAsyncDisposable
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private string? _sessionNonce;

    public PluginBridgeClient(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
    }

    public bool IsConnected => _pipe?.IsConnected == true;

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
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            _sessionNonce = null;
            throw;
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
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
            _pipe = null;
        }

        _sessionNonce = null;
    }

    public ValueTask DisposeAsync() => DisposePipeAsync();
}

