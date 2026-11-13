using System.Diagnostics;
using GeoTools.Shared.Application.Geospatial;
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

    public async Task<IReadOnlyList<RegionDetail>> GetRegionsAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default
    )
    {
        if (ids.Count == 0)
            return [];

        var lookup = ids.Distinct(StringComparer.Ordinal).ToArray();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await context
            .Regions.AsNoTracking()
            .Where(region => lookup.Contains(region.Id))
            .ToListAsync(cancellationToken);

        var result = new List<RegionDetail>(rows.Count);
        foreach (var row in rows)
        {
            var polylines = TryDecode(row.Blob, row.Id);
            if (polylines is null)
                continue;

            result.Add(new RegionDetail(ToSummary(row), polylines));
        }

        return result;
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

    public async Task<IReadOnlyList<RegionSummary>> FindContainingAsync(
        GeoPoint point,
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var candidates = await context
            .Regions.AsNoTracking()
            .Where(region =>
                levels.Contains(region.AdminLevel)
                && region.MinLongitude <= point.Longitude
                && region.MaxLongitude >= point.Longitude
                && region.MinLatitude <= point.Latitude
                && region.MaxLatitude >= point.Latitude
            )
            .ToListAsync(cancellationToken);

        var matches = new List<(RegionSummary Summary, bool Territorial)>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var rings = TryDecode(candidate.Blob, candidate.Id);
            if (rings is null || !RegionGeometry.Contains(rings, point))
                continue;

            matches.Add((ToSummary(candidate), IsTerritorial(candidate.BorderType)));
        }

        return RegionGeometry.Rank(matches);
    }

    public async Task<IReadOnlyList<RegionSummary>> GetChildrenAsync(
        string parentId,
        int childLevel,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await context
            .Regions.AsNoTracking()
            .Where(region => region.ParentId == parentId && region.AdminLevel == childLevel)
            .OrderBy(region => region.Area)
            .ToListAsync(cancellationToken);

        return rows.Select(ToSummary).ToArray();
    }

    public async Task<bool> IsResolvedAsync(
        string parentId,
        int childLevel,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context
            .RegionResolutions.AsNoTracking()
            .AnyAsync(
                row => row.RegionId == parentId && row.ChildLevel == childLevel,
                cancellationToken
            );
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

        // A changed border file can add or remove parents and children anywhere in the hierarchy,
        // not just inside this region, so drop every resolved parent link and resolution marker.
        // Clearing ~5k rows is cheaper than trusting a link that now dangles, and the next focus
        // query recomputes them lazily.
        await ResetHierarchyAsync(context, cancellationToken);

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
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken
        );

        var removed = await context
            .Regions.Where(row => !sourceKeys.Contains(row.SourceKey))
            .ExecuteDeleteAsync(cancellationToken);
        removed += await context
            .BorderLines.Where(row => !sourceKeys.Contains(row.SourceKey))
            .ExecuteDeleteAsync(cancellationToken);

        // A stale source can be the file that owned a parent or a child, so any surviving
        // parent link or resolution marker may now dangle. Recompute the hierarchy from scratch.
        await ResetHierarchyAsync(context, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Removed {Count} index rows from stale sources.", removed);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context.Regions.ExecuteDeleteAsync(cancellationToken);
        await context.BorderLines.ExecuteDeleteAsync(cancellationToken);
        await context.RegionResolutions.ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RegionSummary>> ResolveChildrenAsync(
        string parentId,
        int childLevel,
        CancellationToken cancellationToken = default
    )
    {
        // The resolution marker is the idempotency guarantee: once this pair has been resolved,
        // answer from the persisted links instead of recomputing containment.
        if (await IsResolvedAsync(parentId, childLevel, cancellationToken))
            return await GetChildrenAsync(parentId, childLevel, cancellationToken);

        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken
        );

        // A missing row or a corrupt blob (which GetRegionAsync would report as null) resolves
        // to an empty, persisted result so the caller never retries them.
        var parent = await context.Regions.FirstOrDefaultAsync(
            region => region.Id == parentId,
            cancellationToken
        );
        var parentRings = parent is null ? null : TryDecode(parent.Blob, parent.Id);
        if (parent is null || parentRings is null)
        {
            context.RegionResolutions.Add(Resolution(parentId, childLevel));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return [];
        }

        var candidates = await context
            .Regions.Where(region =>
                region.AdminLevel == childLevel
                && region.Id != parentId
                && region.MinLongitude <= parent.MaxLongitude
                && region.MaxLongitude >= parent.MinLongitude
                && region.MinLatitude <= parent.MaxLatitude
                && region.MaxLatitude >= parent.MinLatitude
            )
            .ToListAsync(cancellationToken);

        var children = new List<RegionSummary>();
        foreach (var candidate in candidates)
        {
            var rings = TryDecode(candidate.Blob, candidate.Id);
            if (rings is null || !IsContainedChild(rings, parentRings))
                continue;

            if (candidate.ParentId is null)
            {
                candidate.ParentId = parentId;
                children.Add(ToSummary(candidate));
            }
            else if (candidate.ParentId == parentId)
            {
                children.Add(ToSummary(candidate));
            }
            else
            {
                logger.LogDebug(
                    "Region {Id} already has parent {Parent}; leaving it.",
                    candidate.Id,
                    candidate.ParentId
                );
            }
        }

        parent.ParentResolved = true;
        context.RegionResolutions.Add(Resolution(parentId, childLevel));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return children.OrderBy(child => child.Area).ToArray();
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

    // Clears every resolved parent link and resolution marker. A geodata change can add or
    // remove parents and children anywhere, so the next focus query must recompute rather than
    // trust a link that now dangles. Cheap at ~5k rows.
    private static async Task ResetHierarchyAsync(
        GeoToolsDbContext context,
        CancellationToken cancellationToken
    )
    {
        await context.Regions.ExecuteUpdateAsync(
            updates =>
                updates
                    .SetProperty(region => region.ParentId, (string?)null)
                    .SetProperty(region => region.ParentResolved, false),
            cancellationToken
        );
        await context.RegionResolutions.ExecuteDeleteAsync(cancellationToken);
    }

    private static bool IsContainedChild(
        IReadOnlyList<GeoPolyline> candidate,
        IReadOnlyList<GeoPolyline> parent
    )
    {
        var representative = RegionGeometry.RepresentativePoints(candidate);
        if (representative.Count > 0)
        {
            var inside = 0;
            foreach (var point in representative)
            {
                if (RegionGeometry.Contains(parent, point))
                    inside++;
            }

            return inside * 2 >= representative.Count;
        }

        // A single-ring candidate with no sampled vertices still has a centroid to test.
        if (candidate.Count == 1)
            return RegionGeometry.Contains(parent, Centroid(candidate[0]));

        return false;
    }

    private static GeoPoint Centroid(GeoPolyline ring)
    {
        var points = ring.Points;
        var count = points.Count;
        if (count > 1 && points[0] == points[count - 1])
            count--;

        if (count == 0)
            return default;

        var longitude = 0.0;
        var latitude = 0.0;
        for (var i = 0; i < count; i++)
        {
            longitude += points[i].Longitude;
            latitude += points[i].Latitude;
        }

        return new GeoPoint(longitude / count, latitude / count);
    }

    private static bool IsTerritorial(string? borderType) =>
        string.Equals(borderType, "territorial", StringComparison.Ordinal);

    private RegionResolutionRow Resolution(string parentId, int childLevel) =>
        new()
        {
            RegionId = parentId,
            ChildLevel = childLevel,
            ResolvedAt = StoreMappings.ToUnixMilliseconds(clock.GetUtcNow()),
        };

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
