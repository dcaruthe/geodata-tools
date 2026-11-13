using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Geospatial;

// Pure geometry helpers for the region hierarchy. Every method takes the decoded polylines of
// a region and answers without touching the database, so the store can decode a candidate blob
// once and run containment and ranking over the in-memory rings.
public static class RegionGeometry
{
    // Even-odd ray casting across every closed polyline. Outer rings and holes both flip the
    // result, so a point inside a hole reads as outside regardless of ring winding. Open
    // polylines are ignored. A ring whose longitude span exceeds 180 degrees crosses the date
    // line and is not a normal containment case, so it never contains a point.
    public static bool Contains(IReadOnlyList<GeoPolyline> rings, GeoPoint point)
    {
        var inside = false;
        foreach (var ring in rings)
        {
            if (!ring.Closed)
                continue;

            if (RingContains(ring.Points, point))
                inside = !inside;
        }

        return inside;
    }

    // Samples up to `count` evenly spaced vertices from the first closed polyline, skipping the
    // duplicate closing point. The caller uses these to test whether a child lies inside a
    // parent. Returns an empty list when there is no closed polyline.
    public static IReadOnlyList<GeoPoint> RepresentativePoints(
        IReadOnlyList<GeoPolyline> rings,
        int count = 9
    )
    {
        if (count <= 0)
            return [];

        foreach (var ring in rings)
        {
            if (!ring.Closed || ring.Points.Count == 0)
                continue;

            var points = ring.Points;
            var total = points.Count;
            if (total > 1 && points[0] == points[total - 1])
                total--; // skip the duplicate closing point

            if (total == 0)
                continue;

            if (total <= count)
            {
                var all = new GeoPoint[total];
                for (var i = 0; i < total; i++)
                    all[i] = points[i];
                return all;
            }

            if (count == 1)
                return [points[0]];

            var sampled = new GeoPoint[count];
            for (var i = 0; i < count; i++)
            {
                var index = (int)Math.Floor((double)i * (total - 1) / (count - 1));
                sampled[i] = points[index];
            }

            return sampled;
        }

        return [];
    }

    // The sum of the absolute shoelace area of each ring in normalized Web Mercator coordinates.
    // Used only for ranking, so an exact geodetic area is not required.
    public static double Area(IReadOnlyList<GeoPolyline> rings)
    {
        var total = 0.0;
        foreach (var ring in rings)
        {
            var points = ring.Points;
            if (points.Count < 3)
                continue;

            double sum = 0;
            for (var i = 0; i < points.Count - 1; i++)
            {
                var first = MapProjection.Project(points[i]);
                var second = MapProjection.Project(points[i + 1]);
                sum += first.X * second.Y - second.X * first.Y;
            }

            total += Math.Abs(sum) / 2;
        }

        return total;
    }

    // The single containment ranking rule: non-territorial regions before territorial ones, then
    // the deepest admin level, then the smallest area. Rank returns every match in that order and
    // Best returns the first, so callers that need one region and callers that need all share the
    // same ordering.
    public static IReadOnlyList<RegionSummary> Rank(
        IEnumerable<(RegionSummary Summary, bool Territorial)> matches
    ) =>
        matches
            .OrderBy(match => match.Territorial)
            .ThenByDescending(match => match.Summary.AdminLevel)
            .ThenBy(match => match.Summary.Area)
            .Select(match => match.Summary)
            .ToArray();

    public static RegionSummary? Best(
        IEnumerable<(RegionSummary Summary, bool Territorial)> matches
    ) => Rank(matches).FirstOrDefault();

    private static bool RingContains(IReadOnlyList<GeoPoint> points, GeoPoint point)
    {
        var count = points.Count;
        if (count < 3)
            return false;

        if (points[0] == points[count - 1])
            count--; // drop the duplicate closing point

        if (count < 3)
            return false;

        var minLongitude = double.PositiveInfinity;
        var maxLongitude = double.NegativeInfinity;
        for (var i = 0; i < count; i++)
        {
            var longitude = points[i].Longitude;
            if (longitude < minLongitude)
                minLongitude = longitude;
            if (longitude > maxLongitude)
                maxLongitude = longitude;
        }

        if (maxLongitude - minLongitude > 180)
            return false; // date-line region, not a normal containment case

        var inside = false;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            var current = points[i];
            var previous = points[j];
            var crosses =
                (current.Latitude > point.Latitude) != (previous.Latitude > point.Latitude)
                && point.Longitude
                    < (previous.Longitude - current.Longitude)
                        * (point.Latitude - current.Latitude)
                        / (previous.Latitude - current.Latitude)
                        + current.Longitude;
            if (crosses)
                inside = !inside;
        }

        return inside;
    }
}
