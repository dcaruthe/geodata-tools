using System.Diagnostics;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Storage;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Infrastructure.Geospatial;
using GeoTools.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.State;

// The single SQLite-backed implementation of the region index. Reads use AsNoTracking and the
// indexed bounds columns; writes run in one transaction and are idempotent per source key.
internal sealed class SqliteRegionIndex(
    IDbContextFactory<GeoToolsDbContext> factory,
    IOptions<StorageOptions> storage,
    TimeProvider clock,
    ILogger<SqliteRegionIndex> logger
) : IRegionIndex, IRegionIndexWriter
{
    private const int InsertChunkSize = 2000;
    private const int MaxListLimit = 5000;

    public async Task<RegionDetail?> GetRegionAsync(
        string id,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var row = await context
            .Regions.AsNoTracking()
            .FirstOrDefaultAsync(region => region.Id == id, cancellationToken);
        if (row is null)
            return null;

        var polylines = TryDecode(row.Blob, row.Id);
        if (polylines is null)
            return null;

        return new RegionDetail(ToSummary(row), polylines);
    }

    public async Task<IReadOnlyList<RegionSummary>> ListRegionsAsync(
        int[]? levels,
        GeoBounds? bounds,
        string? parentId,
        string? search,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var query = context.Regions.AsNoTracking().AsQueryable();

        if (levels is { Length: > 0 })
            query = query.Where(region => levels.Contains(region.AdminLevel));

        if (bounds is { } box)
            query = query.Where(region =>
                region.MinLongitude <= box.MaxLongitude
                && region.MaxLongitude >= box.MinLongitude
                && region.MinLatitude <= box.MaxLatitude
                && region.MaxLatitude >= box.MinLatitude
            );

        if (!string.IsNullOrWhiteSpace(parentId))
            query = query.Where(region => region.ParentId == parentId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(region =>
                (region.Name != null && EF.Functions.Like(region.Name, pattern))
                || EF.Functions.Like(region.Id, pattern)
            );
        }

        var pageSize = Math.Clamp(limit, 1, MaxListLimit);
        var rows = await query
            .OrderBy(region => region.AdminLevel)
            .ThenByDescending(region => region.Area)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return rows.Select(ToSummary).ToArray();
    }

    public async Task<IReadOnlyList<BorderLine>> ListLinesAsync(
        int[] levels,
        GeoBounds? bounds,
        CancellationToken cancellationToken = default
    )
    {
        if (levels is not { Length: > 0 })
            throw new ArgumentException("At least one admin level is required.", nameof(levels));

        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var query = context
            .BorderLines.AsNoTracking()
            .Where(line => levels.Contains(line.AdminLevel));

        if (bounds is { } box)
            query = query.Where(line =>
                line.MinLongitude <= box.MaxLongitude
                && line.MaxLongitude >= box.MinLongitude
                && line.MinLatitude <= box.MaxLatitude
                && line.MaxLatitude >= box.MinLatitude
            );

        var rows = await query.ToListAsync(cancellationToken);
        var result = new List<BorderLine>(rows.Count);
        foreach (var row in rows)
        {
            var polylines = TryDecode(row.Blob, row.Id);
            if (polylines is null)
                continue;

            result.Add(
                new BorderLine(
                    row.Id,
                    row.AdminLevel,
                    new GeoBounds(
                        row.MinLongitude,
                        row.MinLatitude,
                        row.MaxLongitude,
                        row.MaxLatitude
                    ),
                    polylines
                )
            );
        }

        return result;
    }

    public async Task<bool> IsSourceIndexedAsync(
        string sourceKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context
                .Regions.AsNoTracking()
                .AnyAsync(region => region.SourceKey == sourceKey, cancellationToken)
            || await context
                .BorderLines.AsNoTracking()
                .AnyAsync(line => line.SourceKey == sourceKey, cancellationToken);
    }

    public async Task<RegionIndexStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var regionCount = await context.Regions.AsNoTracking().CountAsync(cancellationToken);
        var lineCount = await context.BorderLines.AsNoTracking().CountAsync(cancellationToken);
        var levels = (
            await context
                .Regions.AsNoTracking()
                .Select(region => region.AdminLevel)
                .Distinct()
                .ToListAsync(cancellationToken)
        )
            .Concat(
                await context
                    .BorderLines.AsNoTracking()
                    .Select(line => line.AdminLevel)
                    .Distinct()
                    .ToListAsync(cancellationToken)
            )
            .Distinct()
            .Order()
            .ToArray();
        var bounds = await ComputeBoundsAsync(context, cancellationToken);

        return new RegionIndexStats(regionCount, lineCount, levels, bounds, DatabaseBytes());
    }

    public async Task WriteSourceAsync(
        string region,
        string sourceKey,
        IReadOnlyList<RegionWrite> regions,
        IReadOnlyList<BorderLineWrite> lines,
        CancellationToken cancellationToken = default
    )
    {
        var stopwatch = Stopwatch.StartNew();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken
        );

        // Replace every row for this region so a changed file (a new source key) drops its
        // stale rows and a re-run with the same source key rewrites them idempotently.
        await context
            .Regions.Where(row => row.Region == region)
            .ExecuteDeleteAsync(cancellationToken);
        await context
            .BorderLines.Where(row => row.Region == region)
            .ExecuteDeleteAsync(cancellationToken);

        var createdAt = StoreMappings.ToUnixMilliseconds(clock.GetUtcNow());
        var regionRows = regions.Select(row => ToRegionRow(row, sourceKey, createdAt)).ToArray();
        var lineRows = lines.Select(row => ToLineRow(row, sourceKey, createdAt)).ToArray();

        regionRows = await SkipExistingRegionsAsync(context, regionRows, region, cancellationToken);

        await InsertChunkedAsync(context, regionRows, cancellationToken);
        await InsertChunkedAsync(context, lineRows, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "Indexed {Region} from {SourceKey}: {RegionCount} regions, {LineCount} lines in {ElapsedMilliseconds} ms",
            region,
            sourceKey,
            regionRows.Length,
            lineRows.Length,
            stopwatch.ElapsedMilliseconds
        );
    }

    public async Task DeleteExceptSourcesAsync(
        IReadOnlyCollection<string> sourceKeys,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var removed = await context
            .Regions.Where(row => !sourceKeys.Contains(row.SourceKey))
            .ExecuteDeleteAsync(cancellationToken);
        removed += await context
            .BorderLines.Where(row => !sourceKeys.Contains(row.SourceKey))
            .ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation("Removed {Count} index rows from stale sources.", removed);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context.Regions.ExecuteDeleteAsync(cancellationToken);
        await context.BorderLines.ExecuteDeleteAsync(cancellationToken);
    }

    private IReadOnlyList<GeoPolyline>? TryDecode(byte[] blob, string id)
    {
        try
        {
            return GeometryBlobCodec
                .Decode(blob)
                .Features.SelectMany(feature => feature.Polylines)
                .ToArray();
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            logger.LogWarning(exception, "Ignoring a corrupt index blob for {Id}.", id);
            return null;
        }
    }

    // A border relation can be extracted into two region files (a shared border), so the same
    // region id may already exist under a different source. Keep the first one and skip the rest.
    private async Task<RegionRow[]> SkipExistingRegionsAsync(
        GeoToolsDbContext context,
        RegionRow[] rows,
        string region,
        CancellationToken cancellationToken
    )
    {
        if (rows.Length == 0)
            return rows;

        var ids = rows.Select(row => row.Id).Distinct().ToList();
        var existing = new HashSet<string>(
            await context
                .Regions.Where(row => ids.Contains(row.Id))
                .Select(row => row.Id)
                .ToListAsync(cancellationToken),
            StringComparer.Ordinal
        );
        if (existing.Count == 0)
            return rows;

        var skipped = rows.Length - rows.Count(row => !existing.Contains(row.Id));
        logger.LogDebug(
            "Skipped {Count} region rows for {Region} whose ids already exist.",
            skipped,
            region
        );
        return rows.Where(row => !existing.Contains(row.Id)).ToArray();
    }

    private static RegionSummary ToSummary(RegionRow row) =>
        new(
            row.Id,
            row.Name,
            row.BorderType,
            row.AdminLevel,
            new GeoBounds(row.MinLongitude, row.MinLatitude, row.MaxLongitude, row.MaxLatitude),
            row.ParentId,
            row.Area,
            row.PointCount
        );

    private static RegionRow ToRegionRow(RegionWrite write, string sourceKey, long createdAt) =>
        new()
        {
            Id = write.Id,
            SourceKey = sourceKey,
            Region = write.Region,
            OsmId = write.OsmId,
            Name = write.Name,
            BorderType = write.BorderType,
            AdminLevel = write.AdminLevel,
            ParentId = null,
            ParentResolved = false,
            MinLongitude = write.Bounds.MinLongitude,
            MinLatitude = write.Bounds.MinLatitude,
            MaxLongitude = write.Bounds.MaxLongitude,
            MaxLatitude = write.Bounds.MaxLatitude,
            Area = write.Area,
            PointCount = write.PointCount,
            Blob = write.Blob,
            CreatedAt = createdAt,
        };

    private static BorderLineRow ToLineRow(
        BorderLineWrite write,
        string sourceKey,
        long createdAt
    ) =>
        new()
        {
            Id = write.Id,
            SourceKey = sourceKey,
            Region = write.Region,
            AdminLevel = write.AdminLevel,
            MinLongitude = write.Bounds.MinLongitude,
            MinLatitude = write.Bounds.MinLatitude,
            MaxLongitude = write.Bounds.MaxLongitude,
            MaxLatitude = write.Bounds.MaxLatitude,
            PointCount = write.PointCount,
            Blob = write.Blob,
            CreatedAt = createdAt,
        };

    private static async Task InsertChunkedAsync<T>(
        GeoToolsDbContext context,
        IReadOnlyList<T> rows,
        CancellationToken cancellationToken
    )
        where T : class
    {
        for (var offset = 0; offset < rows.Count; offset += InsertChunkSize)
        {
            var end = Math.Min(offset + InsertChunkSize, rows.Count);
            var chunk = new List<T>(end - offset);
            for (var index = offset; index < end; index++)
                chunk.Add(rows[index]);

            context.AddRange(chunk);
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
        }
    }

    private static async Task<GeoBounds> ComputeBoundsAsync(
        GeoToolsDbContext context,
        CancellationToken cancellationToken
    )
    {
        var regionBounds = await ComputeBoundsAsync(
            context.Regions.AsNoTracking(),
            cancellationToken
        );
        var lineBounds = await ComputeBoundsAsync(
            context.BorderLines.AsNoTracking(),
            cancellationToken
        );

        if (regionBounds is null && lineBounds is null)
            return GeoBounds.World;
        if (regionBounds is null)
            return lineBounds!.Value;
        if (lineBounds is null)
            return regionBounds.Value;

        return new GeoBounds(
            Math.Min(regionBounds.Value.MinLongitude, lineBounds.Value.MinLongitude),
            Math.Min(regionBounds.Value.MinLatitude, lineBounds.Value.MinLatitude),
            Math.Max(regionBounds.Value.MaxLongitude, lineBounds.Value.MaxLongitude),
            Math.Max(regionBounds.Value.MaxLatitude, lineBounds.Value.MaxLatitude)
        );
    }

    private static async Task<GeoBounds?> ComputeBoundsAsync(
        IQueryable<RegionRow> rows,
        CancellationToken cancellationToken
    )
    {
        if (!await rows.AnyAsync(cancellationToken))
            return null;

        return new GeoBounds(
            await rows.MinAsync(row => row.MinLongitude, cancellationToken),
            await rows.MinAsync(row => row.MinLatitude, cancellationToken),
            await rows.MaxAsync(row => row.MaxLongitude, cancellationToken),
            await rows.MaxAsync(row => row.MaxLatitude, cancellationToken)
        );
    }

    private static async Task<GeoBounds?> ComputeBoundsAsync(
        IQueryable<BorderLineRow> rows,
        CancellationToken cancellationToken
    )
    {
        if (!await rows.AnyAsync(cancellationToken))
            return null;

        return new GeoBounds(
            await rows.MinAsync(row => row.MinLongitude, cancellationToken),
            await rows.MinAsync(row => row.MinLatitude, cancellationToken),
            await rows.MaxAsync(row => row.MaxLongitude, cancellationToken),
            await rows.MaxAsync(row => row.MaxLatitude, cancellationToken)
        );
    }

    private long DatabaseBytes()
    {
        try
        {
            var path = RepositoryPaths.Resolve(storage.Value.DatabasePath);
            return File.Exists(path) ? new FileInfo(path).Length : 0L;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0L;
        }
    }
}
