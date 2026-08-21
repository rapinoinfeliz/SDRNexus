using DXNexus.Contracts;

namespace DXNexus.Bridge.Core;

public readonly record struct CandidateQueryKey(
    long FrequencyHz,
    RadioDetector Detector,
    int FilterBandwidthHz)
{
    public static CandidateQueryKey From(SequencedRadioSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new CandidateQueryKey(
            snapshot.Radio.FrequencyHz,
            snapshot.Radio.Detector,
            snapshot.Radio.FilterBandwidthHz);
    }
}
