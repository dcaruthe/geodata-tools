using FluentAssertions;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Infrastructure.Geospatial;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class GeometryBlobCodecTests
{
    [Fact]
    public void Encode_and_decode_round_trip()
    {
        var features = new[]
        {
            new GeometryFeature(
                "relation/1",
                [
                    new GeoPolyline(
                        [
                            new GeoPoint(-10.1234567, -20.7654321),
                            new GeoPoint(30.5, 40.25),
                            new GeoPoint(-10.1234567, -20.7654321),
                        ],
                        Closed: true,
                        AdminLevel: 2
                    ),
                ]
            ),
            new GeometryFeature(null, []),
        };
        var bounds = new GeoBounds(-10.1234567, -20.7654321, 30.5, 40.25);

        var blob = GeometryBlobCodec.Encode(features, bounds);
        var decoded = GeometryBlobCodec.Decode(blob);

        decoded.Features.Should().HaveCount(2);
        decoded.Features[0].DedupeId.Should().Be("relation/1");
        decoded.Features[0].Polylines.Should().ContainSingle();
        decoded.Features[0].Polylines[0].AdminLevel.Should().Be(2);
        decoded.Features[0].Polylines[0].Closed.Should().BeTrue();
        decoded
            .Features[0]
            .Polylines[0]
            .Points[0]
            .Longitude.Should()
            .BeApproximately(-10.1234567, 1e-7);
        decoded
            .Features[0]
            .Polylines[0]
            .Points[0]
            .Latitude.Should()
            .BeApproximately(-20.7654321, 1e-7);
        decoded.Bounds.Should().Be(bounds);
    }

    [Fact]
    public void Decode_rejects_an_empty_blob()
    {
        var act = () => GeometryBlobCodec.Decode([]);

        act.Should().Throw<EndOfStreamException>();
    }

    [Fact]
    public void Decode_rejects_an_unknown_magic()
    {
        var act = () => GeometryBlobCodec.Decode(new byte[64]);

        act.Should().Throw<InvalidDataException>().WithMessage("*format*");
    }

    [Fact]
    public void Decode_rejects_an_unknown_version()
    {
        var blob = BuildBlob(writer =>
        {
            writer.Write(0x4754_4231);
            writer.Write(99);
        });

        var act = () => GeometryBlobCodec.Decode(blob);

        act.Should().Throw<InvalidDataException>().WithMessage("*version*");
    }

    [Fact]
    public void Decode_rejects_an_invalid_feature_count()
    {
        var blob = BuildBlob(writer =>
        {
            writer.Write(0x4754_4231);
            writer.Write(1);
            writer.Write(-1);
        });

        var act = () => GeometryBlobCodec.Decode(blob);

        act.Should().Throw<InvalidDataException>().WithMessage("*feature count*");
    }

    [Fact]
    public void Decode_rejects_an_invalid_point()
    {
        var blob = BuildBlob(writer =>
        {
            writer.Write(0x4754_4231);
            writer.Write(1);
            writer.Write(1);
            writer.Write(false);
            writer.Write(1);
            writer.Write(2);
            writer.Write(true);
            writer.Write(1);
            writer.Write(int.MaxValue);
            writer.Write(0);
            writer.Write(0d);
            writer.Write(0d);
            writer.Write(0d);
            writer.Write(0d);
        });

        var act = () => GeometryBlobCodec.Decode(blob);

        act.Should().Throw<InvalidDataException>().WithMessage("*invalid point*");
    }

    [Fact]
    public void Encode_rejects_an_out_of_range_coordinate()
    {
        var features = new[]
        {
            new GeometryFeature(
                null,
                [
                    new GeoPolyline(
                        [new GeoPoint(500, 0), new GeoPoint(1, 1)],
                        Closed: false,
                        AdminLevel: 2
                    ),
                ]
            ),
        };

        var act = () => GeometryBlobCodec.Encode(features, GeoBounds.World);

        act.Should().Throw<InvalidDataException>();
    }

    private static byte[] BuildBlob(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            write(writer);

        return stream.ToArray();
    }
}
