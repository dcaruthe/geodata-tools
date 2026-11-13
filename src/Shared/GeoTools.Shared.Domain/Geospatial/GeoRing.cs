namespace GeoTools.Shared.Domain.Geospatial;

// A closed boundary ring. The last point always equals the first, so the ring has no
// implicit closing edge when a consumer iterates its consecutive point pairs.
public sealed record GeoRing(IReadOnlyList<GeoPoint> Points);
