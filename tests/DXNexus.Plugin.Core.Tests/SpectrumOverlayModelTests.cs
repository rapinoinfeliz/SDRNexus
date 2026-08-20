using DXNexus.Contracts;
using DXNexus.Plugin.Core;
using Xunit;

namespace DXNexus.Plugin.Core.Tests;

public sealed class SpectrumOverlayModelTests
{
    [Fact]
    public void MapsVisibleFrequencyToSpectrumPixel()
    {
        Assert.Equal(500f, SpectrumOverlayModel.PixelForFrequency(100_000_000, 100_000_000, 1_000_000, 1_000));
        Assert.Equal(0f, SpectrumOverlayModel.PixelForFrequency(99_500_000, 100_000_000, 1_000_000, 1_000));
        Assert.Equal(1_000f, SpectrumOverlayModel.PixelForFrequency(100_500_000, 100_000_000, 1_000_000, 1_000));
        Assert.Null(SpectrumOverlayModel.PixelForFrequency(101_000_000, 100_000_000, 1_000_000, 1_000));
    }

    [Fact]
    public void GroupsAChannelAndPrioritizesPersonalState()
    {
        var response = Response(
            Candidate("weak", false, false),
            Candidate("target", false, true),
            Candidate("heard", true, false));

        var marker = Assert.Single(SpectrumOverlayModel.Build(response));

        Assert.Equal("heard +2", marker.Label);
        Assert.Equal(SpectrumMarkerEmphasis.Received, marker.Emphasis);
        Assert.Equal(3, marker.CandidateCount);
    }

    private static CandidateContextResponse Response(params StationCandidate[] candidates) => new(
        "1.0", Guid.NewGuid(), 1, DateTimeOffset.UtcNow, "FM", 98_900_000, "catalog", "model", false, null, candidates);

    private static StationCandidate Candidate(string name, bool received, bool wishlisted) => new(
        Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), name, 98_900_000, "FM", null,
        new CandidateTransmitter(null, null, null, null, new CandidateCoordinate(-27, -51), 1),
        10, 90, "E", null,
        new CandidateEstimate("possible", "medium", 40, 0, null, false, "screen", []),
        new CandidateReceptionHistory(received, received, received ? 1 : 0, null), wishlisted);
}
