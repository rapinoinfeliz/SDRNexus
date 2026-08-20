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
