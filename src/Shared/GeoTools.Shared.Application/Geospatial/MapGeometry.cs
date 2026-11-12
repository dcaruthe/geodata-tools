using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Application.Geospatial;

public static class MapGeometry
{
    private const double AntimeridianJump = 180;

    public static IReadOnlyList<ScreenPolyline> Project(
        IReadOnlyList<GeoPolyline> polylines,
        IReadOnlySet<int> adminLevels,
        MapViewport viewport,
        float tolerancePixels = 0.25f
    )
    {
        var result = new List<ScreenPolyline>();

        foreach (var polyline in polylines)
        {
            if (!adminLevels.Contains(polyline.AdminLevel))
                continue;

            foreach (var (points, closed) in Split(polyline))
            {
                var screen = new Point[points.Count];
                for (var i = 0; i < points.Count; i++)
                {
                    var (x, y) = MapProjection.Project(points[i]);
                    screen[i] = viewport.ToScreen(x, y);
                }

                var simplified = PolylineSimplifier.Simplify(screen, tolerancePixels);
                if (simplified.Length < 2)
                    continue;

                result.Add(new ScreenPolyline(simplified, closed));
            }
        }

        return result;
    }

    private static IEnumerable<(IReadOnlyList<GeoPoint> Points, bool Closed)> Split(
        GeoPolyline polyline
    )
    {
        var points = polyline.Points;
        if (points.Count == 0)
            yield break;

        if (!polyline.Closed)
        {
            var open = new List<GeoPoint> { points[0] };
            for (var i = 1; i < points.Count; i++)
            {
                if (Math.Abs(points[i].Longitude - points[i - 1].Longitude) > AntimeridianJump)
                {
                    if (open.Count > 1)
                        yield return (open, false);
                    open = [points[i]];
                    continue;
                }

                open.Add(points[i]);
            }

            if (open.Count > 1)
                yield return (open, false);
            yield break;
        }

        var closingJump = Math.Abs(points[0].Longitude - points[^1].Longitude) > AntimeridianJump;
        var firstJump = -1;
        for (var i = 0; i < points.Count - 1; i++)
        {
            if (Math.Abs(points[i + 1].Longitude - points[i].Longitude) <= AntimeridianJump)
                continue;

            firstJump = i;
            break;
        }

        if (firstJump < 0 && !closingJump)
        {
            yield return (points, true);
            yield break;
        }

        var start = (firstJump < 0 ? 0 : firstJump + 1) % points.Count;
        var ring = new List<GeoPoint> { points[start] };
        for (var step = 1; step <= points.Count; step++)
        {
            var index = (start + step) % points.Count;
            var previous = points[(start + step - 1) % points.Count];

            if (Math.Abs(points[index].Longitude - previous.Longitude) > AntimeridianJump)
            {
                if (ring.Count > 1)
                    yield return (ring, false);
                ring = [points[index]];
                continue;
            }

            ring.Add(points[index]);
        }
    }
}
