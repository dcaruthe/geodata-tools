namespace GeoTools.Shared.Application.Geospatial;

public sealed record BorderLoadResult(
    BorderGeometry Geometry,
    string Fingerprint,
    IReadOnlyList<int> AvailableLevels,
    int CacheHits,
    int ParsedFiles
);
