using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Geospatial;

public readonly record struct MapCamera(double CenterX, double CenterY, double Scale)
{
    public static MapCamera Fit(GeoBounds bounds, int width, int height, float padding = 24f)
    {
        var (minX, minY, maxX, maxY) = ProjectCorners(bounds);
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

        return new MapCamera((minX + maxX) / 2, (minY + maxY) / 2, scale);
    }

    public MapViewport Viewport(int width, int height) =>
        MapViewport.FromCamera(this, width, height);

    public MapCamera Pan(double screenDx, double screenDy, int width, int height) =>
        IsUsable
            ? new MapCamera(CenterX + screenDx / Scale, CenterY + screenDy / Scale, Scale)
            : this;

    public MapCamera Zoom(double factor) =>
        IsUsable && double.IsFinite(factor) && factor > 0
            ? new MapCamera(CenterX, CenterY, Scale * factor)
            : this;

    public MapCamera Clamp(GeoBounds bounds, int width, int height)
    {
        if (!IsUsable)
            return this;

        var (minX, minY, maxX, maxY) = ProjectCorners(bounds);
        return new MapCamera(
            ClampAxis(CenterX, minX, maxX, width, Scale),
            ClampAxis(CenterY, minY, maxY, height, Scale),
            Scale
        );
    }

    public GeoPoint CenterGeo => MapProjection.Unproject(CenterX, CenterY);

    private bool IsUsable => Scale > 0 && double.IsFinite(Scale);

    private static double ClampAxis(double center, double min, double max, int pixels, double scale)
    {
        var half = pixels / (2.0 * scale);
        return max - min >= 2 * half ? Math.Clamp(center, min + half, max - half) : (min + max) / 2;
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) ProjectCorners(
        GeoBounds bounds
    )
    {
        (double X, double Y)[] corners =
        [
            MapProjection.Project(new GeoPoint(bounds.MinLongitude, bounds.MinLatitude)),
            MapProjection.Project(new GeoPoint(bounds.MaxLongitude, bounds.MinLatitude)),
            MapProjection.Project(new GeoPoint(bounds.MaxLongitude, bounds.MaxLatitude)),
            MapProjection.Project(new GeoPoint(bounds.MinLongitude, bounds.MaxLatitude)),
        ];

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;

        foreach (var (x, y) in corners)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        return (minX, minY, maxX, maxY);
    }
}
