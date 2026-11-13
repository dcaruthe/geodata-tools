using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Geospatial;

public sealed record BorderGeometry(
    IReadOnlyList<GeoPolyline> Polylines,
    IReadOnlyList<WorldPolyline> WorldPolylines,
    GeoBounds Bounds
)
{
    public static BorderGeometry Empty { get; } = new([], [], GeoBounds.World);

    public static BorderGeometry FromGeo(IReadOnlyList<GeoPolyline> polylines, GeoBounds bounds) =>
        new(polylines, GeometryProjector.Project(polylines), bounds);
}
