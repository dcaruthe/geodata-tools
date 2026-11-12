using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Geospatial;

public interface IBorderSource
{
    BorderGeometry Load();
}
