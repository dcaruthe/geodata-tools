using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Presentation.Tests;

internal sealed class FakeRegionIndex : IRegionIndex
{
    private readonly Dictionary<string, RegionDetail> _regions = new(StringComparer.Ordinal);

    public IReadOnlyList<RegionSummary> FindContainingResult { get; set; } = [];

    public Func<
        GeoPoint,
        IReadOnlyList<int>,
        CancellationToken,
        Task<IReadOnlyList<RegionSummary>>
    >? FindContainingOverride { get; set; }

    public Func<string, CancellationToken, Task<RegionDetail?>>? GetRegionOverride { get; set; }

    public void Add(RegionSummary summary, IReadOnlyList<GeoPolyline> polylines) =>
        _regions[summary.Id] = new RegionDetail(summary, polylines);

    public Task<RegionDetail?> GetRegionAsync(
        string id,
        CancellationToken cancellationToken = default
    ) =>
        GetRegionOverride?.Invoke(id, cancellationToken)
        ?? Task.FromResult(_regions.TryGetValue(id, out var detail) ? detail : (RegionDetail?)null);

    public Task<IReadOnlyList<RegionDetail>> GetRegionsAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult<IReadOnlyList<RegionDetail>>(
            ids.Where(_regions.ContainsKey).Select(id => _regions[id]).ToArray()
        );

    public Task<IReadOnlyList<RegionSummary>> ListRegionsAsync(
        int[]? levels,
        GeoBounds? bounds,
        string? parentId,
        string? search,
        int limit,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<RegionSummary>>([]);

    public Task<IReadOnlyList<BorderLine>> ListLinesAsync(
        int[] levels,
        GeoBounds? bounds,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<BorderLine>>([]);

    public Task<bool> IsSourceIndexedAsync(
        string sourceKey,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(false);

    public Task<RegionIndexStats> GetStatsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new RegionIndexStats(0, 0, [], GeoBounds.World, 0));

    public Task<IReadOnlyList<RegionSummary>> FindContainingAsync(
        GeoPoint point,
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken = default
    ) =>
        FindContainingOverride?.Invoke(point, levels, cancellationToken)
        ?? Task.FromResult(FindContainingResult);

    public Task<IReadOnlyList<RegionSummary>> GetChildrenAsync(
        string parentId,
        int childLevel,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<RegionSummary>>([]);

    public Task<bool> IsResolvedAsync(
        string parentId,
        int childLevel,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(false);
}

internal sealed class FakeRegionIndexWriter : IRegionIndexWriter
{
    private readonly Dictionary<
        (string Parent, int Level),
        IReadOnlyList<RegionSummary>
    > _children = [];

    public int ResolveChildrenCallCount { get; private set; }

    public void AddChildren(
        string parentId,
        int childLevel,
        IReadOnlyList<RegionSummary> children
    ) => _children[(parentId, childLevel)] = children;

    public Task<IReadOnlyList<RegionSummary>> ResolveChildrenAsync(
        string parentId,
        int childLevel,
        CancellationToken cancellationToken = default
    )
    {
        ResolveChildrenCallCount++;
        return Task.FromResult(
            _children.TryGetValue((parentId, childLevel), out var children)
                ? children
                : (IReadOnlyList<RegionSummary>)[]
        );
    }

    public Task WriteSourceAsync(
        string region,
        string sourceKey,
        IReadOnlyList<RegionWrite> regions,
        IReadOnlyList<BorderLineWrite> lines,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task DeleteExceptSourcesAsync(
        IReadOnlyCollection<string> sourceKeys,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
