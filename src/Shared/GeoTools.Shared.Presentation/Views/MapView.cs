using System.Diagnostics;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Components;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Presentation.Views;

internal sealed class MapView(
    IBorderGeometryStore geometryStore,
    IMapStateStore mapStateStore,
    MapController controller,
    ISettingsStore settings,
    IProgressStore progress,
    ILogger<MapView> logger
) : IView, IViewInputTarget
{
    private const float FitPadding = 24f;
    private const float StatusLeft = 16;
    private const float StatusTop = 24;
    private const float StatusLineGap = 18;

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

    private readonly RetainedMapScene _retained = new();

    private SceneDto? _cachedScene;
    private ViewSize _cachedSize;
    private long _cachedGeometryVersion;
    private long _cachedSettingsVersion;
    private long _cachedMapStateVersion;
    private bool _cachedGrid;
    private bool _cachedCrosshair;

    public ViewId Id => ViewId.Map;

    internal int RetainedBuildCount => _retained.BuildCount;

    internal int RetainedTransformCount => _retained.TransformCount;

    public bool Handle(ViewKey key, ViewSize size) => controller.Handle(key, size);

    public SceneDto Compose(ViewSize size)
    {
        var geometryVersion = geometryStore.Version;
        var settingsVersion = settings.Version;
        var mapStateVersion = mapStateStore.Version;
        var grid = settings.IsEnabled(FeatureFlag.MapGrid);
        var crosshair = settings.IsEnabled(FeatureFlag.MapCrosshair);

        if (
            _cachedScene is not null
            && size == _cachedSize
            && geometryVersion == _cachedGeometryVersion
            && settingsVersion == _cachedSettingsVersion
            && mapStateVersion == _cachedMapStateVersion
            && grid == _cachedGrid
            && crosshair == _cachedCrosshair
        )
            return _cachedScene;

        var stopwatch = Stopwatch.StartNew();
        var adminLevels = Normalize(settings.Map.AdminLevels);
        var borders = geometryStore.Geometry;
        var state = mapStateStore.Current;
        var fitCamera = MapCamera.Fit(
            MapController.BoundsFor(borders),
            size.Width,
            size.Height,
            FitPadding
        );
        var camera = state.IsInitialised ? state.Camera : fitCamera;
        var viewport = camera.Viewport(size.Width, size.Height);
        var visible = viewport.WorldBounds(size.Width, size.Height);

        var composition = ComposeScene(
            size,
            borders,
            adminLevels,
            viewport,
            visible,
            camera,
            fitCamera.Scale,
            state.Status,
            grid,
            crosshair,
            geometryVersion,
            settingsVersion
        );
        _cachedScene = composition.Scene;
        _cachedSize = size;
        _cachedGeometryVersion = geometryVersion;
        _cachedSettingsVersion = settingsVersion;
        _cachedMapStateVersion = mapStateVersion;
        _cachedGrid = grid;
        _cachedCrosshair = crosshair;

        var zoom =
            fitCamera.Scale > 0 && double.IsFinite(fitCamera.Scale)
                ? camera.Scale / fitCamera.Scale
                : 1;
        logger.LogInformation(
            "Composed map at {Width}x{Height} with admin levels {AdminLevels} at {Zoom:0.0}x zoom: {ShapeCount} shapes, retained {RetainedCount}, visible {VisibleCount}/{PolylineCount} polylines, {Mode} in {ElapsedMilliseconds} ms",
            size.Width,
            size.Height,
            string.Join(",", adminLevels),
            zoom,
            composition.Scene.Shapes.Count,
            composition.RetainedCount,
            composition.VisibleCount,
            composition.TotalPolylines,
            composition.Translated ? "translated" : "rebuilt",
            stopwatch.ElapsedMilliseconds
        );

        return composition.Scene;
    }

    private ComposeResult ComposeScene(
        ViewSize size,
        BorderGeometry borders,
        int[] adminLevels,
        MapViewport viewport,
        WorldRect visible,
        MapCamera camera,
        double fitScale,
        string status,
        bool grid,
        bool crosshair,
        long geometryVersion,
        long settingsVersion
    )
    {
        var composition = new ViewComposition(size, Color.Black);
        if (grid)
            Grid.Compose(composition);
        var bordersResult = ComposeBorders(
            composition,
            borders,
            adminLevels,
            viewport,
            visible,
            camera,
            size,
            grid,
            crosshair,
            geometryVersion,
            settingsVersion
        );
        if (crosshair)
            Crosshair.Compose(composition);
        ComposeStatus(composition, camera, fitScale, status);
        KeyHint.Compose(composition);
        return new ComposeResult(
            composition.Build(),
            bordersResult.Visible,
            bordersResult.TotalPolylines,
            bordersResult.Retained,
            bordersResult.Translated
        );
    }

    private BordersResult ComposeBorders(
        ViewComposition composition,
        BorderGeometry borders,
        int[] adminLevels,
        MapViewport viewport,
        WorldRect visible,
        MapCamera camera,
        ViewSize size,
        bool grid,
        bool crosshair,
        long geometryVersion,
        long settingsVersion
    )
    {
        var available = geometryStore.AvailableAdminLevels;
        var totalPolylines = borders.WorldPolylines.Count;

        if (borders.Polylines.Count == 0)
        {
            AddMessage(composition, EmptyMessage());
            return new BordersResult(0, totalPolylines, 0, Translated: false);
        }

        if (adminLevels.Length == 0)
        {
            AddMessage(composition, "No admin levels selected. Change them in Maintenance (2).");
            return new BordersResult(0, totalPolylines, 0, Translated: false);
        }

        if (!adminLevels.Any(available.Contains))
        {
            AddMessage(
                composition,
                $"No borders at admin levels {string.Join(",", adminLevels)}. Extracted: {string.Join(",", available)}."
            );
            return new BordersResult(0, totalPolylines, 0, Translated: false);
        }

        // A pan keeps the same scale and transforms the retained arrays. A zoom rebuilds so the
        // coverage margin and the simplification tolerance stay one viewport at the new scale,
        // which also lets a later pan leave a coverage that no longer matches the view.
        var translated =
            _retained.Scale == camera.Scale
            && _retained.CanTransformTo(
                geometryVersion,
                settingsVersion,
                adminLevels,
                grid,
                crosshair,
                size,
                camera,
                visible
            );

        if (translated)
        {
            _retained.TransformTo(camera, size);
        }
        else
        {
            _retained.Build(
                borders,
                adminLevels,
                camera,
                size,
                RetainedMapScene.Coverage(visible, camera, size),
                grid,
                crosshair,
                geometryVersion,
                settingsVersion
            );
        }

        foreach (var shape in _retained.Shapes(visible, viewport))
            composition.Add(shape);

        return new BordersResult(
            _retained.VisibleCount,
            totalPolylines,
            _retained.RetainedCount,
            translated
        );
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

    private static void ComposeStatus(
        ViewComposition composition,
        MapCamera camera,
        double fitScale,
        string status
    )
    {
        var zoom = fitScale > 0 && double.IsFinite(fitScale) ? camera.Scale / fitScale : 1;
        var center = camera.CenterGeo;
        var line =
            $"Center {center.Latitude:0.0000}N {center.Longitude:0.0000}E   Zoom {zoom:0.0}x";

        var y = StatusTop;
        if (!string.IsNullOrWhiteSpace(status))
        {
            composition.Add(new TextDto(new Point(StatusLeft, y), status, 14, HintColor));
            y += StatusLineGap;
        }

        composition.Add(new TextDto(new Point(StatusLeft, y), line, 14, HintColor));
    }

    private static int[] Normalize(IReadOnlyList<int>? adminLevels) =>
        adminLevels is null
            ? []
            : adminLevels.Where(level => level > 0).Distinct().Order().ToArray();

    private sealed record ComposeResult(
        SceneDto Scene,
        int VisibleCount,
        int TotalPolylines,
        int RetainedCount,
        bool Translated
    );

    private sealed record BordersResult(
        int Visible,
        int TotalPolylines,
        int Retained,
        bool Translated
    );
}
