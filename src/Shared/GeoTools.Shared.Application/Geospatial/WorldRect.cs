namespace GeoTools.Shared.Application.Geospatial;

public readonly record struct WorldRect(double MinX, double MinY, double MaxX, double MaxY)
{
    public static WorldRect World { get; } = new(0, 0, 1, 1);

    public static WorldRect Empty { get; } =
        new(
            double.PositiveInfinity,
            double.PositiveInfinity,
            double.NegativeInfinity,
            double.NegativeInfinity
        );

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;

    public static WorldRect FromPoints(IReadOnlyList<WorldPoint> points)
    {
        if (points.Count == 0)
            return Empty;

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;

        foreach (var point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new WorldRect(minX, minY, maxX, maxY);
    }

    public bool Intersects(WorldRect other) =>
        !IsEmpty
        && !other.IsEmpty
        && MinX <= other.MaxX
        && other.MinX <= MaxX
        && MinY <= other.MaxY
        && other.MinY <= MaxY;

    public WorldRect Inflate(double amount) =>
        IsEmpty ? this : new WorldRect(MinX - amount, MinY - amount, MaxX + amount, MaxY + amount);
}
