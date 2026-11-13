using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.State;

public sealed class FocusOverlayStore : IFocusOverlayStore
{
    private IReadOnlyList<WorldPolyline> _polylines = [];
    private string? _label;
    private GeoBounds? _bounds;
    private long _version;

    public IReadOnlyList<WorldPolyline> Polylines => Volatile.Read(ref _polylines);

    public string? Label => Volatile.Read(ref _label);

    public GeoBounds? Bounds => _bounds;

    public long Version => Interlocked.Read(ref _version);

    public event EventHandler? Changed;

    public void Set(IReadOnlyList<WorldPolyline> polylines, string? label, GeoBounds? bounds)
    {
        ArgumentNullException.ThrowIfNull(polylines);

        Volatile.Write(ref _polylines, polylines);
        Volatile.Write(ref _label, label);
        _bounds = bounds;
        Interlocked.Increment(ref _version);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear() => Set([], null, null);
}
