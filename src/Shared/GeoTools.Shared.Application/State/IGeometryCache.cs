namespace GeoTools.Shared.Application.State;

public interface IGeometryCache
{
    Task<GeometryCacheEntry?> TryGetAsync(
        string key,
        CancellationToken cancellationToken = default
    );

    Task PutAsync(GeometryCacheEntry entry, CancellationToken cancellationToken = default);

    Task DeleteExceptAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default
    );

    Task ClearAsync(CancellationToken cancellationToken = default);
}
