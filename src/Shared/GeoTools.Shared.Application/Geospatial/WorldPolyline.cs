namespace GeoTools.Shared.Application.Geospatial;

public sealed record WorldPolyline(
    IReadOnlyList<WorldPoint> Points,
    bool Closed,
    int AdminLevel,
    WorldRect Bounds
);
