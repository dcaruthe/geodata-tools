using System.Diagnostics;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Storage;
using GeoTools.Shared.Domain.Geospatial;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Geospatial;

internal sealed class CachedBorderGeometryLoader(
    IGeometryCache cache,
    ISettingsStore settings,
    IOptions<GeodataOptions> options,
    TimeProvider clock,
    ILogger<CachedBorderGeometryLoader> logger
) : IBorderGeometryLoader
{
    public async Task<BorderLoadResult> LoadAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var geodata = options.Value;
        var settingsValue = settings.Geodata;
        var tolerance = settingsValue.SimplifyToleranceDegrees;
        var levels = settingsValue.ExtractAdminLevels;
        var directory = Path.Combine(
            RepositoryPaths.Resolve(geodata.RootPath),
            geodata.BordersSubdirectory
        );

        if (!Directory.Exists(directory))
        {
            logger.LogWarning("Border directory {Directory} does not exist.", directory);
            return Empty();
        }

        var files = Directory.GetFiles(directory, "*.geojsonseq");
        Array.Sort(files, StringComparer.Ordinal);
        if (files.Length == 0)
        {
            logger.LogWarning("No border files found in {Directory}.", directory);
            return Empty();
        }

        var keys = new string[files.Length];
        var blobs = new GeometryBlob?[files.Length];
        var misses = new List<int>();
        var cacheHits = 0;
        var processed = 0;
        var progressGate = new object();

        void Report()
        {
            lock (progressGate)
            {
                processed++;
                progress?.Report((double)processed / files.Length);
            }
        }

        for (var index = 0; index < files.Length; index++)
        {
            keys[index] = BorderCacheKeys.Compute(files[index], tolerance, levels);
            var entry = await cache.TryGetAsync(keys[index], cancellationToken);
            if (entry is not null)
            {
                try
                {
                    blobs[index] = GeometryBlobCodec.Decode(entry.Blob);
                    cacheHits++;
                    Report();
                    continue;
                }
                catch (Exception exception)
                    when (exception is InvalidDataException or EndOfStreamException)
                {
                    logger.LogWarning(
                        exception,
                        "Ignoring a corrupt geometry cache entry for {File}.",
                        Path.GetFileName(files[index])
                    );
                }
            }

            misses.Add(index);
        }

        var parsedFiles = new ParsedBorderFile?[files.Length];
        Parallel.For(0, misses.Count, index =>
        {
            parsedFiles[misses[index]] = GeoJsonSeqParser.ParseFile(
                files[misses[index]],
                tolerance
            );
            Report();
        });

        var cacheWriteFailed = false;
        foreach (var index in misses)
        {
            var parsed = parsedFiles[index]!;
            if (parsed.ReadFailed)
            {
                cacheWriteFailed = true;
                logger.LogWarning("Could not read border file {File}.", files[index]);
            }

            var features = parsed
                .Features.Select(feature => new GeometryFeature(
                    feature.DedupeId,
                    feature.Polylines
                ))
                .ToArray();
            var bounds = GeoBounds.FromPolylines(
                features.SelectMany(feature => feature.Polylines),
                GeoBounds.World
            );
            var blob = new GeometryBlob(features, bounds);
            blobs[index] = blob;

            if (parsed.ReadFailed)
                continue;

            await cache.PutAsync(
                new GeometryCacheEntry(
                    keys[index],
                    RegionOf(files[index]),
                    levels,
                    tolerance,
                    GeometryBlobCodec.Version,
                    blob.Features.Sum(feature =>
                        feature.Polylines.Sum(polyline => polyline.Points.Count)
                    ),
                    bounds,
                    GeometryBlobCodec.Encode(features, bounds),
                    clock.GetUtcNow()
                ),
                cancellationToken
            );
        }

        var polylines = new List<GeoPolyline>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var featureCount = 0;
        var skippedFeatures = 0;
        var skippedGeometry = 0;

        for (var index = 0; index < blobs.Length; index++)
        {
            var blob = blobs[index];
            if (blob is null)
                continue;

            var parsed = parsedFiles[index];
            if (parsed is not null)
            {
                skippedFeatures += parsed.SkippedFeatures;
                skippedGeometry += parsed.SkippedGeometry;
            }

            featureCount += blob.Features.Count;

            foreach (var feature in blob.Features)
            {
                if (feature.DedupeId is not null && !seen.Add(feature.DedupeId))
                    continue;

                polylines.AddRange(feature.Polylines);
            }
        }

        var geometry = new BorderGeometry(
            polylines,
            GeoBounds.FromPolylines(polylines, GeoBounds.World)
        );
        var availableLevels = polylines
            .Select(polyline => polyline.AdminLevel)
            .Distinct()
            .Order()
            .ToArray();
        var fingerprint = BorderCacheKeys.Fingerprint(keys);

        if (!cacheWriteFailed)
            await cache.DeleteExceptAsync(keys, cancellationToken);

        logger.LogInformation(
            "Loaded {FileCount} border files from {Directory} ({CacheHits} cached, {ParsedFiles} parsed): {FeatureCount} features, {PolylineCount} polylines, {PointCount} points in {ElapsedMilliseconds} ms",
            files.Length,
            directory,
            cacheHits,
            misses.Count,
            geometry.Polylines.Count == 0 ? 0 : featureCount,
            geometry.Polylines.Count,
            geometry.Polylines.Sum(polyline => polyline.Points.Count),
            stopwatch.ElapsedMilliseconds
        );

        if (skippedFeatures > 0)
            logger.LogWarning(
                "Skipped {Count} border features without a usable admin level or geometry.",
                skippedFeatures
            );
        if (skippedGeometry > 0)
            logger.LogWarning(
                "Skipped {Count} border polylines with invalid coordinates.",
                skippedGeometry
            );

        return new BorderLoadResult(
            geometry,
            fingerprint,
            availableLevels,
            cacheHits,
            misses.Count
        );
    }

    private static BorderLoadResult Empty() => new(BorderGeometry.Empty, string.Empty, [], 0, 0);

    private static string RegionOf(string file) => Path.GetFileNameWithoutExtension(file);
}
