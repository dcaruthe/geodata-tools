using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.State;

// The focused region's outline, drawn over the borders as a highlight. Like the border geometry
// store, readers take immutable snapshots and the writer swaps them off the render thread.
public interface IFocusOverlayStore
{
    IReadOnlyList<WorldPolyline> Polylines { get; }

    string? Label { get; }

    GeoBounds? Bounds { get; }

    long Version { get; }

    void Set(IReadOnlyList<WorldPolyline> polylines, string? label, GeoBounds? bounds);

    void Clear();

    event EventHandler? Changed;
}
