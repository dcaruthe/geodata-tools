using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Presentation.Views;

internal sealed class MapController
{
    private readonly IMapStateStore _mapState;
    private readonly IBorderGeometryStore _geometry;
    private readonly MapFocusService _focus;
    private readonly ILogger<MapController> _logger;

    public MapController(
        IMapStateStore mapState,
        IBorderGeometryStore geometry,
        ISettingsStore settings,
        MapFocusService focus,
        ILogger<MapController> logger
    )
    {
        _mapState = mapState;
        _geometry = geometry;
        _focus = focus;
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
        if (key == ViewKey.Backspace)
            return Blur(size);

        switch (key)
        {
            case ViewKey.Up:
            case ViewKey.Down:
            case ViewKey.Left:
            case ViewKey.Right:
            case ViewKey.ZoomIn:
            case ViewKey.ZoomOut:
            case ViewKey.Focus:
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
            ViewKey.Focus => FocusAt(size),
            _ => false,
        };
    }

    public void FocusAt(GeoPoint point, ViewSize size) => _focus.FocusAt(point, size);

    public void FocusRegion(string regionId, ViewSize size) => _focus.FocusRegion(regionId, size);

    public bool Blur(ViewSize size) => _focus.Blur(size);

    private bool FocusAt(ViewSize size)
    {
        _focus.FocusAt(_mapState.Current.Center, size);
        return true;
    }

    internal static GeoBounds BoundsFor(BorderGeometry geometry) =>
        geometry.Polylines.Count == 0 ? GeoBounds.World : geometry.Bounds;

    public bool Pan(double screenX, double screenY, ViewSize size)
    {
        var bounds = BoundsFor(_geometry.Geometry);
        var camera = _mapState
            .Current.Camera.Pan(screenX, screenY, size.Width, size.Height)
            .Clamp(bounds, size.Width, size.Height);
        Publish(camera, size);
        Log(camera);
        return true;
    }

    public bool ZoomByDirection(int direction, ViewSize size) =>
        direction switch
        {
            1 => Zoom(ZoomStep, size),
            -1 => Zoom(1 / ZoomStep, size),
            _ => false,
        };

    public bool Zoom(double factor, ViewSize size)
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
        var state = _mapState.Current;
        var viewport = camera.Viewport(size.Width, size.Height);
        _mapState.Set(
            new MapViewState(
                camera,
                camera.CenterGeo,
                viewport.WorldBounds(size.Width, size.Height),
                state.Status,
                state.Focus,
                state.VisibleLevels
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
