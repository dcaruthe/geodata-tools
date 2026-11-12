using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Domain.Geospatial;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Geospatial;

internal sealed class GeoJsonSeqBorderSource : IBorderSource
{
    private const char RecordSeparator = '\u001e';
    private const char ByteOrderMark = '\uFEFF';

    private readonly IOptionsMonitor<MapOptions> _options;
    private readonly ILogger<GeoJsonSeqBorderSource> _logger;
    private readonly object _gate = new();
    private BorderGeometry? _cached;

    public GeoJsonSeqBorderSource(
        IOptionsMonitor<MapOptions> options,
        ILogger<GeoJsonSeqBorderSource> logger
    )
    {
        _options = options;
        _logger = logger;
    }

    public BorderGeometry Load()
    {
        lock (_gate)
            return _cached ??= LoadCore();
    }

    private BorderGeometry LoadCore()
    {
        var stopwatch = Stopwatch.StartNew();
        var directory = ResolveBordersDirectory(_options.CurrentValue.BordersDirectory);

        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("Border directory {Directory} does not exist.", directory);
            return BorderGeometry.Empty;
        }

        var files = Directory.GetFiles(directory, "*.geojsonseq");
        Array.Sort(files, StringComparer.Ordinal);
        if (files.Length == 0)
        {
            _logger.LogWarning("No border files found in {Directory}.", directory);
            return BorderGeometry.Empty;
        }

        var tolerance = _options.CurrentValue.SimplifyToleranceDegrees;
        var results = new FileResult[files.Length];

        // Each continent file is independent, so parse and simplify them in
        // parallel. Merge in file order afterwards to keep the result deterministic.
        Parallel.For(0, files.Length, index => results[index] = LoadFile(files[index], tolerance));

        var polylines = new List<GeoPolyline>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var featureCount = 0;
        var skippedFeatures = 0;
        var skippedGeometry = 0;

        foreach (var result in results)
        {
            featureCount += result.FeatureCount;
            skippedFeatures += result.SkippedFeatures;
            skippedGeometry += result.SkippedGeometry;

            foreach (var feature in result.Features)
            {
                if (feature.DedupeId is not null && !seenIds.Add(feature.DedupeId))
                    continue;

                polylines.AddRange(feature.Polylines);
            }
        }

        var bounds = GeoBounds.FromPolylines(polylines, GeoBounds.World);
        var pointCount = polylines.Sum(polyline => polyline.Points.Count);

        _logger.LogInformation(
            "Loaded {FileCount} border files from {Directory}: {FeatureCount} features, {PolylineCount} polylines, {PointCount} points in {ElapsedMilliseconds} ms",
            files.Length,
            directory,
            featureCount,
            polylines.Count,
            pointCount,
            stopwatch.ElapsedMilliseconds
        );

        if (skippedFeatures > 0)
            _logger.LogWarning(
                "Skipped {Count} border features without a usable admin level or geometry.",
                skippedFeatures
            );
        if (skippedGeometry > 0)
            _logger.LogWarning(
                "Skipped {Count} border polylines with invalid coordinates.",
                skippedGeometry
            );

        return new BorderGeometry(polylines, bounds);
    }

    private FileResult LoadFile(string file, double tolerance)
    {
        var stopwatch = Stopwatch.StartNew();
        var features = new List<FeatureResult>();
        var featureCount = 0;
        var skippedFeatures = 0;
        var skippedGeometry = 0;

        try
        {
            foreach (var rawLine in File.ReadLines(file))
            {
                var line = rawLine
                    .TrimStart(ByteOrderMark)
                    .Trim()
                    .TrimStart(RecordSeparator)
                    .Trim();
                if (line.Length == 0)
                    continue;

                using var document = JsonDocument.Parse(line);
                var feature = document.RootElement;

                if (
                    !feature.TryGetProperty("properties", out var properties)
                    || !TryReadAdminLevel(properties, out var adminLevel)
                    || !feature.TryGetProperty("geometry", out var geometry)
                    || geometry.ValueKind != JsonValueKind.Object
                    || !TryReadGeometryType(geometry, out var geometryType)
                )
                {
                    skippedFeatures++;
                    continue;
                }

                featureCount++;

                string? dedupeId = null;
                if (
                    geometryType is "Polygon" or "MultiPolygon"
                    && TryReadId(properties, out var id)
                )
                    dedupeId = id;

                features.Add(
                    new FeatureResult(
                        dedupeId,
                        ReadGeometry(
                            geometry,
                            geometryType,
                            adminLevel,
                            tolerance,
                            ref skippedGeometry
                        )
                    )
                );
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(exception, "Could not read border file {File}.", file);
        }

        _logger.LogInformation(
            "Loaded border file {File}: {FeatureCount} features in {ElapsedMilliseconds} ms",
            Path.GetFileName(file),
            featureCount,
            stopwatch.ElapsedMilliseconds
        );

        return new FileResult(features, featureCount, skippedFeatures, skippedGeometry);
    }

    private static List<GeoPolyline> ReadGeometry(
        JsonElement geometry,
        string geometryType,
        int adminLevel,
        double tolerance,
        ref int skippedGeometry
    )
    {
        var polylines = new List<GeoPolyline>();
        if (!geometry.TryGetProperty("coordinates", out var coordinates))
            return polylines;

        switch (geometryType)
        {
            case "Polygon":
                foreach (var ring in coordinates.EnumerateArray())
                    AddPolyline(
                        ring,
                        closed: true,
                        adminLevel,
                        tolerance,
                        polylines,
                        ref skippedGeometry
                    );
                break;
            case "MultiPolygon":
                foreach (var polygon in coordinates.EnumerateArray())
                foreach (var ring in polygon.EnumerateArray())
                    AddPolyline(
                        ring,
                        closed: true,
                        adminLevel,
                        tolerance,
                        polylines,
                        ref skippedGeometry
                    );
                break;
            case "LineString":
                AddPolyline(
                    coordinates,
                    closed: false,
                    adminLevel,
                    tolerance,
                    polylines,
                    ref skippedGeometry
                );
                break;
            case "MultiLineString":
                foreach (var line in coordinates.EnumerateArray())
                    AddPolyline(
                        line,
                        closed: false,
                        adminLevel,
                        tolerance,
                        polylines,
                        ref skippedGeometry
                    );
                break;
        }

        return polylines;
    }

    private static void AddPolyline(
        JsonElement coordinates,
        bool closed,
        int adminLevel,
        double tolerance,
        List<GeoPolyline> polylines,
        ref int skippedGeometry
    )
    {
        if (coordinates.ValueKind != JsonValueKind.Array)
        {
            skippedGeometry++;
            return;
        }

        var points = new List<GeoPoint>();
        foreach (var coordinate in coordinates.EnumerateArray())
        {
            if (!TryReadPoint(coordinate, out var point))
            {
                skippedGeometry++;
                return;
            }

            points.Add(point);
        }

        if (points.Count == 0)
            return;

        if (closed && points[^1] != points[0])
            points.Add(points[0]);

        var simplified = PolylineSimplifier.Simplify(points, tolerance);
        if (simplified.Count == 0)
            return;

        polylines.Add(new GeoPolyline(simplified, closed, adminLevel));
    }

    private static bool TryReadPoint(JsonElement coordinate, out GeoPoint point)
    {
        point = default;

        if (coordinate.ValueKind != JsonValueKind.Array || coordinate.GetArrayLength() < 2)
            return false;

        if (
            coordinate[0].ValueKind != JsonValueKind.Number
            || coordinate[1].ValueKind != JsonValueKind.Number
            || !coordinate[0].TryGetDouble(out var longitude)
            || !coordinate[1].TryGetDouble(out var latitude)
        )
            return false;

        if (
            !double.IsFinite(longitude)
            || !double.IsFinite(latitude)
            || longitude < -180
            || longitude > 180
            || latitude < -90
            || latitude > 90
        )
            return false;

        point = new GeoPoint(longitude, latitude);
        return true;
    }

    private static bool TryReadGeometryType(JsonElement geometry, out string geometryType)
    {
        geometryType = string.Empty;
        if (
            !geometry.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String
        )
            return false;

        geometryType = type.GetString() ?? string.Empty;
        return geometryType is "Polygon" or "MultiPolygon" or "LineString" or "MultiLineString";
    }

    private static bool TryReadAdminLevel(JsonElement properties, out int adminLevel)
    {
        adminLevel = 0;
        if (
            properties.ValueKind != JsonValueKind.Object
            || !properties.TryGetProperty("admin_level", out var element)
        )
            return false;

        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDouble(out var number) && TryConvert(number, out adminLevel);
            case JsonValueKind.String:
                var text = element.GetString();
                return !string.IsNullOrWhiteSpace(text)
                    && double.TryParse(
                        text.Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var parsed
                    )
                    && TryConvert(parsed, out adminLevel);
            default:
                return false;
        }
    }

    private static bool TryConvert(double number, out int adminLevel)
    {
        adminLevel = 0;
        if (
            !double.IsFinite(number)
            || number <= 0
            || number != Math.Floor(number)
            || number > int.MaxValue
        )
            return false;

        adminLevel = (int)number;
        return true;
    }

    private static bool TryReadId(JsonElement properties, out string id)
    {
        id = string.Empty;
        if (
            properties.ValueKind != JsonValueKind.Object
            || !properties.TryGetProperty("@id", out var element)
        )
            return false;

        id =
            element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : element.GetRawText();
        return id.Length > 0;
    }

    private static string ResolveBordersDirectory(string configured)
    {
        if (Path.IsPathRooted(configured))
            return Path.GetFullPath(configured);

        return Path.GetFullPath(Path.Combine(FindRepositoryRoot(), configured));
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "geodata-tools.slnx")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private sealed record FileResult(
        List<FeatureResult> Features,
        int FeatureCount,
        int SkippedFeatures,
        int SkippedGeometry
    );

    private sealed record FeatureResult(string? DedupeId, List<GeoPolyline> Polylines);
}
