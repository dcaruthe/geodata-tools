using GeoTools.Shared.Application.State;
using Microsoft.Extensions.DependencyInjection;

namespace GeoTools.Shared.Infrastructure.State;

// The launcher is the only region-index writer. A read-only host resolves this no-op writer so
// its loader cannot race the launcher for writes; it still reads through IRegionIndex.
public sealed class ReadOnlyRegionIndexWriter : IRegionIndexWriter
{
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

    public Task<IReadOnlyList<RegionSummary>> ResolveChildrenAsync(
        string parentId,
        int childLevel,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<RegionSummary>>([]);
}

public static class ReadOnlyRegionIndexWriterExtensions
{
    public static IServiceCollection AddReadOnlyRegionIndexWriter(
        this IServiceCollection services
    ) => services.AddSingleton<IRegionIndexWriter>(_ => new ReadOnlyRegionIndexWriter());
}
