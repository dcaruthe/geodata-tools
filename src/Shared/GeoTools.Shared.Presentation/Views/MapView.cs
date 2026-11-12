using System.Diagnostics;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Presentation.Views;

internal sealed class MapView : IView
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

    private readonly IOptionsMonitor<MapOptions> _options;
    private readonly ILogger<MapView> _logger;
    private readonly BorderGeometry _borders;

    private SceneDto? _cachedScene;
    private ViewSize _cachedSize;
    private int[] _cachedAdminLevels = [];
    private MapOptions? _lastGoodOptions;

    public MapView(
        IBorderSource borderSource,
        IOptionsMonitor<MapOptions> options,
        ILogger<MapView> logger
    )
    {
        _options = options;
        _logger = logger;
        _borders = borderSource.Load();
    }

    public ViewId Id => ViewId.Map;

    public SceneDto Compose(ViewSize size)
    {
        var options = CurrentOptions();
        var adminLevels = Normalize(options.AdminLevels);

        if (
            _cachedScene is not null
            && size == _cachedSize
            && adminLevels.SequenceEqual(_cachedAdminLevels)
        )
            return _cachedScene;

        var stopwatch = Stopwatch.StartNew();
        var scene = ComposeScene(size, adminLevels, options.BordersDirectory);
        _cachedScene = scene;
        _cachedSize = size;
        _cachedAdminLevels = adminLevels;

        _logger.LogInformation(
            "Composed map at {Width}x{Height} with admin levels {AdminLevels}: {ShapeCount} shapes in {ElapsedMilliseconds} ms",
            size.Width,
            size.Height,
            string.Join(",", adminLevels),
            scene.Shapes.Count,
            stopwatch.ElapsedMilliseconds
        );

        return scene;
    }

    private SceneDto ComposeScene(ViewSize size, int[] adminLevels, string bordersDirectory)
    {
        var composition = new ViewComposition(size, Color.Black);
        Grid.Compose(composition);
        ComposeBorders(composition, size, adminLevels, bordersDirectory);
        Crosshair.Compose(composition);
        KeyHint.Compose(composition);
        return composition.Build();
    }

    private void ComposeBorders(
        ViewComposition composition,
        ViewSize size,
        int[] adminLevels,
        string bordersDirectory
    )
    {
        if (_borders.Polylines.Count == 0)
        {
            composition.Add(
                new TextDto(
                    new Point(24, 48),
                    $"No border data in {bordersDirectory}. Run scripts/extract_borders.sh.",
                    16,
                    BorderColor
                )
            );
            return;
        }

        if (adminLevels.Length == 0)
        {
            composition.Add(
                new TextDto(
                    new Point(24, 48),
                    "No admin levels selected. Set Map:AdminLevels in appsettings.json.",
                    16,
                    BorderColor
                )
            );
            return;
        }

        var viewport = MapViewport.Fit(_borders.Bounds, size.Width, size.Height, 24f);
        foreach (
            var polyline in MapGeometry.Project(
                _borders.Polylines,
                adminLevels.ToHashSet(),
                viewport
            )
        )
        {
            if (polyline.Closed)
                composition.Add(new PolygonDto(polyline.Points, BorderColor, 1, Filled: false));
            else
                composition.Add(new PolylineDto(polyline.Points, BorderColor, 1));
        }
    }

    private MapOptions CurrentOptions()
    {
        try
        {
            return _lastGoodOptions = _options.CurrentValue;
        }
        catch (Exception exception)
            when (exception is OptionsValidationException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "Ignoring invalid map options.");
            return _lastGoodOptions ?? new MapOptions();
        }
    }

    private static int[] Normalize(int[]? adminLevels) =>
        adminLevels is null
            ? []
            : adminLevels.Where(level => level > 0).Distinct().Order().ToArray();
}
