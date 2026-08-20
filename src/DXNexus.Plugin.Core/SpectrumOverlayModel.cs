using DXNexus.Contracts;

namespace DXNexus.Plugin.Core;

public enum SpectrumMarkerEmphasis
{
    Standard,
    Received,
    Wishlisted,
}

public sealed record SpectrumOverlayMarker(
    long FrequencyHz,
    string Label,
    string Detail,
    SpectrumMarkerEmphasis Emphasis,
    int CandidateCount);

public static class SpectrumOverlayModel
{
    public const int MaximumMarkers = 12;

    public static SpectrumOverlayMarker[] Build(CandidateContextResponse? response)
    {
        if (response is null) return [];
        return response.Candidates
            .GroupBy(candidate => candidate.FrequencyHz)
            .OrderBy(group => Math.Abs(group.Key - response.FrequencyHz))
            .ThenBy(group => group.Key)
            .Take(MaximumMarkers)
            .Select(group =>
            {
                var candidates = group.ToArray();
                var primary = candidates.FirstOrDefault(candidate => candidate.Received.AtListeningPoint)
                    ?? candidates.FirstOrDefault(candidate => candidate.Wishlisted)
                    ?? candidates[0];
                var emphasis = candidates.Any(candidate => candidate.Received.AtListeningPoint)
                    ? SpectrumMarkerEmphasis.Received
                    : candidates.Any(candidate => candidate.Wishlisted)
                        ? SpectrumMarkerEmphasis.Wishlisted
                        : SpectrumMarkerEmphasis.Standard;
                var suffix = candidates.Length > 1 ? $" +{candidates.Length - 1}" : string.Empty;
                return new SpectrumOverlayMarker(
                    group.Key,
                    primary.StationName + suffix,
                    FormatFrequency(group.Key, primary.Band),
                    emphasis,
                    candidates.Length);
            })
            .ToArray();
    }

    public static float? PixelForFrequency(
        long frequencyHz,
        long centerFrequencyHz,
        int displayBandwidthHz,
        float width)
    {
        if (displayBandwidthHz <= 0 || !float.IsFinite(width) || width <= 0) return null;
        var normalized = (frequencyHz - centerFrequencyHz) / (double)displayBandwidthHz + 0.5;
        if (normalized is < 0 or > 1) return null;
        return (float)(normalized * width);
    }

    private static string FormatFrequency(long frequencyHz, string band) =>
        band == "FM" || frequencyHz >= 30_000_000
            ? $"{frequencyHz / 1_000_000d:0.000} MHz"
            : $"{frequencyHz / 1_000d:0.0} kHz";
}
