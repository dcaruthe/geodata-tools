using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Infrastructure.Geospatial;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class GeoJsonSeqBorderSourceTests
{
    [Fact]
    public void Load_parses_polygons_and_lines_and_keeps_every_admin_level()
    {
        var directory = CreateDirectory();
        try
        {
            WriteRecords(
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
            WriteRecords(
                Path.Combine(directory, "b.geojsonseq"),
                Feature(
                    """{"@id":"relation/1","admin_level":2}""",
                    """{"type":"Polygon","coordinates":[[[10,10],[20,10],[20,20],[10,10]]]}"""
                ),
                Feature(
                    """{"admin_level":"4"}""",
                    """{"type":"LineString","coordinates":[[30,30],[40,40]]}"""
                ),
                Feature(
                    """{"admin_level":2}""",
                    """{"type":"Polygon","coordinates":[[[200,0],[210,0],[210,10],[200,0]]]}"""
                )
            );

            var geometry = CreateSource(directory).Load();

            geometry.Polylines.Should().HaveCount(3);
            geometry
                .Polylines.Select(polyline => polyline.AdminLevel)
                .Should()
                .BeEquivalentTo(new[] { 2, 4, 4 });

            var polygon = geometry.Polylines.Single(polyline => polyline.AdminLevel == 2);
            polygon.Closed.Should().BeTrue();
            polygon.Points[0].Should().Be(polygon.Points[^1]);

            geometry
                .Polylines.Where(polyline => polyline.AdminLevel == 4)
                .Should()
                .OnlyContain(polyline => !polyline.Closed);

            geometry.Bounds.MinLongitude.Should().BeApproximately(-50, 1e-9);
            geometry.Bounds.MaxLongitude.Should().BeApproximately(40, 1e-9);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_returns_empty_geometry_when_the_directory_is_missing()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"geodata-missing-{Guid.NewGuid():N}");

        var geometry = CreateSource(directory).Load();

        geometry.Should().BeSameAs(BorderGeometry.Empty);
    }

    [Fact]
    public void Load_returns_empty_geometry_when_the_directory_has_no_border_files()
    {
        var directory = CreateDirectory();
        try
        {
            CreateSource(directory).Load().Should().BeSameAs(BorderGeometry.Empty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_reads_multipolygons_and_multilinestrings()
    {
        var directory = CreateDirectory();
        try
        {
            WriteRecords(
                Path.Combine(directory, "a.geojsonseq"),
                Feature(
                    """{"admin_level":2}""",
                    """{"type":"MultiPolygon","coordinates":[[[[0,0],[10,0],[10,10],[0,0]]],[[[20,20],[30,20],[30,30],[20,20]]]]}"""
                ),
                Feature(
                    """{"admin_level":4}""",
                    """{"type":"MultiLineString","coordinates":[[[0,0],[1,1]],[[2,2],[3,3],[4,4]]]}"""
                )
            );

            var geometry = CreateSource(directory).Load();

            geometry.Polylines.Should().HaveCount(4);
            geometry
                .Polylines.Where(polyline => polyline.Closed)
                .Should()
                .HaveCount(2)
                .And.OnlyContain(polyline => polyline.AdminLevel == 2);
            geometry
                .Polylines.Where(polyline => !polyline.Closed)
                .Should()
                .HaveCount(2)
                .And.OnlyContain(polyline => polyline.AdminLevel == 4);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_appends_the_first_point_to_an_unclosed_polygon_ring()
    {
        var directory = CreateDirectory();
        try
        {
            WriteRecords(
                Path.Combine(directory, "a.geojsonseq"),
                Feature(
                    """{"admin_level":2}""",
                    """{"type":"Polygon","coordinates":[[[10,10],[20,10],[20,20]]]}"""
                )
            );

            var polyline = CreateSource(directory).Load().Polylines.Should().ContainSingle().Which;

            polyline.Closed.Should().BeTrue();
            polyline.Points.Should().HaveCount(4);
            polyline.Points[^1].Should().Be(polyline.Points[0]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_drops_a_ring_that_simplifies_to_a_line()
    {
        var directory = CreateDirectory();
        try
        {
            WriteRecords(
                Path.Combine(directory, "a.geojsonseq"),
                Feature(
                    """{"admin_level":2}""",
                    """{"type":"Polygon","coordinates":[[[0,0],[5,0],[10,0],[0,0]]]}"""
                )
            );

            CreateSource(directory).Load().Polylines.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_skips_blank_lines_malformed_roots_and_unusable_geometry()
    {
        var directory = CreateDirectory();
        try
        {
            WriteRaw(
                Path.Combine(directory, "skipped.geojsonseq"),
                "",
                "[]",
                Feature("""{"admin_level":2}""", """{"coordinates":[[0,0],[1,1]]}"""),
                Feature("""{"admin_level":2}""", """{"type":"Point","coordinates":[0,0]}"""),
                Feature(
                    """{"admin_level":2}""",
                    """{"type":"Polygon","coordinates":[[[0,0],[1,0],[2,0],[0,0]],5]}"""
                ),
                Feature("""{"admin_level":2}""", """{"type":"Polygon"}"""),
                Feature("""{"admin_level":2}""", """{"type":"Polygon","coordinates":5}"""),
                Feature("""{"admin_level":2}""", """{"type":"Polygon","coordinates":[]}"""),
                Feature("""{"admin_level":2}""", """{"type":"Polygon","coordinates":[[]]}"""),
                Feature("""{"admin_level":2}""", """{"type":"Polygon","coordinates":[[[1]]]}"""),
                Feature(
                    """{"admin_level":2}""",
                    """{"type":"Polygon","coordinates":[[["a","b"]]]}"""
                ),
                Feature(
                    """{"admin_level":2}""",
                    """{"type":"Polygon","coordinates":[[[200,0],[210,0],[200,1]]]}"""
                ),
                Feature(
                    """{"admin_level":true}""",
                    """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                ),
                Feature(
                    """{"admin_level":0}""",
                    """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                ),
                Feature(
                    """{"admin_level":"abc"}""",
                    """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                ),
                """{"type":"Feature","properties":5,"geometry":{"type":"LineString","coordinates":[[0,0],[1,1]]}}"""
            );

            CreateSource(directory).Load().Polylines.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_warns_and_continues_when_a_border_file_cannot_be_read()
    {
        var directory = CreateDirectory();
        try
        {
            File.CreateSymbolicLink(
                Path.Combine(directory, "broken.geojsonseq"),
                Path.Combine(directory, "missing-target")
            );

            CreateSource(directory).Load().Polylines.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_reads_a_bom_prefixed_record_with_a_record_separator()
    {
        var directory = CreateDirectory();
        try
        {
            WriteRaw(
                Path.Combine(directory, "bom.geojsonseq"),
                "\uFEFF\u001e"
                    + Feature(
                        """{"admin_level":" 2 "}""",
                        """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                    )
            );

            var geometry = CreateSource(directory).Load();

            geometry.Polylines.Should().ContainSingle();
            geometry.Polylines[0].AdminLevel.Should().Be(2);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_caches_the_first_read()
    {
        var directory = CreateDirectory();
        try
        {
            var file = Path.Combine(directory, "a.geojsonseq");
            WriteRecords(
                file,
                Feature(
                    """{"admin_level":2}""",
                    """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                )
            );
            var source = CreateSource(directory);

            var first = source.Load();
            File.Delete(file);
            var second = source.Load();

            second.Should().BeSameAs(first);
            second.Polylines.Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_resolves_a_relative_borders_directory_from_the_repository_root()
    {
        var geometry = CreateSource($"missing-{Guid.NewGuid():N}").Load();

        geometry.Should().BeSameAs(BorderGeometry.Empty);
    }

    private static GeoJsonSeqBorderSource CreateSource(string directory) =>
        new(
            new TestOptionsMonitor<MapOptions>(
                new MapOptions
                {
                    BordersDirectory = directory,
                    AdminLevels = [2],
                    SimplifyToleranceDegrees = 0.002,
                }
            ),
            NullLogger<GeoJsonSeqBorderSource>.Instance
        );

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"geodata-borders-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteRecords(string path, params string[] features) =>
        File.WriteAllLines(path, features.Select(feature => $"\u001e{feature}"));

    private static void WriteRaw(string path, params string[] lines) =>
        File.WriteAllLines(path, lines);

    private static string Feature(string properties, string geometry) =>
        $$"""{"type":"Feature","properties":{{properties}},"geometry":{{geometry}}}""";

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
