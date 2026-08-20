using System.IO.Pipes;

namespace DXNexus.LocalTransport;

public sealed class BridgePipeServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private Task? _runTask;

    public BridgePipeServer(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
    }

    public event EventHandler<PipeEnvelope>? MessageReceived;
    public event EventHandler<bool>? ConnectionChanged;

    public void Start()
    {
        _runTask ??= RunAsync(_stop.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await ProcessClientAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // A malformed or abruptly disconnected client affects one
                // session only; the server immediately returns to accept mode.
            }
            finally
            {
                ConnectionChanged?.Invoke(this, false);
            }
        }
    }

    private async Task ProcessClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var hello = await PipeFrameCodec.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
        if (hello is null || hello.Type != "hello" || hello.Protocol != Contracts.Protocol.Version || hello.SessionNonce.Length < 32)
        {
            throw new InvalidDataException("The plugin sent an invalid handshake.");
        }

        await PipeFrameCodec.WriteAsync(
            pipe,
            PipeEnvelope.Create("welcome", 0, hello.SessionNonce, new { protocol = Contracts.Protocol.Version }),
            cancellationToken).ConfigureAwait(false);
        ConnectionChanged?.Invoke(this, true);

        while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            var message = await PipeFrameCodec.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                return;
            }

            if (message.Protocol != Contracts.Protocol.Version || message.SessionNonce != hello.SessionNonce)
            {
                throw new InvalidDataException("The plugin changed protocol or session nonce during a connection.");
            }

            MessageReceived?.Invoke(this, message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_runTask is not null)
        {
            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stop.Dispose();
    }
}

