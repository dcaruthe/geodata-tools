using System.Diagnostics;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Storage;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Infrastructure.Geodata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Geospatial;

internal sealed class CachedBorderGeometryLoader(
    IGeometryCache cache,
    ISettingsStore settings,
    IOptions<GeodataOptions> options,
    TimeProvider clock,
    IRegionIndexBuilder indexBuilder,
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
        var cacheMisses = new List<int>();
        var indexMisses = new List<int>();
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
            var cacheHit = false;
            if (entry is not null)
            {
                try
                {
                    blobs[index] = GeometryBlobCodec.Decode(entry.Blob);
                    cacheHit = true;
                    cacheHits++;
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

            if (!cacheHit)
                cacheMisses.Add(index);

            if (!await indexBuilder.IsIndexedAsync(keys[index], cancellationToken))
                indexMisses.Add(index);

            if (cacheHit)
                Report();
        }

        var toParse = new HashSet<int>(cacheMisses);
        toParse.UnionWith(indexMisses);
        var parseList = toParse.Order().ToArray();
        var parsedFiles = new ParsedBorderFile?[files.Length];

        Parallel.For(0, parseList.Length, index =>
        {
            var fileIndex = parseList[index];
            parsedFiles[fileIndex] = GeoJsonSeqParser.ParseFile(files[fileIndex], tolerance);
            if (cacheMisses.Contains(fileIndex))
                Report();
        });

        var cacheWriteFailed = false;
        foreach (var index in cacheMisses)
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

        var indexStopwatch = Stopwatch.StartNew();
        var indexRegionCount = 0;
        var indexLineCount = 0;
        foreach (var index in indexMisses)
        {
            var parsed = parsedFiles[index]!;
            if (parsed.ReadFailed)
                continue;

            await indexBuilder.WriteAsync(
                RegionOf(files[index]),
                keys[index],
                parsed.Features,
                cancellationToken
            );

            var (regions, lines) = CountIndexRows(parsed.Features);
            indexRegionCount += regions;
            indexLineCount += lines;
        }
        indexStopwatch.Stop();

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

        var geometry = BorderGeometry.FromGeo(
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
        {
            await cache.DeleteExceptAsync(keys, cancellationToken);
            await indexBuilder.DeleteExceptAsync(keys, cancellationToken);
        }

        logger.LogInformation(
            "Loaded {FileCount} border files from {Directory} ({CacheHits} cached, {ParsedFiles} parsed): {FeatureCount} features, {PolylineCount} polylines, {PointCount} points, {RegionCount} regions, {LineCount} lines (index {IndexMilliseconds} ms) in {ElapsedMilliseconds} ms",
            files.Length,
            directory,
            cacheHits,
            cacheMisses.Count,
            geometry.Polylines.Count == 0 ? 0 : featureCount,
            geometry.Polylines.Count,
            geometry.Polylines.Sum(polyline => polyline.Points.Count),
            indexRegionCount,
            indexLineCount,
            indexStopwatch.ElapsedMilliseconds,
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
            cacheMisses.Count
        );
    }

    private static (int Regions, int Lines) CountIndexRows(IReadOnlyList<ParsedFeature> features) =>
        (
            features
                .Where(feature => feature.DedupeId is not null)
                .Select(feature => feature.DedupeId!)
                .Distinct()
                .Count(),
            features.Count(feature => feature.DedupeId is null)
        );

    private static BorderLoadResult Empty() => new(BorderGeometry.Empty, string.Empty, [], 0, 0);

    private static string RegionOf(string file) => Path.GetFileNameWithoutExtension(file);
}
