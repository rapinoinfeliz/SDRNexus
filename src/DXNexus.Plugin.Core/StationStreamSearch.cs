using System.Globalization;
using DXNexus.Contracts;

namespace DXNexus.Plugin.Core;

public static class StationStreamSearch
{
    public static Uri BuildGoogleUri(StationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var frequency = string.Equals(candidate.Band, "FM", StringComparison.OrdinalIgnoreCase)
            ? (candidate.FrequencyHz / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture)
            : (candidate.FrequencyHz / 1_000d).ToString("0.###", CultureInfo.InvariantCulture);
        var terms = new[]
        {
            frequency,
            candidate.Band.Trim(),
            candidate.Transmitter.City?.Trim(),
        };
        var query = string.Join(' ', terms.Where(term => !string.IsNullOrWhiteSpace(term)));

        return new Uri($"https://www.google.com/search?q={Uri.EscapeDataString(query)}");
    }
}
