namespace GeoTools.Shared.Domain.Geospatial;

public readonly record struct GeoBounds(
    double MinLongitude,
    double MinLatitude,
    double MaxLongitude,
    double MaxLatitude
)
{
    public static GeoBounds World { get; } = new(-180, -85.05112878, 180, 85.05112878);

    public static GeoBounds FromPolylines(IEnumerable<GeoPolyline> polylines, GeoBounds fallback)
    {
        var minLongitude = double.PositiveInfinity;
        var minLatitude = double.PositiveInfinity;
        var maxLongitude = double.NegativeInfinity;
        var maxLatitude = double.NegativeInfinity;
        var any = false;

        foreach (var polyline in polylines)
        {
            foreach (var point in polyline.Points)
            {
                any = true;
                minLongitude = Math.Min(minLongitude, point.Longitude);
                minLatitude = Math.Min(minLatitude, point.Latitude);
                maxLongitude = Math.Max(maxLongitude, point.Longitude);
                maxLatitude = Math.Max(maxLatitude, point.Latitude);
            }
        }

        return any ? new GeoBounds(minLongitude, minLatitude, maxLongitude, maxLatitude) : fallback;
    }
}
