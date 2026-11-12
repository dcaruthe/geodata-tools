#pragma warning disable CA1416 // The unreadable-file proof uses Unix modes on Linux.
using FluentAssertions;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Infrastructure.Geospatial;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class BorderCacheKeysTests
{
    [Fact]
    public void The_key_depends_on_the_file_tolerance_and_levels()
    {
        var directory = Directory.CreateTempSubdirectory("geotools-keys-");
        try
        {
            var file = Path.Combine(directory.FullName, "africa-boundaries.geojsonseq");
            File.WriteAllText(file, "abc");

            var key = BorderCacheKeys.Compute(file, 0.002, [2, 4]);

            BorderCacheKeys.Compute(file, 0.002, [2, 4]).Should().Be(key);
            BorderCacheKeys.Compute(file, 0.005, [2, 4]).Should().NotBe(key);
            BorderCacheKeys.Compute(file, 0.002, [2]).Should().NotBe(key);

            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(5));
            BorderCacheKeys.Compute(file, 0.002, [2, 4]).Should().NotBe(key);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_fingerprint_depends_on_every_key()
    {
        BorderCacheKeys
            .Fingerprint(["a", "b"])
            .Should()
            .Be(BorderCacheKeys.Fingerprint(["a", "b"]));
        BorderCacheKeys
            .Fingerprint(["a", "b"])
            .Should()
            .NotBe(BorderCacheKeys.Fingerprint(["b", "a"]));
    }
}

public sealed class CachedBorderGeometryLoaderTests
{
    [Fact]
    public async Task A_missing_directory_returns_empty_geometry()
    {
        await using var database = await TestDatabase.CreateAsync();
        var root = Path.Combine(Path.GetTempPath(), $"geotools-missing-{Guid.NewGuid():N}");
        var loader = CreateLoader(database, new TestSettings(), root);

        var result = await loader.LoadAsync();

        result.Geometry.Polylines.Should().BeEmpty();
        result.Fingerprint.Should().BeEmpty();
        result.AvailableLevels.Should().BeEmpty();
    }

    [Fact]
    public async Task An_empty_directory_returns_empty_geometry()
    {
        await using var database = await TestDatabase.CreateAsync();
        var root = Directory.CreateTempSubdirectory("geotools-empty-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "borders"));
            var loader = CreateLoader(database, new TestSettings(), root.FullName);

            var result = await loader.LoadAsync();

            result.Geometry.Polylines.Should().BeEmpty();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task The_first_load_parses_and_caches_and_the_next_load_hits_the_cache()
    {
        await using var database = await TestDatabase.CreateAsync();
        var root = Directory.CreateTempSubdirectory("geotools-loader-");
        try
        {
            var borders = Directory
                .CreateDirectory(Path.Combine(root.FullName, "borders"))
                .FullName;
            GeoJsonSeqParserTests.Write(
                Path.Combine(borders, "a-boundaries.geojsonseq"),
                GeoJsonSeqParserTests.Feature(
                    """{"@id":"relation/1","admin_level":2}""",
                    """{"type":"Polygon","coordinates":[[[10,10],[20,10],[20,20],[10,10]]]}"""
                )
            );
            GeoJsonSeqParserTests.Write(
                Path.Combine(borders, "b-boundaries.geojsonseq"),
                GeoJsonSeqParserTests.Feature(
                    """{"@id":"relation/1","admin_level":2}""",
                    """{"type":"Polygon","coordinates":[[[10,10],[20,10],[20,20],[10,10]]]}"""
                ),
                GeoJsonSeqParserTests.Feature(
                    """{"admin_level":4}""",
                    """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                )
            );

            var loader = CreateLoader(database, new TestSettings(), root.FullName);

            var first = await loader.LoadAsync();
            var second = await loader.LoadAsync();

            first.ParsedFiles.Should().Be(2);
            first.CacheHits.Should().Be(0);
            first.Geometry.Polylines.Should().HaveCount(2);
            first.AvailableLevels.Should().Equal(2, 4);
            second.ParsedFiles.Should().Be(0);
            second.CacheHits.Should().Be(2);
            second.Geometry.Polylines.Should().HaveCount(2);
            second.Fingerprint.Should().Be(first.Fingerprint);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_corrupt_cache_entry_falls_back_to_parsing()
    {
        await using var database = await TestDatabase.CreateAsync();
        var root = Directory.CreateTempSubdirectory("geotools-corrupt-");
        try
        {
            var borders = Directory
                .CreateDirectory(Path.Combine(root.FullName, "borders"))
                .FullName;
            var file = Path.Combine(borders, "a-boundaries.geojsonseq");
            GeoJsonSeqParserTests.Write(
                file,
                GeoJsonSeqParserTests.Feature(
                    """{"admin_level":2}""",
                    """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                )
            );

            var loader = CreateLoader(database, new TestSettings(), root.FullName);
            await loader.LoadAsync();

            var key = BorderCacheKeys.Compute(file, 0.002, [2, 4]);
            var cache = new GeoTools.Shared.Infrastructure.State.GeometryCacheStore(
                database.Factory
            );
            var entry = await cache.TryGetAsync(key);
            await cache.PutAsync(entry! with { Blob = [1, 2, 3] });

            var second = await loader.LoadAsync();

            second.ParsedFiles.Should().Be(1);
            second.CacheHits.Should().Be(0);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_different_tolerance_misses_the_cache()
    {
        await using var database = await TestDatabase.CreateAsync();
        var root = Directory.CreateTempSubdirectory("geotools-tolerance-");
        try
        {
            var borders = Directory
                .CreateDirectory(Path.Combine(root.FullName, "borders"))
                .FullName;
            GeoJsonSeqParserTests.Write(
                Path.Combine(borders, "a-boundaries.geojsonseq"),
                GeoJsonSeqParserTests.Feature(
                    """{"admin_level":2}""",
                    """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                )
            );

            var settings = new TestSettings();
            var loader = CreateLoader(database, settings, root.FullName);
            await loader.LoadAsync();

            settings.Geodata = settings.Geodata with { SimplifyToleranceDegrees = 0.01 };
            var second = await loader.LoadAsync();

            second.ParsedFiles.Should().Be(1);
            second.CacheHits.Should().Be(0);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task An_unreadable_file_is_reported_and_not_cached()
    {
        await using var database = await TestDatabase.CreateAsync();
        var root = Directory.CreateTempSubdirectory("geotools-unreadable-");
        try
        {
            var borders = Directory
                .CreateDirectory(Path.Combine(root.FullName, "borders"))
                .FullName;
            var file = Path.Combine(borders, "a-boundaries.geojsonseq");
            GeoJsonSeqParserTests.Write(
                file,
                GeoJsonSeqParserTests.Feature(
                    """{"admin_level":2}""",
                    """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                )
            );
            File.SetUnixFileMode(file, UnixFileMode.None);

            var loader = CreateLoader(database, new TestSettings(), root.FullName);
            var result = await loader.LoadAsync();

            result.ParsedFiles.Should().Be(1);
            result.Geometry.Polylines.Should().BeEmpty();
            var cache = new GeoTools.Shared.Infrastructure.State.GeometryCacheStore(
                database.Factory
            );
            (await cache.TryGetAsync(BorderCacheKeys.Compute(file, 0.002, [2, 4])))
                .Should()
                .BeNull();
        }
        finally
        {
            foreach (
                var file in Directory.GetFiles(root.FullName, "*", SearchOption.AllDirectories)
            )
            {
                try
                {
                    File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch (UnauthorizedAccessException) { }
            }

            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task The_loader_reports_progress_per_file()
    {
        await using var database = await TestDatabase.CreateAsync();
        var root = Directory.CreateTempSubdirectory("geotools-progress-");
        try
        {
            var borders = Directory
                .CreateDirectory(Path.Combine(root.FullName, "borders"))
                .FullName;
            GeoJsonSeqParserTests.Write(
                Path.Combine(borders, "a-boundaries.geojsonseq"),
                GeoJsonSeqParserTests.Feature(
                    """{"admin_level":2}""",
                    """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""
                )
            );
            GeoJsonSeqParserTests.Write(
                Path.Combine(borders, "b-boundaries.geojsonseq"),
                GeoJsonSeqParserTests.Feature(
                    """{"admin_level":2}""",
                    """{"type":"LineString","coordinates":[[2,2],[3,3]]}"""
                )
            );
            var values = new List<double>();
            var loader = CreateLoader(database, new TestSettings(), root.FullName);

            await loader.LoadAsync(new Collector<double>(values.Add));

            values.Should().HaveCount(2);
            values[^1].Should().Be(1);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private sealed class Collector<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static CachedBorderGeometryLoader CreateLoader(
        TestDatabase database,
        TestSettings settings,
        string root
    ) =>
        new(
            new GeoTools.Shared.Infrastructure.State.GeometryCacheStore(database.Factory),
            settings,
            Options.Create(
                new GeodataOptions
                {
                    RootPath = root,
                    BordersSubdirectory = "borders",
                    Regions = ["a", "b"],
                }
            ),
            TimeProvider.System,
            NullLogger<CachedBorderGeometryLoader>.Instance
        );
}
#pragma warning restore CA1416
