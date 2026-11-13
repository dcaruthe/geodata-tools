using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Application.Geospatial;

public static class MapGeometry
{
    public static IReadOnlyList<ScreenPolyline> Project(
        IReadOnlyList<WorldPolyline> polylines,
        IReadOnlySet<int> adminLevels,
        MapViewport viewport,
        WorldRect visible,
        float tolerancePixels = 0.25f
    )
    {
        var result = new List<ScreenPolyline>();

        foreach (var polyline in Visible(polylines, adminLevels, visible))
        {
            var screen = new Point[polyline.Points.Count];
            for (var i = 0; i < screen.Length; i++)
                screen[i] = viewport.ToScreen(polyline.Points[i].X, polyline.Points[i].Y);

            var simplified = PolylineSimplifier.Simplify(screen, tolerancePixels);
            if (simplified.Length < 2)
                continue;

            result.Add(new ScreenPolyline(simplified, polyline.Closed, polyline.AdminLevel));
        }

        return result;
    }

    public static int CountVisible(
        IReadOnlyList<WorldPolyline> polylines,
        IReadOnlySet<int> adminLevels,
        WorldRect visible
    ) => Visible(polylines, adminLevels, visible).Count();

    private static IEnumerable<WorldPolyline> Visible(
        IReadOnlyList<WorldPolyline> polylines,
        IReadOnlySet<int> adminLevels,
        WorldRect visible
    )
    {
        foreach (var polyline in polylines)
        {
            if (!adminLevels.Contains(polyline.AdminLevel))
                continue;

            if (!polyline.Bounds.Intersects(visible))
                continue;

            yield return polyline;
        }
    }
}
