using DXNexus.Contracts;
using DXNexus.Plugin.Core;
using Xunit;

namespace DXNexus.Plugin.Core.Tests;

public sealed class StationStreamSearchTests
{
    [Theory]
    [InlineData("FM", 101_700_000, "Concórdia", "101.7 FM Concórdia")]
    [InlineData("AM", 740_000, "São Paulo", "740 AM São Paulo")]
    [InlineData("SW", 6_055_000, null, "6055 SW")]
    public void MatchesDxnexusGoogleStreamQuery(string band, long frequencyHz, string? city, string expected)
    {
        var uri = StationStreamSearch.BuildGoogleUri(Candidate(band, frequencyHz, city));

        Assert.Equal("https", uri.Scheme);
        Assert.Equal("www.google.com", uri.Host);
        Assert.Equal("/search", uri.AbsolutePath);
        Assert.Equal(expected, Uri.UnescapeDataString(uri.Query[3..]));
    }

    private static StationCandidate Candidate(string band, long frequencyHz, string? city) => new(
        "broadcast-1",
        "site-1",
        "Test Radio",
        frequencyHz,
        band,
        null,
        new CandidateTransmitter(
            "Test Site",
            city,
            "Test Region",
            "Brazil",
            new CandidateCoordinate(-27.2, -52.0),
            10),
        12,
        90,
        "E",
        null,
        new CandidateEstimate("strong", "medium", 60, 6, null, false, "Test", []),
        new CandidateReceptionHistory(false, false, 0, null),
        false);
}
