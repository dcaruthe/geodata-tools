using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Presentation.Views;

internal sealed class MapController
{
    private readonly IMapStateStore _mapState;
    private readonly IBorderGeometryStore _geometry;
    private readonly ILogger<MapController> _logger;

    public MapController(
        IMapStateStore mapState,
        IBorderGeometryStore geometry,
        ISettingsStore settings,
        ILogger<MapController> logger
    )
    {
        _mapState = mapState;
        _geometry = geometry;
        _logger = logger;
    }

    private const double PanPixels = 64;
    private const double ZoomStep = 1.4;
    private const double MaximumZoomFactor = 64;
    private const float FitPadding = 24f;

    public void EnsureInitialised(ViewSize size)
    {
        if (_mapState.Current.IsInitialised)
            return;

        var bounds = BoundsFor(_geometry.Geometry);
        Publish(MapCamera.Fit(bounds, size.Width, size.Height, FitPadding), size);
    }

    public bool Handle(ViewKey key, ViewSize size)
    {
        switch (key)
        {
            case ViewKey.Up:
            case ViewKey.Down:
            case ViewKey.Left:
            case ViewKey.Right:
            case ViewKey.ZoomIn:
            case ViewKey.ZoomOut:
                EnsureInitialised(size);
                break;
            default:
                return false;
        }

        return key switch
        {
            ViewKey.Up => Pan(0, -PanPixels, size),
            ViewKey.Down => Pan(0, PanPixels, size),
            ViewKey.Left => Pan(-PanPixels, 0, size),
            ViewKey.Right => Pan(PanPixels, 0, size),
            ViewKey.ZoomIn => Zoom(ZoomStep, size),
            ViewKey.ZoomOut => Zoom(1 / ZoomStep, size),
            _ => false,
        };
    }

    internal static GeoBounds BoundsFor(BorderGeometry geometry) =>
        geometry.Polylines.Count == 0 ? GeoBounds.World : geometry.Bounds;

    private bool Pan(double screenX, double screenY, ViewSize size)
    {
        var bounds = BoundsFor(_geometry.Geometry);
        var camera = _mapState
            .Current.Camera.Pan(screenX, screenY, size.Width, size.Height)
            .Clamp(bounds, size.Width, size.Height);
        Publish(camera, size);
        Log(camera);
        return true;
    }

    private bool Zoom(double factor, ViewSize size)
    {
        var bounds = BoundsFor(_geometry.Geometry);
        var fitScale = MapCamera.Fit(bounds, size.Width, size.Height, FitPadding).Scale;
        var current = _mapState.Current.Camera;
        var scale = Math.Clamp(current.Scale * factor, fitScale, fitScale * MaximumZoomFactor);
        var camera = new MapCamera(current.CenterX, current.CenterY, scale).Clamp(
            bounds,
            size.Width,
            size.Height
        );
        Publish(camera, size);
        Log(camera);
        return true;
    }

    private void Publish(MapCamera camera, ViewSize size)
    {
        var viewport = camera.Viewport(size.Width, size.Height);
        _mapState.Set(
            new MapViewState(
                camera,
                camera.CenterGeo,
                viewport.WorldBounds(size.Width, size.Height),
                _mapState.Current.Status
            )
        );
    }

    private void Log(MapCamera camera)
    {
        var center = camera.CenterGeo;
        _logger.LogInformation(
            "Camera center {Longitude:0.0000},{Latitude:0.0000} zoom {Scale:0.00}",
            center.Longitude,
            center.Latitude,
            camera.Scale
        );
    }
}
