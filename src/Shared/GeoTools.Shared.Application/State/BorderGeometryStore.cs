using GeoTools.Shared.Application.Geospatial;

namespace GeoTools.Shared.Application.State;

public sealed class BorderGeometryStore : IBorderGeometryStore
{
    private BorderGeometry _geometry = BorderGeometry.Empty;
    private IReadOnlyList<int> _levels = [];
    private long _version;

    public BorderGeometry Geometry => Volatile.Read(ref _geometry);

    public IReadOnlyList<int> AvailableAdminLevels => Volatile.Read(ref _levels);

    public long Version => Interlocked.Read(ref _version);

    public event EventHandler? Changed;

    public void Set(BorderGeometry geometry, IReadOnlyList<int> availableAdminLevels)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(availableAdminLevels);

        Volatile.Write(ref _geometry, geometry);
        Volatile.Write(ref _levels, availableAdminLevels);
        Interlocked.Increment(ref _version);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
