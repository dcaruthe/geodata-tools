using System.Diagnostics;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Components;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Presentation.Views;

internal sealed class MapView(
    IBorderGeometryStore geometryStore,
    ISettingsStore settings,
    IProgressStore progress,
    ILogger<MapView> logger
) : IView
{
    private static readonly Color BorderColor = new(200, 205, 215);
    private static readonly Color HintColor = new(150, 150, 150);
    private static readonly GridComponent Grid = new();
    private static readonly CrosshairComponent Crosshair = new();
    private static readonly LabelComponent KeyHint = new(
        ViewText.KeyHint,
        14,
        HintColor,
        size => new Point(16, size.Height - 16)
    );

    private SceneDto? _cachedScene;
    private ViewSize _cachedSize;
    private long _cachedGeometryVersion;
    private long _cachedSettingsVersion;
    private bool _cachedGrid;
    private bool _cachedCrosshair;

    public ViewId Id => ViewId.Map;

    public SceneDto Compose(ViewSize size)
    {
        var geometryVersion = geometryStore.Version;
        var settingsVersion = settings.Version;
        var grid = settings.IsEnabled(FeatureFlag.MapGrid);
        var crosshair = settings.IsEnabled(FeatureFlag.MapCrosshair);

        if (
            _cachedScene is not null
            && size == _cachedSize
            && geometryVersion == _cachedGeometryVersion
            && settingsVersion == _cachedSettingsVersion
            && grid == _cachedGrid
            && crosshair == _cachedCrosshair
        )
            return _cachedScene;

        var stopwatch = Stopwatch.StartNew();
        var adminLevels = Normalize(settings.Map.AdminLevels);
        var (scene, visiblePolylines, totalPolylines) = ComposeScene(
            size,
            adminLevels,
            grid,
            crosshair
        );
        _cachedScene = scene;
        _cachedSize = size;
        _cachedGeometryVersion = geometryVersion;
        _cachedSettingsVersion = settingsVersion;
        _cachedGrid = grid;
        _cachedCrosshair = crosshair;

        logger.LogInformation(
            "Composed map at {Width}x{Height} with admin levels {AdminLevels}: {ShapeCount} shapes, {VisiblePolylineCount}/{PolylineCount} polylines visible in {ElapsedMilliseconds} ms",
            size.Width,
            size.Height,
            string.Join(",", adminLevels),
            scene.Shapes.Count,
            visiblePolylines,
            totalPolylines,
            stopwatch.ElapsedMilliseconds
        );

        return scene;
    }

    private (SceneDto Scene, int VisiblePolylines, int TotalPolylines) ComposeScene(
        ViewSize size,
        int[] adminLevels,
        bool grid,
        bool crosshair
    )
    {
        var composition = new ViewComposition(size, Color.Black);
        if (grid)
            Grid.Compose(composition);
        var (visiblePolylines, totalPolylines) = ComposeBorders(composition, size, adminLevels);
        if (crosshair)
            Crosshair.Compose(composition);
        KeyHint.Compose(composition);
        return (composition.Build(), visiblePolylines, totalPolylines);
    }

    private (int VisiblePolylines, int TotalPolylines) ComposeBorders(
        ViewComposition composition,
        ViewSize size,
        int[] adminLevels
    )
    {
        var borders = geometryStore.Geometry;
        var available = geometryStore.AvailableAdminLevels;
        var totalPolylines = borders.WorldPolylines.Count;

        if (borders.Polylines.Count == 0)
        {
            AddMessage(composition, EmptyMessage());
            return (0, totalPolylines);
        }

        if (adminLevels.Length == 0)
        {
            AddMessage(composition, "No admin levels selected. Change them in Maintenance (2).");
            return (0, totalPolylines);
        }

        if (!adminLevels.Any(available.Contains))
        {
            AddMessage(
                composition,
                $"No borders at admin levels {string.Join(",", adminLevels)}. Extracted: {string.Join(",", available)}."
            );
            return (0, totalPolylines);
        }

        var camera = MapCamera.Fit(borders.Bounds, size.Width, size.Height, 24f);
        var viewport = camera.Viewport(size.Width, size.Height);
        var visible = viewport.WorldBounds(size.Width, size.Height).Inflate(2.0 / camera.Scale);
        var levels = adminLevels.ToHashSet();
        var visiblePolylines = MapGeometry.CountVisible(borders.WorldPolylines, levels, visible);

        foreach (
            var polyline in MapGeometry.Project(borders.WorldPolylines, levels, viewport, visible)
        )
        {
            if (polyline.Closed)
                composition.Add(new PolygonDto(polyline.Points, BorderColor, 1, Filled: false));
            else
                composition.Add(new PolylineDto(polyline.Points, BorderColor, 1));
        }

        return (visiblePolylines, totalPolylines);
    }

    private string EmptyMessage()
    {
        var snapshot = progress.Current;
        return snapshot.Phase switch
        {
            BootstrapPhase.Running => "Loading border data. Press 2 for maintenance.",
            BootstrapPhase.Failed =>
                $"No border data. {snapshot.Error ?? "The last run failed."} Press 2 for maintenance.",
            _ => "No border data. Press 2 for maintenance and run Rebuild borders.",
        };
    }

    private static void AddMessage(ViewComposition composition, string message) =>
        composition.Add(new TextDto(new Point(24, 48), message, 16, BorderColor));

    private static int[] Normalize(IReadOnlyList<int>? adminLevels) =>
        adminLevels is null
            ? []
            : adminLevels.Where(level => level > 0).Distinct().Order().ToArray();
}
