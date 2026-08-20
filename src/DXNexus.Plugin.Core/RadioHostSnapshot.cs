namespace DXNexus.Plugin.Core;

public enum RadioDetector
{
    Unknown,
    Wfm,
    Nfm,
    Am,
    Dsb,
    Lsb,
    Usb,
    Cw,
    Raw,
}

public sealed record RelativeSignalMetrics(
    float VisualSnrDb,
    float VisualPeakDb,
    float VisualFloorDb,
    string Source = "sdrsharp.visual",
    bool Calibrated = false);

public sealed record RdsSnapshot(string? Pi, string? Ps, string? Rt);

public sealed record RadioHostSnapshot(
    long FrequencyHz,
    long CenterFrequencyHz,
    RadioDetector Detector,
    int FilterBandwidthHz,
    int RfDisplayBandwidthHz,
    int InputSampleRateHz,
    bool Playing,
    string? SourceName,
    RelativeSignalMetrics Signal,
    RdsSnapshot Rds);

public sealed record SequencedRadioSnapshot(
    long Sequence,
    DateTimeOffset CapturedAtUtc,
    RadioHostSnapshot Radio);

