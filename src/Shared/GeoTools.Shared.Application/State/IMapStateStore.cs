using GeoTools.Shared.Application.Geospatial;

namespace GeoTools.Shared.Application.State;

public interface IMapStateStore
{
    MapViewState Current { get; }

    long Version { get; }

    void Set(MapViewState state);

    event EventHandler? Changed;
}
