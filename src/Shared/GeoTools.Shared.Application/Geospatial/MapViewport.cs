using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Application.Geospatial;

public readonly record struct MapViewport(double Scale, double OffsetX, double OffsetY)
{
    public static MapViewport Fit(GeoBounds bounds, int width, int height, float padding)
    {
        var (minX, maxY) = MapProjection.Project(
            new GeoPoint(bounds.MinLongitude, bounds.MinLatitude)
        );
        var (maxX, minY) = MapProjection.Project(
            new GeoPoint(bounds.MaxLongitude, bounds.MaxLatitude)
        );

        var spanX = maxX - minX;
        var spanY = maxY - minY;
        var availableWidth = Math.Max(0, width - 2.0 * padding);
        var availableHeight = Math.Max(0, height - 2.0 * padding);

        var scale = double.PositiveInfinity;
        if (spanX > 0)
            scale = Math.Min(scale, availableWidth / spanX);
        if (spanY > 0)
            scale = Math.Min(scale, availableHeight / spanY);
        if (!double.IsFinite(scale) || scale <= 0)
            scale = 1;

        var offsetX = (width - spanX * scale) / 2 - minX * scale;
        var offsetY = (height - spanY * scale) / 2 - minY * scale;

        return new MapViewport(scale, offsetX, offsetY);
    }

    public Point ToScreen(GeoPoint point)
    {
        var (x, y) = MapProjection.Project(point);
        return ToScreen(x, y);
    }

    public Point ToScreen(double x, double y) =>
        new((float)(x * Scale + OffsetX), (float)(y * Scale + OffsetY));
}
