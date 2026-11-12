using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.State;

public sealed record ArtifactRecord(
    ArtifactKey Key,
    ArtifactKind Kind,
    string? Region,
    string Path,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    string Fingerprint,
    IReadOnlyList<int> AdminLevels,
    GeoBounds? Bounds,
    ArtifactStatus Status,
    DateTimeOffset UpdatedAt
);
