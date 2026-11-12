using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Geospatial;

public sealed record BorderGeometry(IReadOnlyList<GeoPolyline> Polylines, GeoBounds Bounds)
{
    public static BorderGeometry Empty { get; } = new([], GeoBounds.World);
}
