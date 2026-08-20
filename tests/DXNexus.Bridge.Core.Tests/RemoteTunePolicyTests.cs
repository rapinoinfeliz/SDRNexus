using DXNexus.Bridge.Core;
using DXNexus.Contracts;
using Xunit;

namespace DXNexus.Bridge.Core.Tests;

public sealed class RemoteTunePolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-20T23:00:00Z");

    [Fact]
    public void RequiresLocalWindowFreshCommandAndMatchingTunerRevision()
    {
        var snapshot = Snapshot(12, 92_300_000);
        var command = Command(12, 92_300_000, 98_900_000, Now.AddSeconds(15));
        Assert.Null(RemoteTunePolicy.RejectionReason(command, snapshot, Now.AddMinutes(15), Now));
        Assert.Contains("locked", RemoteTunePolicy.RejectionReason(command, snapshot, default, Now));
        Assert.Contains("expired", RemoteTunePolicy.RejectionReason(command with { ExpiresAt = Now }, snapshot, Now.AddMinutes(15), Now));
        Assert.Contains("changed", RemoteTunePolicy.RejectionReason(command with { ExpectedSequence = 11 }, snapshot, Now.AddMinutes(15), Now));
        Assert.Contains("bounds", RemoteTunePolicy.RejectionReason(command with { FrequencyHz = 999 }, snapshot, Now.AddMinutes(15), Now));
    }

    private static RemoteTuneCommand Command(long sequence, long expected, long target, DateTimeOffset expires) =>
        new("live.command.tune", Protocol.Version, Guid.NewGuid(), Guid.NewGuid(), target, expected, sequence, expires);

    private static SequencedRadioSnapshot Snapshot(long sequence, long frequency) => new(
        sequence,
        Now,
        new RadioHostSnapshot(frequency, frequency, RadioDetector.Wfm, 180_000, 768_000, 768_000, true, null,
            new RelativeSignalMetrics(0, 0, 0), new RdsSnapshot(null, null, null)));
}
