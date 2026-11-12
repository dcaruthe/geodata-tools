using GeoTools.Shared.Application.Geospatial;

namespace GeoTools.Shared.Application.State;

public interface IBorderGeometryStore
{
    BorderGeometry Geometry { get; }

    IReadOnlyList<int> AvailableAdminLevels { get; }

    long Version { get; }

    void Set(BorderGeometry geometry, IReadOnlyList<int> availableAdminLevels);

    event EventHandler? Changed;
}
