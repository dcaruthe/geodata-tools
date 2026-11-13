using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Geospatial;

public static class GeometryProjector
{
    private const double AntimeridianJump = 180;

    public static IReadOnlyList<WorldPolyline> Project(IReadOnlyList<GeoPolyline> polylines)
    {
        var result = new List<WorldPolyline>();

        foreach (var polyline in polylines)
        foreach (var (points, closed) in Split(polyline))
            result.Add(Build(points, closed, polyline.AdminLevel));

        return result;
    }

    private static WorldPolyline Build(IReadOnlyList<GeoPoint> points, bool closed, int adminLevel)
    {
        var projected = new WorldPoint[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var (x, y) = MapProjection.Project(points[i]);
            projected[i] = new WorldPoint(x, y);
        }

        return new WorldPolyline(projected, closed, adminLevel, WorldRect.FromPoints(projected));
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
