using DXNexus.Plugin.Core;
using Xunit;

namespace DXNexus.Plugin.Core.Tests;

public sealed class RadioStateCollectorTests
{
    [Fact]
    public void CaptureFullSnapshotReturnsSequencedHostState()
    {
        var host = new FakeRadioHost(Snapshot(frequencyHz: 92_300_000));
        using var collector = new RadioStateCollector(host);

        var captured = collector.CaptureFullSnapshot();

        Assert.Equal(1, captured.Sequence);
        Assert.Equal(92_300_000, captured.Radio.FrequencyHz);
        Assert.Equal(RadioDetector.Wfm, captured.Radio.Detector);
        Assert.False(captured.Radio.Signal.Calibrated);
        Assert.Equal("sdrsharp.visual", captured.Radio.Signal.Source);
    }

    [Fact]
    public void HostChangePublishesOnlyWhenSnapshotActuallyChanged()
    {
        var host = new FakeRadioHost(Snapshot(frequencyHz: 92_300_000));
        using var collector = new RadioStateCollector(host);
        var published = new List<SequencedRadioSnapshot>();
        collector.SnapshotChanged += (_, snapshot) => published.Add(snapshot);
        collector.CaptureFullSnapshot();

        host.NotifyChanged();
        host.Current = Snapshot(frequencyHz: 101_700_000);
        host.NotifyChanged();

        Assert.Equal(2, published.Count);
        Assert.Equal(1, published[0].Sequence);
        Assert.Equal(2, published[1].Sequence);
        Assert.Equal(101_700_000, published[1].Radio.FrequencyHz);
    }

    [Fact]
    public void DisposeUnsubscribesAndDisposesHost()
    {
        var host = new FakeRadioHost(Snapshot(frequencyHz: 92_300_000));
        var collector = new RadioStateCollector(host);
        collector.Dispose();

        host.Current = Snapshot(frequencyHz: 101_700_000);
        host.NotifyChanged();

        Assert.True(host.Disposed);
        Assert.Equal(0, host.SubscriberCount);
        Assert.Throws<ObjectDisposedException>(() => collector.CaptureFullSnapshot());
    }

    private static RadioHostSnapshot Snapshot(long frequencyHz) => new(
        frequencyHz,
        92_100_000,
        RadioDetector.Wfm,
        180_000,
        660_000,
        768_000,
        true,
        "Airspy HF+ Discovery",
        new RelativeSignalMetrics(22.4f, -71.8f, -94.2f),
        new RdsSnapshot("9401", "BAND_FM", "Band FM"));

    private sealed class FakeRadioHost(RadioHostSnapshot current) : IRadioHost
    {
        private EventHandler? _stateChanged;

        public RadioHostSnapshot Current { get; set; } = current;
        public bool Disposed { get; private set; }
        public int SubscriberCount => _stateChanged?.GetInvocationList().Length ?? 0;

        public event EventHandler? StateChanged
        {
            add => _stateChanged += value;
            remove => _stateChanged -= value;
        }

        public RadioHostSnapshot CaptureSnapshot() => Current;
        public void NotifyChanged() => _stateChanged?.Invoke(this, EventArgs.Empty);
        public void Dispose() => Disposed = true;
    }
}
