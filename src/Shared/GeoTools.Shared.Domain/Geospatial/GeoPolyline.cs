namespace GeoTools.Shared.Domain.Geospatial;

public sealed record GeoPolyline(IReadOnlyList<GeoPoint> Points, bool Closed, int AdminLevel);
