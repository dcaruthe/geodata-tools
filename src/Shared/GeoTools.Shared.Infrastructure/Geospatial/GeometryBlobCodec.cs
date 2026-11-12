using System.Text;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Infrastructure.Geospatial;

internal sealed record GeometryFeature(string? DedupeId, IReadOnlyList<GeoPolyline> Polylines);

internal sealed record GeometryBlob(IReadOnlyList<GeometryFeature> Features, GeoBounds Bounds);

internal static class GeometryBlobCodec
{
    public const int Version = 1;

    private const int Magic = 0x4754_4231;
    private const double Scale = 10_000_000;
    private const int MaxFeatures = 1_000_000;
    private const int MaxPolylines = 1_000_000;
    private const int MaxPoints = 100_000_000;

    public static byte[] Encode(IReadOnlyList<GeometryFeature> features, GeoBounds bounds)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(features.Count);

        foreach (var feature in features)
        {
            writer.Write(feature.DedupeId is not null);
            if (feature.DedupeId is not null)
                writer.Write(feature.DedupeId);

            writer.Write(feature.Polylines.Count);
            foreach (var polyline in feature.Polylines)
            {
                writer.Write(polyline.AdminLevel);
                writer.Write(polyline.Closed);
                writer.Write(polyline.Points.Count);
                foreach (var point in polyline.Points)
                {
                    writer.Write(ToFixed(point.Longitude));
                    writer.Write(ToFixed(point.Latitude));
                }
            }
        }

        writer.Write(bounds.MinLongitude);
        writer.Write(bounds.MinLatitude);
        writer.Write(bounds.MaxLongitude);
        writer.Write(bounds.MaxLatitude);
        writer.Flush();

        return stream.ToArray();
    }

    public static GeometryBlob Decode(byte[] blob)
    {
        using var stream = new MemoryStream(blob, writable: false);
        using var reader = new BinaryReader(stream);

        if (reader.ReadInt32() != Magic)
            throw new InvalidDataException("The geometry cache blob has an unknown format.");

        var version = reader.ReadInt32();
        if (version != Version)
            throw new InvalidDataException($"Unsupported geometry cache version {version}.");

        var featureCount = reader.ReadInt32();
        if (featureCount is < 0 or > MaxFeatures)
            throw new InvalidDataException("The geometry cache feature count is invalid.");

        var features = new List<GeometryFeature>(featureCount);
        for (var featureIndex = 0; featureIndex < featureCount; featureIndex++)
        {
            string? dedupeId = null;
            if (reader.ReadBoolean())
                dedupeId = reader.ReadString();

            var polylineCount = reader.ReadInt32();
            if (polylineCount is < 0 or > MaxPolylines)
                throw new InvalidDataException("The geometry cache polyline count is invalid.");

            var polylines = new List<GeoPolyline>(polylineCount);
            for (var polylineIndex = 0; polylineIndex < polylineCount; polylineIndex++)
            {
                var adminLevel = reader.ReadInt32();
                var closed = reader.ReadBoolean();
                var pointCount = reader.ReadInt32();
                if (pointCount is < 0 or > MaxPoints)
                    throw new InvalidDataException("The geometry cache point count is invalid.");

                var points = new List<GeoPoint>(Math.Min(pointCount, 1024));
                for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
                {
                    var longitude = FromFixed(reader.ReadInt32());
                    var latitude = FromFixed(reader.ReadInt32());
                    if (longitude < -180 || longitude > 180 || latitude < -90 || latitude > 90)
                        throw new InvalidDataException(
                            "The geometry cache contains an invalid point."
                        );

                    points.Add(new GeoPoint(longitude, latitude));
                }

                polylines.Add(new GeoPolyline(points, closed, adminLevel));
            }

            features.Add(new GeometryFeature(dedupeId, polylines));
        }

        var bounds = new GeoBounds(
            reader.ReadDouble(),
            reader.ReadDouble(),
            reader.ReadDouble(),
            reader.ReadDouble()
        );

        if (
            !double.IsFinite(bounds.MinLongitude)
            || !double.IsFinite(bounds.MinLatitude)
            || !double.IsFinite(bounds.MaxLongitude)
            || !double.IsFinite(bounds.MaxLatitude)
        )
            throw new InvalidDataException("The geometry cache bounds are invalid.");

        return new GeometryBlob(features, bounds);
    }

    private static int ToFixed(double value)
    {
        if (!double.IsFinite(value) || value is < -180 or > 180)
            throw new InvalidDataException("A geometry coordinate is out of range.");

        return (int)Math.Round(value * Scale, MidpointRounding.AwayFromZero);
    }

    private static double FromFixed(int value) => value / Scale;
}
