using DXNexus.Bridge.Core;
using DXNexus.Contracts;
using Xunit;

namespace DXNexus.Bridge.Core.Tests;

public sealed class CandidateQueryKeyTests
{
    [Fact]
    public void IgnoresSignalAndSequenceChanges()
    {
        var first = Snapshot(1, 101_700_000, 8, -90);
        var second = Snapshot(2, 101_700_000, 16, -72);

        Assert.Equal(CandidateQueryKey.From(first), CandidateQueryKey.From(second));
    }

    [Fact]
    public void ChangesWithCatalogRelevantTunerState()
    {
        var baseline = CandidateQueryKey.From(Snapshot(1, 101_700_000, 8, -90));

        Assert.NotEqual(baseline, CandidateQueryKey.From(Snapshot(2, 101_900_000, 8, -90)));
        Assert.NotEqual(baseline, CandidateQueryKey.From(Snapshot(2, 101_700_000, 8, -90, RadioDetector.Nfm)));
        Assert.NotEqual(baseline, CandidateQueryKey.From(Snapshot(2, 101_700_000, 8, -90, bandwidth: 12_500)));
    }

    private static SequencedRadioSnapshot Snapshot(
        long sequence,
        long frequency,
        float snr,
        float peak,
        RadioDetector detector = RadioDetector.Wfm,
        int bandwidth = 200_000) => new(
            sequence,
            DateTimeOffset.UtcNow,
            new RadioHostSnapshot(
                frequency,
                frequency,
                detector,
                bandwidth,
                725_000,
                912_000,
                true,
                "AIRSPY HF+ Series",
                new RelativeSignalMetrics(snr, peak, -100),
                new RdsSnapshot(null, null, null)));
}
