using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Infrastructure.Geospatial;
using GeoTools.Shared.Infrastructure.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Geodata;

internal sealed record ExtractionRequest(
    string Region,
    string SourcePath,
    string BordersPath,
    string LevelsPath,
    string WorkDirectory,
    IReadOnlyList<int> Levels,
    double Tolerance
);

internal sealed record ExtractionResult(
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    int FeatureCount,
    GeoBounds Bounds
);

internal interface IBorderExtractor
{
    Task<ExtractionResult> ExtractAsync(
        ExtractionRequest request,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default
    );
}

internal sealed class BorderExtractor(
    IProcessRunner runner,
    IGeometryCache cache,
    IOptions<GeodataOptions> options,
    TimeProvider clock,
    ILogger<BorderExtractor> logger
) : IBorderExtractor
{
    private static readonly Regex PercentPattern = new(
        @"(\d{1,3})\s*%",
        RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    public async Task<ExtractionResult> ExtractAsync(
        ExtractionRequest request,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default
    )
    {
        var osmium = options.Value.OsmiumPath;
        var adminPath = Path.Combine(request.WorkDirectory, $"{request.Region}-admin.osm.pbf");
        var boundaryPath = Path.Combine(
            request.WorkDirectory,
            $"{request.Region}-boundary.osm.pbf"
        );
        var rawPath = request.BordersPath + ".raw";
        var temporaryPath = request.BordersPath + ".tmp";

        Directory.CreateDirectory(request.WorkDirectory);
        var bordersDirectory = Path.GetDirectoryName(request.BordersPath);
        if (!string.IsNullOrEmpty(bordersDirectory))
            Directory.CreateDirectory(bordersDirectory);

        Delete(request.BordersPath);
        Delete(request.LevelsPath);
        Delete(rawPath);
        Delete(temporaryPath);

        var levels = request.Levels.Order().Distinct().ToArray();
        var levelExpressions = levels.Select(level => $"r/admin_level={level}").ToArray();

        try
        {
            await RunAsync(
                osmium,
                [
                    "tags-filter",
                    "-O",
                    "-o",
                    adminPath,
                    request.SourcePath,
                    .. levelExpressions,
                    "--progress",
                ],
                0.0,
                0.35,
                "level filter",
                progress,
                cancellationToken
            );

            await RunAsync(
                osmium,
                [
                    "tags-filter",
                    "-O",
                    "-o",
                    boundaryPath,
                    adminPath,
                    "r/boundary=administrative",
                    "--progress",
                ],
                0.35,
                0.2,
                "boundary filter",
                progress,
                cancellationToken
            );

            await RunAsync(
                osmium,
                [
                    "export",
                    "-O",
                    "-f",
                    "geojsonseq",
                    "--geometry-types=polygon,linestring",
                    "-a",
                    "id",
                    "-o",
                    rawPath,
                    boundaryPath,
                    "--progress",
                ],
                0.55,
                0.3,
                "export",
                progress,
                cancellationToken
            );

            progress?.Report(0.85);
            var features = await PostFilterAsync(
                rawPath,
                temporaryPath,
                levels.ToHashSet(),
                request.Tolerance,
                cancellationToken
            );
            progress?.Report(0.99);

            File.Move(temporaryPath, request.BordersPath, overwrite: true);
            await File.WriteAllTextAsync(
                request.LevelsPath,
                string.Join(',', levels),
                cancellationToken
            );

            var info = new FileInfo(request.BordersPath);
            var polylines = features.SelectMany(feature => feature.Polylines).ToArray();
            var bounds = GeoBounds.FromPolylines(polylines, GeoBounds.World);

            await CacheAsync(request, features, bounds, cancellationToken);

            Delete(adminPath);
            Delete(boundaryPath);
            Delete(rawPath);

            logger.LogInformation(
                "Extracted {Region} at levels {Levels}: {FeatureCount} features, {Size} bytes.",
                request.Region,
                string.Join(',', levels),
                features.Count,
                info.Length
            );

            progress?.Report(1);

            return new ExtractionResult(
                info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                features.Count,
                bounds
            );
        }
        catch
        {
            Delete(temporaryPath);
            Delete(rawPath);
            Delete(request.BordersPath);
            Delete(request.LevelsPath);
            throw;
        }
    }

    private async Task CacheAsync(
        ExtractionRequest request,
        IReadOnlyList<GeometryFeature> features,
        GeoBounds bounds,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var key = BorderCacheKeys.Compute(
                request.BordersPath,
                request.Tolerance,
                request.Levels
            );
            await cache.PutAsync(
                new GeometryCacheEntry(
                    key,
                    request.Region,
                    request.Levels,
                    request.Tolerance,
                    GeometryBlobCodec.Version,
                    features.Sum(feature =>
                        feature.Polylines.Sum(polyline => polyline.Points.Count)
                    ),
                    bounds,
                    GeometryBlobCodec.Encode(features, bounds),
                    clock.GetUtcNow()
                ),
                cancellationToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Could not cache the geometry for {Region}; the next load will parse it.",
                request.Region
            );
        }
    }

    private async Task RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        double start,
        double span,
        string stage,
        IProgress<double>? progress,
        CancellationToken cancellationToken
    )
    {
        progress?.Report(start);
        var result = await runner.RunAsync(
            new ProcessSpec(fileName, arguments),
            line =>
            {
                var match = PercentPattern.Match(line);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
                {
                    progress?.Report(start + span * Math.Clamp(percent, 0, 100) / 100.0);
                }
            },
            cancellationToken
        );

        if (result.ExitCode != 0)
        {
            var detail = Tail(result.StandardError);
            throw new InvalidOperationException(
                $"{stage} failed with exit code {result.ExitCode}: {detail}"
            );
        }

        progress?.Report(start + span);
    }

    private static async Task<List<GeometryFeature>> PostFilterAsync(
        string source,
        string target,
        IReadOnlySet<int> levels,
        double tolerance,
        CancellationToken cancellationToken
    )
    {
        var features = new List<GeometryFeature>();

        await using var output = new StreamWriter(
            target,
            append: false,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );

        foreach (var rawLine in File.ReadLines(source))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = GeoJsonSeqParser.NormalizeLine(rawLine);
            if (line.Length == 0)
                continue;

            var skippedGeometry = 0;
            ParsedFeature? feature;
            try
            {
                if (
                    !GeoJsonSeqParser.TryParse(line, tolerance, out feature, ref skippedGeometry)
                    || feature is null
                )
                    continue;
            }
            catch (JsonException)
            {
                continue;
            }

            if (!levels.Contains(feature.AdminLevel))
                continue;

            await output.WriteAsync('\u001e');
            await output.WriteAsync(line);
            await output.WriteAsync('\n');
            features.Add(new GeometryFeature(feature.DedupeId, feature.Polylines));
        }

        return features;
    }

    private static string Tail(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', lines.TakeLast(3)).Trim();
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
