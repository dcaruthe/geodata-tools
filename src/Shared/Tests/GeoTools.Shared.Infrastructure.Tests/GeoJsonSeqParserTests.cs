using FluentAssertions;
using GeoTools.Shared.Infrastructure.Geospatial;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class GeoJsonSeqParserTests
{
    [Fact]
    public void ParseFile_reads_polygons_and_lines_and_keeps_every_level()
    {
        var directory = CreateDirectory();
        try
        {
            Write(
                Path.Combine(directory, "a.geojsonseq"),
                Feature(
                    """{"@id":"relation/1","admin_level":2}""",
                    """{"type":"Polygon","coordinates":[[[10,10],[20,10],[20,20],[10,10]]]}"""
                ),
                Feature(
                    """{"admin_level":4}""",
                    """{"type":"LineString","coordinates":[[-50,-20],[-40,-10],[-30,-20]]}"""
                ),
                Feature("""{}""", """{"type":"LineString","coordinates":[[0,0],[1,1]]}""")
            );

            var parsed = GeoJsonSeqParser.ParseFile(Path.Combine(directory, "a.geojsonseq"), 0.002);

            parsed.ReadFailed.Should().BeFalse();
            parsed.Features.Should().HaveCount(2);
            parsed.Features.Select(feature => feature.AdminLevel).Should().Equal(2, 4);
            parsed.Features[0].DedupeId.Should().Be("relation/1");
            parsed.Features[0].Polylines.Should().ContainSingle();
            parsed.Features[0].Polylines[0].Closed.Should().BeTrue();
            parsed
                .Features[0]
                .Polylines[0]
                .Points[0]
                .Should()
                .Be(parsed.Features[0].Polylines[0].Points[^1]);
            parsed.Features[1].Polylines[0].Closed.Should().BeFalse();
            parsed.SkippedFeatures.Should().Be(1);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseFile_skips_malformed_lines_and_keeps_the_rest()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "a.geojsonseq");
            File.WriteAllText(
                path,
                "\u001e{"
                    + "\n"
                    + "\u001e"
                    + Feature(
                        """{"admin_level":2}""",
                        """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                    )
                    + "\n"
            );

            var parsed = GeoJsonSeqParser.ParseFile(path, 0.002);

            parsed.Features.Should().ContainSingle();
            parsed.SkippedFeatures.Should().Be(1);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseFile_counts_invalid_coordinates_as_skipped_geometry()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "a.geojsonseq");
            Write(
                path,
                Feature(
                    """{"admin_level":2}""",
                    """{"type":"LineString","coordinates":[[0,0],[200,1]]}"""
                ),
                Feature("""{"admin_level":2}""", """{"type":"LineString","coordinates":"nope"}""")
            );

            var parsed = GeoJsonSeqParser.ParseFile(path, 0.002);

            parsed.SkippedGeometry.Should().Be(2);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseFile_reports_a_missing_file()
    {
        var parsed = GeoJsonSeqParser.ParseFile(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.geojsonseq"),
            0.002
        );

        parsed.ReadFailed.Should().BeTrue();
        parsed.Features.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeLine_strips_the_record_separator_bom_and_space()
    {
        GeoJsonSeqParser.NormalizeLine("\uFEFF\u001e  {\"a\":1}  ").Should().Be("{\"a\":1}");
    }

    [Fact]
    public void ParseFile_handles_multi_geometries_and_unusual_property_types()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "a.geojsonseq");
            Write(
                path,
                Feature(
                    """{"@id":42,"admin_level":"4"}""",
                    """{"type":"MultiPolygon","coordinates":[[[[0,0],[1,0],[1,1],[0,0]]],[[[10,10],[11,10],[11,11],[10,10]]]]}"""
                ),
                Feature(
                    """{"admin_level":2}""",
                    """{"type":"MultiLineString","coordinates":[[[0,0],[1,1]],[[2,2],[3,3]]]}"""
                ),
                Feature(
                    """{"admin_level":2.5}""",
                    """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                ),
                Feature(
                    """{"admin_level":0}""",
                    """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                ),
                Feature(
                    """{"admin_level":"x"}""",
                    """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                ),
                Feature("""{"admin_level":2}""", """{"type":"Point","coordinates":[0,0]}"""),
                Feature("""{"admin_level":2}""", """{"coordinates":[[0,0],[1,1]]}"""),
                Feature("""{"admin_level":2}""", """{"type":"LineString","coordinates":"nope"}"""),
                "[]",
                Feature("""{"admin_level":2}""", """{"type":"LineString"}""")
            );

            var parsed = GeoJsonSeqParser.ParseFile(path, 0.002);

            parsed.ReadFailed.Should().BeFalse();
            parsed.Features.Should().HaveCount(4);
            parsed.Features[0].DedupeId.Should().Be("42");
            parsed.Features[0].Polylines.Should().HaveCount(2);
            parsed.Features[1].Polylines.Should().HaveCount(2);
            parsed.SkippedFeatures.Should().Be(6);
            parsed.SkippedGeometry.Should().Be(1);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseFile_closes_open_rings_and_rejects_unusable_levels()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "a.geojsonseq");
            Write(
                path,
                Feature(
                    """{"admin_level":2}""",
                    """{"type":"Polygon","coordinates":[[[10,10],[20,10],[20,20]]]}"""
                ),
                Feature(
                    """{"admin_level":1e300}""",
                    """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                ),
                Feature(
                    """{"admin_level":0.5}""",
                    """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                ),
                Feature("""[]""", """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""),
                Feature("""{"admin_level":2}""", """"x""""),
                Feature("""{"admin_level":2}""", """{"type":"LineString","coordinates":[]}""")
            );

            var parsed = GeoJsonSeqParser.ParseFile(path, 0.002);

            parsed.Features.Should().HaveCount(2);
            parsed.Features[0].Polylines.Should().ContainSingle();
            parsed.Features[0].Polylines[0].Closed.Should().BeTrue();
            parsed
                .Features[0]
                .Polylines[0]
                .Points[0]
                .Should()
                .Be(parsed.Features[0].Polylines[0].Points[^1]);
            parsed.Features[1].Polylines.Should().BeEmpty();
            parsed.SkippedFeatures.Should().Be(4);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseFile_reads_name_border_type_and_closed_rings()
    {
        var directory = CreateDirectory();
        try
        {
            Write(
                Path.Combine(directory, "a.geojsonseq"),
                Feature(
                    """{"@id":"relation/1","admin_level":2,"name":"Alpha","border_type":"nation"}""",
                    """{"type":"Polygon","coordinates":[[[10,10],[20,10],[20,20],[10,10]]]}"""
                )
            );

            var parsed = GeoJsonSeqParser.ParseFile(Path.Combine(directory, "a.geojsonseq"), 0.002);

            parsed.Features.Should().ContainSingle();
            var feature = parsed.Features[0];
            feature.Name.Should().Be("Alpha");
            feature.BorderType.Should().Be("nation");
            feature.Rings.Should().ContainSingle();
            feature.Rings[0].Points.Should().Equal(feature.Polylines[0].Points);
            feature.Rings[0].Points[0].Should().Be(feature.Rings[0].Points[^1]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseFile_keeps_a_hole_as_a_second_ring()
    {
        var directory = CreateDirectory();
        try
        {
            Write(
                Path.Combine(directory, "a.geojsonseq"),
                Feature(
                    """{"admin_level":2}""",
                    """{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]],[[2,2],[4,2],[4,4],[2,4],[2,2]]]}"""
                )
            );

            var parsed = GeoJsonSeqParser.ParseFile(Path.Combine(directory, "a.geojsonseq"), 0.002);

            parsed.Features[0].Polylines.Should().HaveCount(2);
            parsed.Features[0].Rings.Should().HaveCount(2);
            parsed
                .Features[0]
                .Rings[0]
                .Points.Should()
                .Equal(parsed.Features[0].Polylines[0].Points);
            parsed
                .Features[0]
                .Rings[1]
                .Points.Should()
                .Equal(parsed.Features[0].Polylines[1].Points);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseFile_flattens_multipolygon_rings_in_order()
    {
        var directory = CreateDirectory();
        try
        {
            Write(
                Path.Combine(directory, "a.geojsonseq"),
                Feature(
                    """{"admin_level":2}""",
                    """{"type":"MultiPolygon","coordinates":[[[[0,0],[1,0],[1,1],[0,0]]],[[[10,10],[11,10],[11,11],[10,10]],[[10,10],[10.5,10],[10.5,10.5],[10,10]]]]}"""
                )
            );

            var parsed = GeoJsonSeqParser.ParseFile(Path.Combine(directory, "a.geojsonseq"), 0.002);

            parsed.Features[0].Polylines.Should().HaveCount(3);
            parsed.Features[0].Rings.Should().HaveCount(3);
            parsed.Features[0].Rings[0].Points[0].Longitude.Should().Be(0);
            parsed.Features[0].Rings[1].Points[0].Longitude.Should().Be(10);
            parsed.Features[0].Rings[2].Points[0].Longitude.Should().Be(10);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseFile_leaves_line_features_without_rings()
    {
        var directory = CreateDirectory();
        try
        {
            Write(
                Path.Combine(directory, "a.geojsonseq"),
                Feature(
                    """{"admin_level":2}""",
                    """{"type":"MultiLineString","coordinates":[[[0,0],[1,1]],[[2,2],[3,3]]]}"""
                )
            );

            var parsed = GeoJsonSeqParser.ParseFile(Path.Combine(directory, "a.geojsonseq"), 0.002);

            parsed.Features[0].Polylines.Should().HaveCount(2);
            parsed.Features[0].Rings.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static string Feature(string properties, string geometry) =>
        $$"""{"type":"Feature","properties":{{properties}},"geometry":{{geometry}}}""";

    internal static void Write(string path, params string[] features) =>
        File.WriteAllText(
            path,
            string.Concat(features.Select(feature => "\u001e" + feature + "\n"))
        );

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"geotools-geojson-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
