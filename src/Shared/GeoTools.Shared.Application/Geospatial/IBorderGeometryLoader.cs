namespace GeoTools.Shared.Application.Geospatial;

public interface IBorderGeometryLoader
{
    Task<BorderLoadResult> LoadAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    );
}
