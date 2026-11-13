using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Infrastructure.Geospatial;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Infrastructure.Geodata;

internal interface IRegionIndexBuilder
{
    Task<bool> IsIndexedAsync(string sourceKey, CancellationToken cancellationToken);

    Task<int> WriteAsync(
        string region,
        string sourceKey,
        IReadOnlyList<ParsedFeature> features,
        CancellationToken cancellationToken
    );

    Task DeleteExceptAsync(
        IReadOnlyCollection<string> sourceKeys,
        CancellationToken cancellationToken
    );
}

// Maps parsed border features to index rows. Polygon features with an id become region rows and
// everything else becomes a border-line row, so a cold load writes the index from the same parse
// that fills the geometry cache.
internal sealed class RegionIndexBuilder(
    IRegionIndex index,
    IRegionIndexWriter writer,
    ILogger<RegionIndexBuilder> logger
) : IRegionIndexBuilder
{
    public Task<bool> IsIndexedAsync(string sourceKey, CancellationToken cancellationToken) =>
        index.IsSourceIndexedAsync(sourceKey, cancellationToken);

    public async Task<int> WriteAsync(
        string region,
        string sourceKey,
        IReadOnlyList<ParsedFeature> features,
        CancellationToken cancellationToken
    )
    {
        var regions = new List<RegionWrite>();
        var lines = new List<BorderLineWrite>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lineOrdinal = 0;

        foreach (var feature in features)
        {
            if (feature.DedupeId is not null)
            {
                if (!seen.Add(feature.DedupeId))
                {
                    logger.LogDebug("Skipping duplicate region {Id}.", feature.DedupeId);
                    continue;
                }

                regions.Add(ToRegionWrite(region, feature));
            }
            else
            {
                lines.Add(ToLineWrite(region, feature, lineOrdinal++));
            }
        }

        await writer.WriteSourceAsync(region, sourceKey, regions, lines, cancellationToken);
        return regions.Count + lines.Count;
    }

    public Task DeleteExceptAsync(
        IReadOnlyCollection<string> sourceKeys,
        CancellationToken cancellationToken
    ) => writer.DeleteExceptSourcesAsync(sourceKeys, cancellationToken);

    private static RegionWrite ToRegionWrite(string region, ParsedFeature feature)
    {
        var polylines = feature.Polylines;
        var bounds = GeoBounds.FromPolylines(polylines, GeoBounds.World);
        var area = feature.Rings.Sum(RingArea);
        var blob = GeometryBlobCodec.Encode([new GeometryFeature(null, polylines)], bounds);

        return new RegionWrite(
            $"region:{feature.DedupeId}",
            region,
            feature.DedupeId!,
            feature.Name,
            feature.BorderType,
            feature.AdminLevel,
            bounds,
            area,
            polylines.Sum(polyline => polyline.Points.Count),
            blob
        );
    }

    private static BorderLineWrite ToLineWrite(string region, ParsedFeature feature, int ordinal)
    {
        var polylines = feature.Polylines;
        var bounds = GeoBounds.FromPolylines(polylines, GeoBounds.World);
        var blob = GeometryBlobCodec.Encode([new GeometryFeature(null, polylines)], bounds);

        return new BorderLineWrite(
            $"region:line:{region}:{ordinal}",
            region,
            feature.AdminLevel,
            bounds,
            polylines.Sum(polyline => polyline.Points.Count),
            blob
        );
    }

    private static double RingArea(GeoRing ring)
    {
        double sum = 0;
        for (var i = 0; i < ring.Points.Count - 1; i++)
        {
            var first = MapProjection.Project(ring.Points[i]);
            var second = MapProjection.Project(ring.Points[i + 1]);
            sum += first.X * second.Y - second.X * first.Y;
        }

        return Math.Abs(sum) / 2;
    }
}
