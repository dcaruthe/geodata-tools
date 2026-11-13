using GeoTools.Shared.Application.Geospatial;

namespace GeoTools.Shared.Application.State;

public sealed class MapStateStore : IMapStateStore
{
    private MapViewState _state = MapViewState.Empty;
    private long _version;

    public MapViewState Current => Volatile.Read(ref _state);

    public long Version => Interlocked.Read(ref _version);

    public event EventHandler? Changed;

    public void Set(MapViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Interlocked.Exchange(ref _state, state);
        Interlocked.Increment(ref _version);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
