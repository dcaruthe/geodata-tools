using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Application.Geospatial;

public readonly record struct MapViewport(double Scale, double OffsetX, double OffsetY)
{
    public static MapViewport Fit(GeoBounds bounds, int width, int height, float padding) =>
        FromCamera(MapCamera.Fit(bounds, width, height, padding), width, height);

    public static MapViewport FromCamera(MapCamera camera, int width, int height) =>
        new(
            camera.Scale,
            width / 2.0 - camera.CenterX * camera.Scale,
            height / 2.0 - camera.CenterY * camera.Scale
        );

    public Point ToScreen(GeoPoint point)
    {
        var (x, y) = MapProjection.Project(point);
        return ToScreen(x, y);
    }

    public Point ToScreen(double x, double y) =>
        new((float)(x * Scale + OffsetX), (float)(y * Scale + OffsetY));

    public (double X, double Y) ToWorld(Point screen) =>
        ((screen.X - OffsetX) / Scale, (screen.Y - OffsetY) / Scale);

    public WorldRect WorldBounds(int width, int height)
    {
        var (minX, minY) = ToWorld(new Point(0, 0));
        var (maxX, maxY) = ToWorld(new Point(width, height));
        return new WorldRect(minX, minY, maxX, maxY);
    }
}
