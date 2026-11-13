using System.Globalization;
using System.Text.Json;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Infrastructure.Geospatial;

internal sealed record ParsedFeature(
    int AdminLevel,
    string? DedupeId,
    string? Name,
    string? BorderType,
    IReadOnlyList<GeoPolyline> Polylines,
    IReadOnlyList<GeoRing> Rings
);

internal sealed record ParsedBorderFile(
    IReadOnlyList<ParsedFeature> Features,
    int FeatureCount,
    int SkippedFeatures,
    int SkippedGeometry,
    bool ReadFailed
);

internal static class GeoJsonSeqParser
{
    private const char RecordSeparator = '\u001e';
    private const char ByteOrderMark = '\uFEFF';

    public static string NormalizeLine(string rawLine) =>
        rawLine.TrimStart(ByteOrderMark).Trim().TrimStart(RecordSeparator).Trim();

    public static bool TryParse(
        string line,
        double tolerance,
        out ParsedFeature? feature,
        ref int skippedGeometry
    )
    {
        feature = null;

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (
            !root.TryGetProperty("properties", out var properties)
            || !TryReadAdminLevel(properties, out var adminLevel)
            || !root.TryGetProperty("geometry", out var geometry)
            || geometry.ValueKind != JsonValueKind.Object
            || !TryReadGeometryType(geometry, out var geometryType)
        )
            return false;

        string? dedupeId = null;
        if (geometryType is "Polygon" or "MultiPolygon" && TryReadId(properties, out var id))
            dedupeId = id;

        var name = TryReadString(properties, "name");
        var borderType = TryReadString(properties, "border_type");

        var (polylines, rings) = ReadGeometry(
            geometry,
            geometryType,
            adminLevel,
            tolerance,
            ref skippedGeometry
        );

        feature = new ParsedFeature(adminLevel, dedupeId, name, borderType, polylines, rings);
        return true;
    }

    public static ParsedBorderFile ParseFile(string file, double tolerance)
    {
        var features = new List<ParsedFeature>();
        var featureCount = 0;
        var skippedFeatures = 0;
        var skippedGeometry = 0;
        var readFailed = false;

        try
        {
            foreach (var rawLine in File.ReadLines(file))
            {
                var line = NormalizeLine(rawLine);
                if (line.Length == 0)
                    continue;

                try
                {
                    if (
                        TryParse(line, tolerance, out var feature, ref skippedGeometry)
                        && feature is not null
                    )
                    {
                        features.Add(feature);
                        featureCount++;
                    }
                    else
                    {
                        skippedFeatures++;
                    }
                }
                catch (JsonException)
                {
                    skippedFeatures++;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            readFailed = true;
        }

        return new ParsedBorderFile(
            features,
            featureCount,
            skippedFeatures,
            skippedGeometry,
            readFailed
        );
    }

    private static (List<GeoPolyline> Polylines, List<GeoRing> Rings) ReadGeometry(
        JsonElement geometry,
        string geometryType,
        int adminLevel,
        double tolerance,
        ref int skippedGeometry
    )
    {
        var polylines = new List<GeoPolyline>();
        var rings = new List<GeoRing>();
        if (!geometry.TryGetProperty("coordinates", out var coordinates))
            return (polylines, rings);

        if (coordinates.ValueKind != JsonValueKind.Array)
        {
            skippedGeometry++;
            return (polylines, rings);
        }

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
                        rings,
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
                        rings,
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
                    rings,
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
                        rings,
                        ref skippedGeometry
                    );
                break;
        }

        return (polylines, rings);
    }

    private static void AddPolyline(
        JsonElement coordinates,
        bool closed,
        int adminLevel,
        double tolerance,
        List<GeoPolyline> polylines,
        List<GeoRing> rings,
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
        if (closed)
            rings.Add(new GeoRing(simplified));
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

    private static string? TryReadString(JsonElement properties, string name)
    {
        if (
            properties.ValueKind != JsonValueKind.Object
            || !properties.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.String
        )
            return null;

        return element.GetString();
    }
}
