using System.Threading.Channels;
using DXNexus.Contracts;

namespace DXNexus.Plugin.Core;

public sealed class RadioStatePublisher : IDisposable
{
    private readonly RadioStateCollector _collector;
    private readonly IRadioStateSink _sink;
    private readonly Channel<SequencedRadioSnapshot> _pending;
    private readonly CancellationTokenSource _stop = new();
    private Task? _runTask;
    private bool _disposed;

    public RadioStatePublisher(RadioStateCollector collector, IRadioStateSink sink)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _pending = Channel.CreateBounded<SequencedRadioSnapshot>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public BridgeConnectionState State { get; private set; } = BridgeConnectionState.Offline;
    public event EventHandler<BridgeConnectionState>? ConnectionStateChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_runTask is not null)
        {
            return;
        }

        _collector.SnapshotChanged += HandleSnapshotChanged;
        _pending.Writer.TryWrite(_collector.CaptureFullSnapshot());
        _runTask = RunAsync(_stop.Token);
    }

    private void HandleSnapshotChanged(object? sender, SequencedRadioSnapshot snapshot)
    {
        _pending.Writer.TryWrite(snapshot);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var retrySeconds = 1;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SetState(BridgeConnectionState.Connecting);
                await _sink.ConnectAsync(cancellationToken).ConfigureAwait(false);
                SetState(BridgeConnectionState.Connected);
                retrySeconds = 1;
                _pending.Writer.TryWrite(_collector.CaptureFullSnapshot());

                while (await _pending.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (_pending.Reader.TryRead(out var snapshot))
                    {
                        await _sink.SendAsync(snapshot, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                SetState(BridgeConnectionState.Offline);
            }
            catch (InvalidOperationException)
            {
                SetState(BridgeConnectionState.Offline);
            }
            catch (Exception)
            {
                // No transport or serialization failure may escape into SDR#.
                // Diagnostics will be added through the Bridge in a later task.
                SetState(BridgeConnectionState.Offline);
            }

            await Task.Delay(TimeSpan.FromSeconds(retrySeconds), cancellationToken).ConfigureAwait(false);
            retrySeconds = Math.Min(30, retrySeconds * 2);
        }
    }

    private void SetState(BridgeConnectionState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        ConnectionStateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _collector.SnapshotChanged -= HandleSnapshotChanged;
        _pending.Writer.TryComplete();
        _stop.Cancel();
        try
        {
            _runTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        _sink.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _stop.Dispose();
        SetState(BridgeConnectionState.Offline);
    }
}
