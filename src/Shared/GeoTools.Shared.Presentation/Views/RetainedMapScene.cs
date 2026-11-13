using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Presentation.Views;

/// <summary>
/// A screen-space projection of the level-filtered world geometry that survives camera moves.
/// Building projects and simplifies once; a pan or zoom transforms the retained point arrays
/// in place, and every compose still culls the shapes to the visible viewport.
/// </summary>
internal sealed class RetainedMapScene
{
    private static readonly Color BorderColor = new(200, 205, 215);
    private static readonly Color RevealedColor = new(140, 190, 235);

    private readonly List<RetainedPolyline> _polylines = [];
    private HashSet<int> _levels = [];
    private HashSet<int> _rootLevels = [];
    private long _geometryVersion;
    private long _settingsVersion;
    private bool _grid;
    private bool _crosshair;
    private ViewSize _size;
    private double _scale;
    private double _centerX;
    private double _centerY;
    private WorldRect _coverage;
    private bool _built;

    public int RetainedCount => _polylines.Count;

    public int VisibleCount { get; private set; }

    internal double Scale => _scale;

    internal int BuildCount { get; private set; }

    internal int TransformCount { get; private set; }

    /// <summary>
    /// The world rect the retained arrays were projected for: the visible rect with one full
    /// viewport of margin on every side, clamped to the world only when the view stays inside.
    /// </summary>
    internal static WorldRect Coverage(WorldRect visible, MapCamera camera, ViewSize size)
    {
        if (camera.Scale <= 0 || !double.IsFinite(camera.Scale))
            return WorldRect.World;

        var marginX = size.Width / camera.Scale;
        var marginY = size.Height / camera.Scale;
        var desired = new WorldRect(
            visible.MinX - marginX,
            visible.MinY - marginY,
            visible.MaxX + marginX,
            visible.MaxY + marginY
        );
        var clamped = new WorldRect(
            Math.Max(WorldRect.World.MinX, desired.MinX),
            Math.Max(WorldRect.World.MinY, desired.MinY),
            Math.Min(WorldRect.World.MaxX, desired.MaxX),
            Math.Min(WorldRect.World.MaxY, desired.MaxY)
        );

        return Contains(clamped, visible) ? clamped : desired;
    }

    /// <summary>
    /// True when the retained arrays still describe the same source and the requested view is
    /// inside their coverage. Scale changes are allowed because the points transform about the
    /// viewport center.
    /// </summary>
    public bool CanTransformTo(
        long geometryVersion,
        long settingsVersion,
        IReadOnlyList<int> levels,
        bool grid,
        bool crosshair,
        ViewSize size,
        MapCamera camera,
        WorldRect visible
    )
    {
        if (!_built)
            return false;

        if (camera.Scale <= 0 || !double.IsFinite(camera.Scale))
            return false;

        return geometryVersion == _geometryVersion
            && settingsVersion == _settingsVersion
            && levels.Count == _levels.Count
            && _levels.SetEquals(levels)
            && grid == _grid
            && crosshair == _crosshair
            && size == _size
            && Contains(_coverage, visible);
    }

    public void Build(
        BorderGeometry geometry,
        IReadOnlyList<int> levels,
        MapCamera camera,
        ViewSize size,
        WorldRect coverage,
        bool grid = true,
        bool crosshair = true,
        long geometryVersion = 0,
        long settingsVersion = 0,
        IReadOnlyList<int>? rootLevels = null
    )
    {
        var viewport = camera.Viewport(size.Width, size.Height);
        var levelSet = levels.ToHashSet();
        var rootLevelSet = (rootLevels ?? levels).ToHashSet();

        _polylines.Clear();
        foreach (
            var polyline in MapGeometry.Project(
                geometry.WorldPolylines,
                levelSet,
                viewport,
                coverage
            )
        )
        {
            _polylines.Add(
                new RetainedPolyline(
                    polyline.Points,
                    polyline.Closed,
                    polyline.AdminLevel,
                    ScreenRect.FromPoints(polyline.Points)
                )
            );
        }

        _levels = levelSet;
        _rootLevels = rootLevelSet;
        _geometryVersion = geometryVersion;
        _settingsVersion = settingsVersion;
        _grid = grid;
        _crosshair = crosshair;
        _size = size;
        _scale = camera.Scale;
        _centerX = camera.CenterX;
        _centerY = camera.CenterY;
        _coverage = coverage;
        VisibleCount = 0;
        _built = true;
        BuildCount++;
    }

    public void TransformTo(MapCamera camera, ViewSize size)
    {
        if (!_built || size != _size)
            return;

        var factor = camera.Scale / _scale;
        var dx = (camera.CenterX - _centerX) * camera.Scale;
        var dy = (camera.CenterY - _centerY) * camera.Scale;

        if (factor == 1 && dx == 0 && dy == 0)
            return;

        var screenCenterX = size.Width / 2.0;
        var screenCenterY = size.Height / 2.0;

        foreach (var polyline in _polylines)
            polyline.Transform(screenCenterX, screenCenterY, factor, dx, dy);

        _scale = camera.Scale;
        _centerX = camera.CenterX;
        _centerY = camera.CenterY;
        TransformCount++;
    }

    /// <summary>
    /// Returns the retained shapes whose transformed screen bounds intersect the screen rect of
    /// the visible world rect. Nothing outside the viewport reaches the scene.
    /// </summary>
    public IReadOnlyList<ShapeDto> Shapes(WorldRect visible, MapViewport viewport)
    {
        var (minX, minY) = viewport.ToScreen(visible.MinX, visible.MinY);
        var (maxX, maxY) = viewport.ToScreen(visible.MaxX, visible.MaxY);
        var screenRect = new ScreenRect(
            Math.Min(minX, maxX),
            Math.Min(minY, maxY),
            Math.Max(minX, maxX),
            Math.Max(minY, maxY)
        );

        var shapes = new List<ShapeDto>(_polylines.Count);
        foreach (var polyline in _polylines)
        {
            if (!polyline.Bounds.Intersects(screenRect))
                continue;

            shapes.Add(
                polyline.Closed
                    ? new PolygonDto(
                        polyline.Points,
                        ColorFor(polyline.AdminLevel),
                        1,
                        Filled: false
                    )
                    : new PolylineDto(polyline.Points, ColorFor(polyline.AdminLevel), 1)
            );
        }

        VisibleCount = shapes.Count;
        return shapes;
    }

    private Color ColorFor(int adminLevel) =>
        _rootLevels.Contains(adminLevel) ? BorderColor : RevealedColor;

    private static bool Contains(WorldRect outer, WorldRect inner) =>
        inner.MinX >= outer.MinX
        && inner.MaxX <= outer.MaxX
        && inner.MinY >= outer.MinY
        && inner.MaxY <= outer.MaxY;

    private sealed class RetainedPolyline(
        Point[] points,
        bool closed,
        int adminLevel,
        ScreenRect bounds
    )
    {
        public Point[] Points { get; } = points;
        public bool Closed { get; } = closed;
        public int AdminLevel { get; } = adminLevel;
        public ScreenRect Bounds { get; private set; } = bounds;

        public void Transform(
            double screenCenterX,
            double screenCenterY,
            double factor,
            double dx,
            double dy
        )
        {
            for (var i = 0; i < Points.Length; i++)
            {
                var point = Points[i];
                Points[i] = new Point(
                    (float)((point.X - screenCenterX) * factor + screenCenterX + dx),
                    (float)((point.Y - screenCenterY) * factor + screenCenterY + dy)
                );
            }

            Bounds = Bounds.Transform(screenCenterX, screenCenterY, factor, dx, dy);
        }
    }

    private readonly record struct ScreenRect(float MinX, float MinY, float MaxX, float MaxY)
    {
        public static ScreenRect FromPoints(IReadOnlyList<Point> points)
        {
            var minX = float.PositiveInfinity;
            var minY = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            var maxY = float.NegativeInfinity;

            foreach (var point in points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }

            return new ScreenRect(minX, minY, maxX, maxY);
        }

        public ScreenRect Transform(
            double screenCenterX,
            double screenCenterY,
            double factor,
            double dx,
            double dy
        ) =>
            new(
                TransformX(MinX, screenCenterX, factor, dx),
                TransformY(MinY, screenCenterY, factor, dy),
                TransformX(MaxX, screenCenterX, factor, dx),
                TransformY(MaxY, screenCenterY, factor, dy)
            );

        public bool Intersects(ScreenRect other) =>
            MinX <= other.MaxX && other.MinX <= MaxX && MinY <= other.MaxY && other.MinY <= MaxY;

        private static float TransformX(
            float value,
            double screenCenterX,
            double factor,
            double dx
        ) => (float)((value - screenCenterX) * factor + screenCenterX + dx);

        private static float TransformY(
            float value,
            double screenCenterY,
            double factor,
            double dy
        ) => (float)((value - screenCenterY) * factor + screenCenterY + dy);
    }
}
