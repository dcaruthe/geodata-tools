using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Geospatial;

public sealed record MapViewState(
    MapCamera Camera,
    GeoPoint Center,
    WorldRect Viewport,
    string Status
)
{
    public static MapViewState Empty { get; } =
        new(new MapCamera(0, 0, 0), new GeoPoint(0, 0), WorldRect.World, string.Empty);

    public bool IsInitialised => Camera.Scale > 0 && double.IsFinite(Camera.Scale);
}
