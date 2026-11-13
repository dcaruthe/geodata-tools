using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.State;

// The single writer for the persistent region index. Only the launcher resolves parents and
// writes rows; the API resolves a read-only no-op writer instead.
public interface IRegionIndexWriter
{
    Task WriteSourceAsync(
        string region,
        string sourceKey,
        IReadOnlyList<RegionWrite> regions,
        IReadOnlyList<BorderLineWrite> lines,
        CancellationToken cancellationToken = default
    );

    Task DeleteExceptSourcesAsync(
        IReadOnlyCollection<string> sourceKeys,
        CancellationToken cancellationToken = default
    );

    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed record RegionWrite(
    string Id,
    string Region,
    string OsmId,
    string? Name,
    string? BorderType,
    int AdminLevel,
    GeoBounds Bounds,
    double Area,
    int PointCount,
    byte[] Blob
);

public sealed record BorderLineWrite(
    string Id,
    string Region,
    int AdminLevel,
    GeoBounds Bounds,
    int PointCount,
    byte[] Blob
);
