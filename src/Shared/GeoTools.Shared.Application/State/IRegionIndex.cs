using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.State;

// Read-only view of the persistent region index. The launcher is the only writer; readers
// answer from SQLite with AsNoTracking and never mutate rows.
public interface IRegionIndex
{
    Task<RegionDetail?> GetRegionAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RegionSummary>> ListRegionsAsync(
        int[]? levels,
        GeoBounds? bounds,
        string? parentId,
        string? search,
        int limit,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<BorderLine>> ListLinesAsync(
        int[] levels,
        GeoBounds? bounds,
        CancellationToken cancellationToken = default
    );

    Task<bool> IsSourceIndexedAsync(
        string sourceKey,
        CancellationToken cancellationToken = default
    );

    Task<RegionIndexStats> GetStatsAsync(CancellationToken cancellationToken = default);
}

public sealed record RegionSummary(
    string Id,
    string? Name,
    string? BorderType,
    int AdminLevel,
    GeoBounds Bounds,
    string? ParentId,
    double Area,
    int PointCount
);

public sealed record RegionDetail(RegionSummary Summary, IReadOnlyList<GeoPolyline> Polylines);

public sealed record BorderLine(
    string Id,
    int AdminLevel,
    GeoBounds Bounds,
    IReadOnlyList<GeoPolyline> Polylines
);

public sealed record RegionIndexStats(
    int RegionCount,
    int LineCount,
    int[] Levels,
    GeoBounds Bounds,
    long DatabaseBytes
);
