using FluentAssertions;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Infrastructure.Geospatial;
using GeoTools.Shared.Infrastructure.State;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class SqliteRegionIndexTests
{
    [Fact]
    public async Task GetRegionAsync_round_trips_a_written_region()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        var polyline = Square(0, 0, 2);
        var bounds = new GeoBounds(0, 0, 1, 1);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [Region("region:42", polyline, bounds, level: 2, name: "Alpha")],
            [],
            CancellationToken.None
        );

        var detail = await store.GetRegionAsync("region:42");

        detail.Should().NotBeNull();
        detail!.Summary.Id.Should().Be("region:42");
        detail.Summary.Name.Should().Be("Alpha");
        detail.Summary.BorderType.Should().Be("nation");
        detail.Summary.AdminLevel.Should().Be(2);
        detail.Summary.Bounds.Should().Be(bounds);
        detail.Polylines.Should().HaveCount(1);
        detail.Polylines[0].Points.Should().HaveCount(5);
    }

    [Fact]
    public async Task ListLinesAsync_round_trips_written_lines()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        var polyline = new GeoPolyline(
            [new GeoPoint(0, 0), new GeoPoint(1, 1)],
            Closed: false,
            AdminLevel: 4
        );
        var bounds = new GeoBounds(0, 0, 1, 1);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [],
            [Line("region:line:0", polyline, bounds, 4)],
            CancellationToken.None
        );

        var lines = await store.ListLinesAsync([4], null);

        lines.Should().ContainSingle();
        lines[0].Id.Should().Be("region:line:0");
        lines[0].AdminLevel.Should().Be(4);
        lines[0].Bounds.Should().Be(bounds);
        lines[0].Polylines.Should().ContainSingle();
    }

    [Fact]
    public async Task ListRegionsAsync_filters_by_level_bounds_parent_and_search()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        var square = Square(0, 0, 2);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                Region("region:1", square, new GeoBounds(0, 0, 1, 1), level: 2, name: "Alpha"),
                Region("region:2", square, new GeoBounds(10, 10, 11, 11), level: 2, name: "Beta"),
                Region("region:3", square, new GeoBounds(0, 0, 1, 1), level: 4, name: "Gamma"),
            ],
            [],
            CancellationToken.None
        );

        var byLevel = await store.ListRegionsAsync([4], null, null, null, 100);
        byLevel.Select(region => region.Id).Should().Equal("region:3");

        var byBounds = await store.ListRegionsAsync(
            null,
            new GeoBounds(9, 9, 12, 12),
            null,
            null,
            100
        );
        byBounds.Select(region => region.Id).Should().Equal("region:2");

        var bySearch = await store.ListRegionsAsync(null, null, null, "alp", 100);
        bySearch.Select(region => region.Id).Should().Equal("region:1");

        await using var context = await database.Factory.CreateDbContextAsync();
        var parentRow = await context.Regions.FirstAsync(region => region.Id == "region:2");
        parentRow.ParentId = "region:1";
        await context.SaveChangesAsync();

        var byParent = await store.ListRegionsAsync(null, null, "region:1", null, 100);
        byParent.Select(region => region.Id).Should().Equal("region:2");
    }

    [Fact]
    public async Task ListRegionsAsync_orders_by_level_then_area_descending()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        var square = Square(0, 0, 2);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                Region("region:1", square, new GeoBounds(0, 0, 1, 1), level: 4, area: 5),
                Region("region:2", square, new GeoBounds(0, 0, 1, 1), level: 2, area: 1),
                Region("region:3", square, new GeoBounds(0, 0, 1, 1), level: 2, area: 9),
            ],
            [],
            CancellationToken.None
        );

        var regions = await store.ListRegionsAsync(null, null, null, null, 100);

        regions.Select(region => region.Id).Should().Equal("region:3", "region:2", "region:1");
    }

    [Fact]
    public async Task WriteSourceAsync_chunks_and_is_idempotent_for_the_same_source_key()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        var square = Square(0, 0, 2);
        var bounds = new GeoBounds(0, 0, 1, 1);
        var regions = Enumerable
            .Range(0, 2500)
            .Select(index => Region($"region:{index}", square, bounds, level: 2))
            .ToArray();
        var lines = Enumerable
            .Range(0, 1500)
            .Select(index =>
                Line(
                    $"region:line:{index}",
                    new GeoPolyline([new GeoPoint(0, 0), new GeoPoint(1, 1)], false, 4),
                    bounds,
                    4
                )
            )
            .ToArray();

        await store.WriteSourceAsync("africa", "key-a", regions, lines, CancellationToken.None);
        await store.WriteSourceAsync("africa", "key-a", regions, lines, CancellationToken.None);

        var stats = await store.GetStatsAsync();
        stats.RegionCount.Should().Be(2500);
        stats.LineCount.Should().Be(1500);
    }

    [Fact]
    public async Task WriteSourceAsync_replaces_rows_for_a_changed_source_key()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        var square = Square(0, 0, 2);
        var bounds = new GeoBounds(0, 0, 1, 1);

        await store.WriteSourceAsync(
            "africa",
            "old-key",
            [Region("region:1", square, bounds, level: 2)],
            [],
            CancellationToken.None
        );
        await store.WriteSourceAsync(
            "africa",
            "new-key",
            [Region("region:2", square, bounds, level: 2)],
            [],
            CancellationToken.None
        );

        await using var context = await database.Factory.CreateDbContextAsync();
        var ids = await context.Regions.Select(region => region.Id).ToListAsync();
        ids.Should().Equal("region:2");
        (await context.Regions.FirstAsync()).SourceKey.Should().Be("new-key");
    }

    [Fact]
    public async Task WriteSourceAsync_keeps_the_first_region_id_across_sources()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        var square = Square(0, 0, 2);
        var bounds = new GeoBounds(0, 0, 1, 1);

        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [Region("region:42", square, bounds, level: 2)],
            [],
            CancellationToken.None
        );
        await store.WriteSourceAsync(
            "asia",
            "key-b",
            [
                new RegionWrite(
                    "region:42",
                    "asia",
                    "42",
                    null,
                    null,
                    2,
                    bounds,
                    1,
                    5,
                    Encode(square, bounds)
                ),
            ],
            [],
            CancellationToken.None
        );

        var stats = await store.GetStatsAsync();
        stats.RegionCount.Should().Be(1);

        await using var context = await database.Factory.CreateDbContextAsync();
        var row = await context.Regions.SingleAsync();
        row.Region.Should().Be("africa");
        row.SourceKey.Should().Be("key-a");
    }

    [Fact]
    public async Task DeleteExceptSourcesAsync_removes_rows_from_other_sources()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        var square = Square(0, 0, 2);
        var bounds = new GeoBounds(0, 0, 1, 1);

        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [Region("region:1", square, bounds, level: 2)],
            [],
            CancellationToken.None
        );
        await store.WriteSourceAsync(
            "europe",
            "key-b",
            [Region("region:2", square, bounds, level: 2)],
            [],
            CancellationToken.None
        );

        await store.DeleteExceptSourcesAsync(["key-a"], CancellationToken.None);

        var stats = await store.GetStatsAsync();
        stats.RegionCount.Should().Be(1);
    }

    [Fact]
    public async Task GetStatsAsync_counts_levels_bounds_and_database_bytes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        var square = Square(0, 0, 2);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                Region("region:1", square, new GeoBounds(0, 0, 1, 1), level: 2),
                Region("region:2", square, new GeoBounds(10, 10, 11, 11), level: 4),
            ],
            [
                Line(
                    "region:line:0",
                    new GeoPolyline([new GeoPoint(0, 0), new GeoPoint(1, 1)], false, 4),
                    new GeoBounds(0, 0, 1, 1),
                    4
                ),
            ],
            CancellationToken.None
        );

        var stats = await store.GetStatsAsync();

        stats.RegionCount.Should().Be(2);
        stats.LineCount.Should().Be(1);
        stats.Levels.Should().Equal(2, 4);
        stats.Bounds.Should().Be(new GeoBounds(0, 0, 11, 11));
        stats.DatabaseBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetStatsAsync_reports_world_bounds_when_empty()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);

        var stats = await store.GetStatsAsync();

        stats.RegionCount.Should().Be(0);
        stats.LineCount.Should().Be(0);
        stats.Levels.Should().BeEmpty();
        stats.Bounds.Should().Be(GeoBounds.World);
    }

    [Fact]
    public async Task GetRegionAsync_skips_a_corrupt_blob()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                new RegionWrite(
                    "region:42",
                    "africa",
                    "42",
                    null,
                    null,
                    2,
                    new GeoBounds(0, 0, 1, 1),
                    1,
                    5,
                    [1, 2, 3]
                ),
            ],
            [],
            CancellationToken.None
        );

        var detail = await store.GetRegionAsync("region:42");

        detail.Should().BeNull();
    }

    [Fact]
    public async Task ListLinesAsync_requires_a_level_set()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);

        var act = async () => await store.ListLinesAsync([], null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task IsSourceIndexedAsync_reports_whether_a_source_has_rows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        var square = Square(0, 0, 2);

        (await store.IsSourceIndexedAsync("key-a")).Should().BeFalse();

        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [Region("region:1", square, new GeoBounds(0, 0, 1, 1), level: 2)],
            [],
            CancellationToken.None
        );

        (await store.IsSourceIndexedAsync("key-a")).Should().BeTrue();
        (await store.IsSourceIndexedAsync("key-b")).Should().BeFalse();
    }

    [Fact]
    public async Task ClearAsync_removes_every_row()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        var square = Square(0, 0, 2);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [Region("region:1", square, new GeoBounds(0, 0, 1, 1), level: 2)],
            [],
            CancellationToken.None
        );

        await store.ClearAsync();

        var stats = await store.GetStatsAsync();
        stats.RegionCount.Should().Be(0);
    }

    private static SqliteRegionIndex CreateStore(TestDatabase database) =>
        new(
            database.Factory,
            Options.Create(new StorageOptions { DatabasePath = database.Path }),
            TimeProvider.System,
            NullLogger<SqliteRegionIndex>.Instance
        );

    private static RegionWrite Region(
        string id,
        GeoPolyline polyline,
        GeoBounds bounds,
        int level,
        string? name = null,
        double area = 1
    ) =>
        new(
            id,
            "africa",
            id["region:".Length..],
            name,
            "nation",
            level,
            bounds,
            area,
            polyline.Points.Count,
            Encode(polyline, bounds)
        );

    private static BorderLineWrite Line(
        string id,
        GeoPolyline polyline,
        GeoBounds bounds,
        int level
    ) => new(id, "africa", level, bounds, polyline.Points.Count, Encode(polyline, bounds));

    private static byte[] Encode(GeoPolyline polyline, GeoBounds bounds) =>
        GeometryBlobCodec.Encode([new GeometryFeature(null, [polyline])], bounds);

    private static GeoPolyline Square(double minLon, double minLat, int level) =>
        new(
            [
                new GeoPoint(minLon, minLat),
                new GeoPoint(minLon + 1, minLat),
                new GeoPoint(minLon + 1, minLat + 1),
                new GeoPoint(minLon, minLat + 1),
                new GeoPoint(minLon, minLat),
            ],
            Closed: true,
            AdminLevel: level
        );
}
