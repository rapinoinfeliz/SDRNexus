namespace DXNexus.Plugin.Core;

public sealed class RadioStateCollector : IDisposable
{
    private readonly IRadioHost _host;
    private readonly TimeProvider _timeProvider;
    private RadioHostSnapshot? _lastRadio;
    private long _sequence;
    private bool _disposed;

    public RadioStateCollector(IRadioHost host, TimeProvider? timeProvider = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _host.StateChanged += HandleHostStateChanged;
    }

    public event EventHandler<SequencedRadioSnapshot>? SnapshotChanged;

    public SequencedRadioSnapshot CaptureFullSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Capture(emitWhenUnchanged: true);
    }

    private void HandleHostStateChanged(object? sender, EventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        Capture(emitWhenUnchanged: false);
    }

    private SequencedRadioSnapshot Capture(bool emitWhenUnchanged)
    {
        var radio = _host.CaptureSnapshot();
        if (!emitWhenUnchanged && radio == _lastRadio)
        {
            return new SequencedRadioSnapshot(_sequence, _timeProvider.GetUtcNow(), radio);
        }

        _lastRadio = radio;
        var snapshot = new SequencedRadioSnapshot(
            Interlocked.Increment(ref _sequence),
            _timeProvider.GetUtcNow(),
            radio);
        SnapshotChanged?.Invoke(this, snapshot);
        return snapshot;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.StateChanged -= HandleHostStateChanged;
        _host.Dispose();
    }
}

