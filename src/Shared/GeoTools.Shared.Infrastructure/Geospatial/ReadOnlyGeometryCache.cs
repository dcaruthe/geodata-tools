using GeoTools.Shared.Application.State;
using GeoTools.Shared.Infrastructure.State;
using Microsoft.Extensions.DependencyInjection;

namespace GeoTools.Shared.Infrastructure.Geospatial;

// The launcher owns the geometry cache. A read-only host resolves entries and never writes,
// so a second process cannot race the single writer.
public sealed class ReadOnlyGeometryCache(IGeometryCache inner) : IGeometryCache
{
    public Task<GeometryCacheEntry?> TryGetAsync(
        string key,
        CancellationToken cancellationToken = default
    ) => inner.TryGetAsync(key, cancellationToken);

    public Task PutAsync(GeometryCacheEntry entry, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task DeleteExceptAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public static class ReadOnlyGeometryCacheExtensions
{
    public static IServiceCollection AddReadOnlyGeometryCache(this IServiceCollection services) =>
        services.AddSingleton<IGeometryCache>(provider => new ReadOnlyGeometryCache(
            provider.GetRequiredService<GeometryCacheStore>()
        ));
}
