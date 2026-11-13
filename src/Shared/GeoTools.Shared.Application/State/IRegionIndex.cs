using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.State;

// Read-only view of the persistent region index. The launcher is the only writer; readers
// answer from SQLite with AsNoTracking and never mutate rows.
public interface IRegionIndex
{
    Task<RegionDetail?> GetRegionAsync(string id, CancellationToken cancellationToken = default);

    // Every region named by id, with its decoded polylines, in one query. Missing or corrupt
    // rows are dropped rather than reported, so callers always get usable geometry.
    Task<IReadOnlyList<RegionDetail>> GetRegionsAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default
    );

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

    // Every region whose bounds and rings contain the point, ranked so the deepest, smallest,
    // non-territorial region comes first. Returns an empty list when nothing contains the point.
    Task<IReadOnlyList<RegionSummary>> FindContainingAsync(
        GeoPoint point,
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<RegionSummary>> GetChildrenAsync(
        string parentId,
        int childLevel,
        CancellationToken cancellationToken = default
    );

    // Answers the same children as the resolving writer without persisting anything. When the
    // resolution marker already exists the persisted links answer; otherwise the candidate set and
    // containment test run read-only so the API can answer with the launcher closed.
    Task<IReadOnlyList<RegionSummary>> FindChildrenAsync(
        string parentId,
        int childLevel,
        CancellationToken cancellationToken = default
    );

    Task<bool> IsResolvedAsync(
        string parentId,
        int childLevel,
        CancellationToken cancellationToken = default
    );
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
