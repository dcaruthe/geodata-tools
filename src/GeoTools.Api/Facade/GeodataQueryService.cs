using GeoTools.Api.Contracts;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.Storage;
using GeoTools.Shared.Domain.Geospatial;
using Microsoft.Extensions.Options;

namespace GeoTools.Api.Facade;

// Reads border geometry from the border files and the launcher's cache, and never writes.
public sealed class GeodataQueryService(
    IBorderGeometryLoader loader,
    IOptions<GeodataOptions> options,
    TimeProvider clock,
    ILogger<GeodataQueryService> logger
)
{
    private const int DefaultLimit = 200;
    private const int MaxLimit = 2000;
    private const int MaxGeoJsonPoints = 100_000;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private GeometrySnapshot? _snapshot;

    public string BordersDirectory =>
        System.IO.Path.Combine(
            RepositoryPaths.Resolve(options.Value.RootPath),
            options.Value.BordersSubdirectory
        );

    public int BorderFileCount
    {
        get
        {
            var directory = BordersDirectory;
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.geojsonseq").Length
                : 0;
        }
    }

    public async Task<(BorderGeometry Geometry, int[] Levels)> GetGeometryAsync(
        CancellationToken cancellationToken
    )
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is not null && clock.GetUtcNow() - snapshot.LoadedAt < Ttl)
            return (snapshot.Geometry, snapshot.Levels);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            snapshot = _snapshot;
            if (snapshot is not null && clock.GetUtcNow() - snapshot.LoadedAt < Ttl)
                return (snapshot.Geometry, snapshot.Levels);

            var result = await loader.LoadAsync(progress: null, cancellationToken);
            var next = new GeometrySnapshot(
                result.Geometry,
                result.AvailableLevels.ToArray(),
                clock.GetUtcNow()
            );
            Volatile.Write(ref _snapshot, next);
            logger.LogInformation(
                "Loaded {PolylineCount} border polylines at levels {Levels}.",
                result.Geometry.Polylines.Count,
                string.Join(',', next.Levels)
            );
            return (next.Geometry, next.Levels);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BorderQueryResponse> QueryAsync(
        GeoBounds? bounds,
        IReadOnlyList<int>? adminLevels,
        int limit,
        string format,
        CancellationToken cancellationToken
    )
    {
        var normalizedFormat = (format ?? "summary").Trim().ToLowerInvariant();
        if (normalizedFormat is not ("summary" or "geojson"))
            throw new ArgumentException("Format must be summary or geojson.", nameof(format));

        var pageSize = Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaxLimit);
        var (geometry, available) = await GetGeometryAsync(cancellationToken);
        var levels = (adminLevels is { Count: > 0 } ? adminLevels : available)
            .Where(level => level > 0)
            .Distinct()
            .Order()
            .ToArray();

        var selected = geometry
            .Polylines.Where(polyline => levels.Contains(polyline.AdminLevel))
            .Where(polyline => bounds is null || Intersects(polyline, bounds.Value))
            .ToArray();

        var summary = new BorderSummaryDto(
            selected.Length,
            selected.Sum(polyline => polyline.Points.Count),
            selected
                .GroupBy(polyline => polyline.AdminLevel)
                .OrderBy(group => group.Key)
                .Select(group => new AdminLevelCountDto(group.Key, group.Count()))
                .ToArray(),
            levels,
            GeoBounds.FromPolylines(selected, geometry.Bounds),
            selected.Length > pageSize
        );

        if (normalizedFormat == "summary")
            return new BorderQueryResponse(summary, null);

        if (summary.PointCount > MaxGeoJsonPoints)
            throw new BorderQueryTooLargeException(
                $"The query matches {summary.PointCount} points, over the {MaxGeoJsonPoints} point limit. "
                    + "Narrow the bounds, pick fewer admin levels, or use format=summary."
            );

        var features = selected
            .Take(pageSize)
            .Select(
                (polyline, index) =>
                    new GeoJsonFeature(
                        "Feature",
                        new GeoJsonGeometry(
                            "LineString",
                            polyline
                                .Points.Select(point => new[] { point.Longitude, point.Latitude })
                                .ToArray()
                        ),
                        polyline.AdminLevel,
                        index
                    )
            )
            .ToArray();

        return new BorderQueryResponse(
            summary,
            new GeoJsonResponse("FeatureCollection", features, selected.Length > pageSize)
        );
    }

    public static GeoBounds? ParseBounds(
        double? minLongitude,
        double? minLatitude,
        double? maxLongitude,
        double? maxLatitude
    )
    {
        if (
            minLongitude is null
            && minLatitude is null
            && maxLongitude is null
            && maxLatitude is null
        )
            return null;

        if (
            minLongitude is null
            || minLatitude is null
            || maxLongitude is null
            || maxLatitude is null
        )
            throw new ArgumentException("Bounds need all four values.");

        var bounds = new GeoBounds(
            minLongitude.Value,
            minLatitude.Value,
            maxLongitude.Value,
            maxLatitude.Value
        );

        if (bounds.MinLongitude > bounds.MaxLongitude || bounds.MinLatitude > bounds.MaxLatitude)
            throw new ArgumentException("Bounds must have min values at or below max values.");

        if (
            bounds.MinLongitude < -180
            || bounds.MaxLongitude > 180
            || bounds.MinLatitude < -90
            || bounds.MaxLatitude > 90
        )
            throw new ArgumentException("Bounds are outside the world extent.");

        return bounds;
    }

    private static bool Intersects(GeoPolyline polyline, GeoBounds bounds) =>
        polyline.Points.Any(point =>
            point.Longitude >= bounds.MinLongitude
            && point.Longitude <= bounds.MaxLongitude
            && point.Latitude >= bounds.MinLatitude
            && point.Latitude <= bounds.MaxLatitude
        );

    private sealed record GeometrySnapshot(
        BorderGeometry Geometry,
        int[] Levels,
        DateTimeOffset LoadedAt
    );
}
