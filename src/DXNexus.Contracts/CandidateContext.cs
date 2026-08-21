namespace DXNexus.Contracts;

public sealed record ReceptionSetupContext(Guid ListeningPointId, Guid ReceiverProfileId);

public sealed record CandidateContextRequest(
    string Protocol,
    Guid RequestId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    long FrequencyHz,
    string? Band,
    string Detector,
    int FilterBandwidthHz,
    ReceptionSetupContext Receiver,
    int Limit = 20);

public sealed record SavedListeningPoint(
    Guid Id,
    string Name,
    double Latitude,
    double Longitude,
    bool IsDefault);

public sealed record SavedReceiverProfile(
    Guid Id,
    string Name,
    string? Brand,
    string? Model,
    string? Antenna,
    bool IsDefault);

public sealed record ReceptionSetupResponse(
    string Protocol,
    SavedListeningPoint[] ListeningPoints,
    SavedReceiverProfile[] Receivers);

public sealed record CandidateTransmitter(
    string? SiteName,
    string? City,
    string? Region,
    string? Country,
    CandidateCoordinate Coordinate,
    double? ErpKw);

public sealed record CandidateCoordinate(double Latitude, double Longitude);
public sealed record CandidateSchedule(string State, string? Label);

public sealed record CandidateEstimate(
    string Tier,
    string Confidence,
    double? FieldStrengthDbuvM,
    double? SignalMarginDb,
    string? DominantMode,
    bool RefinedByTerrain,
    string Summary,
    string[] Limitations);

public sealed record CandidateReceptionHistory(
    bool Globally,
    bool AtListeningPoint,
    int Count,
    DateTimeOffset? LastReceivedAt);

public sealed record StationCandidate(
    string BroadcastId,
    string SiteId,
    string StationName,
    long FrequencyHz,
    string Band,
    string? LogoUrl,
    CandidateTransmitter Transmitter,
    double DistanceKm,
    double BearingDeg,
    string BearingCardinal,
    CandidateSchedule? Schedule,
    CandidateEstimate Estimate,
    CandidateReceptionHistory Received,
    bool Wishlisted);

public sealed record CandidateContextResponse(
    string Protocol,
    Guid RequestId,
    long Sequence,
    DateTimeOffset GeneratedAt,
    string Band,
    long FrequencyHz,
    string CatalogVersion,
    string ModelVersion,
    bool Cached,
    string? NextCursor,
    StationCandidate[] Candidates);

public sealed record StationLogoImage(
    long Sequence,
    string BroadcastId,
    string SiteId,
    byte[] PngBytes);

public sealed record StationMutationContext(
    string BroadcastId,
    string SiteId,
    string Name,
    string Band,
    long FrequencyHz,
    string? LogoUrl,
    CandidateTransmitter Transmitter)
{
    public static StationMutationContext FromCandidate(StationCandidate candidate) => new(
        candidate.BroadcastId, candidate.SiteId, candidate.StationName, candidate.Band,
        candidate.FrequencyHz, candidate.LogoUrl, candidate.Transmitter);
}

public sealed record WishlistMutationRequest(
    string Protocol,
    Guid ClientMutationId,
    string Operation,
    string BroadcastId,
    StationMutationContext? Station);

public sealed record WishlistMutationResponse(Guid ClientMutationId, string BroadcastId, bool Wishlisted);

public sealed record SdrMeasurement(
    string Type, double Value, string Unit, string Source, bool Calibrated);

public sealed record SdrSnapshotContext(
    Guid DeviceId,
    long TunedFrequencyHz,
    long CenterFrequencyHz,
    string Detector,
    int FilterBandwidthHz,
    int SampleRateHz,
    SdrMeasurement[] Measurements,
    bool Calibrated,
    RdsSnapshot Rds,
    string PluginVersion,
    string BridgeVersion,
    int SdrSharpRevision);

public sealed record LogbookMutationRequest(
    string Protocol,
    Guid ClientMutationId,
    DateTimeOffset ReceivedAtUtc,
    string Band,
    long FrequencyHz,
    StationMutationContext Station,
    ReceptionSetupContext ReceptionSetup,
    string SignalQuality,
    string IdentificationStatus,
    string[] IdentificationMethods,
    string? Notes,
    string? Propagation,
    SdrSnapshotContext SdrSnapshot);

public sealed record MutationResponse(Guid ClientMutationId, Guid? EntryId, bool? Created, bool? Wishlisted);

public sealed record WishlistCommand(Guid ClientMutationId, StationCandidate Candidate, bool Wishlisted);
public sealed record QuickLogCommand(
    Guid ClientMutationId,
    StationCandidate Candidate,
    string SignalQuality,
    string IdentificationStatus,
    string[] IdentificationMethods,
    string? Notes,
    string? Propagation,
    SequencedRadioSnapshot Snapshot);

public sealed record CommandResult(Guid ClientMutationId, string Action, bool Success, string Message);

public sealed record LiveBrowserState(
    string Type,
    string Protocol,
    long Sequence,
    DateTimeOffset GeneratedAt,
    SequencedRadioSnapshot Snapshot,
    CandidateContextResponse? Candidates);

public sealed record RemoteTuneCommand(
    string Type,
    string Protocol,
    Guid CommandId,
    Guid DeviceId,
    long FrequencyHz,
    long ExpectedFrequencyHz,
    long ExpectedSequence,
    DateTimeOffset ExpiresAt);

public sealed record RemoteTuneResult(
    string Type,
    string Protocol,
    Guid CommandId,
    string Action,
    bool Success,
    string Message,
    long? FrequencyHz = null);
