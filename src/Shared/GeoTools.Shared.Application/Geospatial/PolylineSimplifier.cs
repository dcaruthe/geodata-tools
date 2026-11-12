using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Application.Geospatial;

public static class PolylineSimplifier
{
    public static IReadOnlyList<GeoPoint> Simplify(IReadOnlyList<GeoPoint> points, double tolerance)
    {
        if (points.Count < 3)
            return points;

        if (points[0] != points[^1])
            return SimplifyOpen(points, tolerance);

        var farthest = FindFarthest(points);

        var start = SimplifyOpen(points.Take(farthest + 1).ToArray(), tolerance);
        var end = SimplifyOpen(points.Skip(farthest).ToArray(), tolerance);

        var ring = new List<GeoPoint>(start.Count + end.Count - 1);
        ring.AddRange(start);
        ring.AddRange(end.Skip(1));

        return ring.Distinct().Count() < 3 ? [] : ring;
    }

    public static Point[] Simplify(IReadOnlyList<Point> points, float tolerance)
    {
        if (points.Count < 3)
            return points.ToArray();

        if (points[0] != points[^1])
            return SimplifyOpen(points, tolerance);

        var farthest = FindFarthest(points);

        var start = SimplifyOpen(points.Take(farthest + 1).ToArray(), tolerance);
        var end = SimplifyOpen(points.Skip(farthest).ToArray(), tolerance);

        var ring = new List<Point>(start.Length + end.Length - 1);
        ring.AddRange(start);
        ring.AddRange(end.Skip(1));

        return ring.Distinct().Count() < 3 ? [] : ring.ToArray();
    }

    private static IReadOnlyList<GeoPoint> SimplifyOpen(
        IReadOnlyList<GeoPoint> points,
        double tolerance
    )
    {
        if (points.Count < 3)
            return points;

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;

        var toleranceSquared = tolerance * tolerance;
        var stack = new Stack<(int First, int Last)>();
        stack.Push((0, points.Count - 1));

        while (stack.Count > 0)
        {
            var (first, last) = stack.Pop();
            var farthest = -1;
            var farthestDistance = toleranceSquared;

            for (var i = first + 1; i < last; i++)
            {
                var distance = DistanceSquared(points[i], points[first], points[last]);
                if (distance <= farthestDistance)
                    continue;

                farthestDistance = distance;
                farthest = i;
            }

            if (farthest < 0)
                continue;

            keep[farthest] = true;
            stack.Push((first, farthest));
            stack.Push((farthest, last));
        }

        var simplified = new List<GeoPoint>(points.Count);
        for (var i = 0; i < points.Count; i++)
            if (keep[i])
                simplified.Add(points[i]);

        return simplified;
    }

    private static Point[] SimplifyOpen(IReadOnlyList<Point> points, float tolerance)
    {
        if (points.Count < 3)
            return points.ToArray();

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;

        var toleranceSquared = tolerance * tolerance;
        var stack = new Stack<(int First, int Last)>();
        stack.Push((0, points.Count - 1));

        while (stack.Count > 0)
        {
            var (first, last) = stack.Pop();
            var farthest = -1;
            var farthestDistance = toleranceSquared;

            for (var i = first + 1; i < last; i++)
            {
                var distance = DistanceSquared(points[i], points[first], points[last]);
                if (distance <= farthestDistance)
                    continue;

                farthestDistance = distance;
                farthest = i;
            }

            if (farthest < 0)
                continue;

            keep[farthest] = true;
            stack.Push((first, farthest));
            stack.Push((farthest, last));
        }

        var simplified = new List<Point>(points.Count);
        for (var i = 0; i < points.Count; i++)
            if (keep[i])
                simplified.Add(points[i]);

        return simplified.ToArray();
    }

    private static int FindFarthest(IReadOnlyList<GeoPoint> points)
    {
        var farthest = 0;
        var farthestDistance = -1.0;

        for (var i = 1; i < points.Count; i++)
        {
            var distance = Squared(
                points[i].Longitude - points[0].Longitude,
                points[i].Latitude - points[0].Latitude
            );
            if (distance <= farthestDistance)
                continue;

            farthestDistance = distance;
            farthest = i;
        }

        return farthest;
    }

    private static int FindFarthest(IReadOnlyList<Point> points)
    {
        var farthest = 0;
        var farthestDistance = -1f;

        for (var i = 1; i < points.Count; i++)
        {
            var distance = Squared(points[i].X - points[0].X, points[i].Y - points[0].Y);
            if (distance <= farthestDistance)
                continue;

            farthestDistance = distance;
            farthest = i;
        }

        return farthest;
    }

    private static double DistanceSquared(GeoPoint point, GeoPoint start, GeoPoint end)
    {
        var dx = end.Longitude - start.Longitude;
        var dy = end.Latitude - start.Latitude;
        var lengthSquared = dx * dx + dy * dy;

        if (lengthSquared <= double.Epsilon)
            return Squared(point.Longitude - start.Longitude, point.Latitude - start.Latitude);

        var t =
            ((point.Longitude - start.Longitude) * dx + (point.Latitude - start.Latitude) * dy)
            / lengthSquared;
        t = Math.Clamp(t, 0, 1);

        return Squared(
            point.Longitude - (start.Longitude + t * dx),
            point.Latitude - (start.Latitude + t * dy)
        );
    }

    private static float DistanceSquared(Point point, Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;

        if (lengthSquared <= float.Epsilon)
            return Squared(point.X - start.X, point.Y - start.Y);

        var t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0, 1);

        return Squared(point.X - (start.X + t * dx), point.Y - (start.Y + t * dy));
    }

    private static double Squared(double x, double y) => x * x + y * y;

    private static float Squared(float x, float y) => x * x + y * y;
}
