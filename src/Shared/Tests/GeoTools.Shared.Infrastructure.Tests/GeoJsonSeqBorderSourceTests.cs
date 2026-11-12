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

    private static string Feature(string properties, string geometry) =>
        $$"""{"type":"Feature","properties":{{properties}},"geometry":{{geometry}}}""";

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
