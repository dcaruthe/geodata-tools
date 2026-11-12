using GeoTools.Shared.Application.State;
using GeoTools.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoTools.Shared.Infrastructure.State;

internal sealed class GeometryCacheStore(IDbContextFactory<GeoToolsDbContext> factory)
    : IGeometryCache
{
    public async Task<GeometryCacheEntry?> TryGetAsync(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var row = await context
            .GeometryCache.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Key == key, cancellationToken);
        if (row is null)
            return null;

        return new GeometryCacheEntry(
            row.Key,
            row.Region,
            StoreMappings.ParseLevels(row.AdminLevels),
            row.Tolerance,
            row.CodecVersion,
            row.PointCount,
            StoreMappings.ParseBounds(row.Bounds)
                ?? GeoTools.Shared.Domain.Geospatial.GeoBounds.World,
            row.Blob,
            StoreMappings.FromUnixMilliseconds(row.CreatedAt)
        );
    }

    public async Task PutAsync(
        GeometryCacheEntry entry,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var row = await context.GeometryCache.FindAsync([entry.Key], cancellationToken);
        if (row is null)
        {
            row = new GeometryCacheRow { Key = entry.Key };
            context.GeometryCache.Add(row);
        }

        row.Region = entry.Region;
        row.AdminLevels = StoreMappings.FormatLevels(entry.AdminLevels);
        row.Tolerance = entry.Tolerance;
        row.CodecVersion = entry.CodecVersion;
        row.PointCount = entry.PointCount;
        row.Bounds = StoreMappings.FormatBounds(entry.Bounds);
        row.Blob = entry.Blob;
        row.CreatedAt = StoreMappings.ToUnixMilliseconds(entry.CreatedAt);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteExceptAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context
            .GeometryCache.Where(row => !keys.Contains(row.Key))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context.GeometryCache.ExecuteDeleteAsync(cancellationToken);
    }
}
