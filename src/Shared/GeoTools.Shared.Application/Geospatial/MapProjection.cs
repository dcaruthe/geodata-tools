using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Geospatial;

public static class MapProjection
{
    private const double MaxLatitude = 85.05112878;

    public static (double X, double Y) Project(GeoPoint point)
    {
        var longitude = Math.Clamp(point.Longitude, -180, 180);
        var latitude = Math.Clamp(point.Latitude, -MaxLatitude, MaxLatitude);

        var x = (longitude + 180) / 360;
        var latitudeRadians = latitude * Math.PI / 180;
        var y = (1 - Math.Log(Math.Tan(Math.PI / 4 + latitudeRadians / 2)) / Math.PI) / 2;

        return (x, y);
    }
}
