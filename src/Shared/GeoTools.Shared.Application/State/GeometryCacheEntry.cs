using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.State;

public sealed record GeometryCacheEntry(
    string Key,
    string Region,
    IReadOnlyList<int> AdminLevels,
    double Tolerance,
    int CodecVersion,
    int PointCount,
    GeoBounds Bounds,
    byte[] Blob,
    DateTimeOffset CreatedAt
);
