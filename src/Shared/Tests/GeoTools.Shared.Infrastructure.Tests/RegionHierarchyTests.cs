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

public sealed class RegionHierarchyTests
{
    [Fact]
    public async Task FindContainingAsync_orders_by_deepest_then_smallest()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                Region("region:country", [Square(0, 0, 10, 10, 2)], 2, area: 10000),
                Region("region:province", [Square(0, 0, 5, 5, 4)], 4, area: 2500),
                Region("region:city", [Square(0, 0, 1, 1, 4)], 4, area: 100),
            ],
            [],
            CancellationToken.None
        );

        var matches = await store.FindContainingAsync(new GeoPoint(0.5, 0.5), [2, 4]);

        matches
            .Select(region => region.Id)
            .Should()
            .Equal("region:city", "region:province", "region:country");
    }

    [Fact]
    public async Task FindContainingAsync_prefers_a_non_territorial_match()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                Region("region:land", [Square(0, 0, 10, 10, 4)], 4, area: 100),
                Region(
                    "region:sea",
                    [Square(0, 0, 10, 10, 4)],
                    4,
                    area: 1,
                    borderType: "territorial"
                ),
            ],
            [],
            CancellationToken.None
        );

        var matches = await store.FindContainingAsync(new GeoPoint(5, 5), [4]);

        matches.Select(region => region.Id).Should().Equal("region:land", "region:sea");
    }

    [Fact]
    public async Task FindContainingAsync_returns_a_territorial_region_when_it_is_the_only_match()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                Region(
                    "region:sea",
                    [Square(0, 0, 10, 10, 2)],
                    2,
                    area: 1,
                    borderType: "territorial"
                ),
            ],
            [],
            CancellationToken.None
        );

        var matches = await store.FindContainingAsync(new GeoPoint(5, 5), [2]);

        matches.Select(region => region.Id).Should().Equal("region:sea");
    }

    [Fact]
    public async Task FindContainingAsync_returns_empty_when_nothing_contains_the_point()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [Region("region:land", [Square(0, 0, 10, 10, 2)], 2)],
            [],
            CancellationToken.None
        );

        var matches = await store.FindContainingAsync(new GeoPoint(50, 50), [2]);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveChildrenAsync_finds_only_truly_contained_children()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        var shell = Square(0, 0, 10, 10, 2);
        var hole = Square(4, 4, 6, 6, 2);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                Region("region:parent", [shell, hole], 2),
                Region("region:inside", [Square(1, 1, 2, 2, 4)], 4),
                Region("region:in-hole", [Square(4.5, 4.5, 5.5, 5.5, 4)], 4),
                Region("region:outside", [Square(-20, -20, -19, -19, 4)], 4),
            ],
            [],
            CancellationToken.None
        );

        var children = await store.ResolveChildrenAsync("region:parent", 4);

        children.Select(region => region.Id).Should().Equal("region:inside");
    }

    [Fact]
    public async Task ResolveChildrenAsync_persists_parent_links_and_the_resolution_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                Region("region:parent", [Square(0, 0, 10, 10, 2)], 2),
                Region("region:child", [Square(1, 1, 2, 2, 4)], 4),
            ],
            [],
            CancellationToken.None
        );

        await store.ResolveChildrenAsync("region:parent", 4);

        await using var context = await database.Factory.CreateDbContextAsync();
        var child = await context.Regions.SingleAsync(region => region.Id == "region:child");
        child.ParentId.Should().Be("region:parent");
        var parent = await context.Regions.SingleAsync(region => region.Id == "region:parent");
        parent.ParentResolved.Should().BeTrue();
        (await store.IsResolvedAsync("region:parent", 4)).Should().BeTrue();
    }

    [Fact]
    public async Task ResolveChildrenAsync_second_call_short_circuits_on_the_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                Region("region:parent", [Square(0, 0, 10, 10, 2)], 2),
                Region("region:child", [Square(1, 1, 2, 2, 4)], 4),
            ],
            [],
            CancellationToken.None
        );

        var first = await store.ResolveChildrenAsync("region:parent", 4);
        first.Select(region => region.Id).Should().Equal("region:child");

        await using (var context = await database.Factory.CreateDbContextAsync())
        {
            var child = await context.Regions.SingleAsync(region => region.Id == "region:child");
            child.ParentId = null;
            await context.SaveChangesAsync();
        }

        var second = await store.ResolveChildrenAsync("region:parent", 4);

        second.Should().BeEmpty();

        await using var verify = await database.Factory.CreateDbContextAsync();
        (await verify.Regions.SingleAsync(region => region.Id == "region:child"))
            .ParentId.Should()
            .BeNull();
    }

    [Fact]
    public async Task ResolveChildrenAsync_missing_parent_resolves_to_empty()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);

        var children = await store.ResolveChildrenAsync("region:missing", 4);

        children.Should().BeEmpty();
        (await store.IsResolvedAsync("region:missing", 4)).Should().BeTrue();
    }

    [Fact]
    public async Task ResolveChildrenAsync_accepts_a_single_open_ring_child_by_centroid()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        var openChild = new GeoPolyline(
            [new GeoPoint(4, 4), new GeoPoint(6, 6)],
            Closed: false,
            AdminLevel: 4
        );
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                Region("region:parent", [Square(0, 0, 10, 10, 2)], 2),
                Region("region:open-child", [openChild], 4),
            ],
            [],
            CancellationToken.None
        );

        var children = await store.ResolveChildrenAsync("region:parent", 4);

        children.Select(region => region.Id).Should().Equal("region:open-child");
    }

    [Fact]
    public async Task ResolveChildrenAsync_corrupt_parent_resolves_to_empty()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                new RegionWrite(
                    "region:parent",
                    "africa",
                    "parent",
                    null,
                    "nation",
                    2,
                    new GeoBounds(0, 0, 10, 10),
                    1,
                    0,
                    [1, 2, 3]
                ),
            ],
            [],
            CancellationToken.None
        );

        var children = await store.ResolveChildrenAsync("region:parent", 4);

        children.Should().BeEmpty();
        (await store.IsResolvedAsync("region:parent", 4)).Should().BeTrue();
    }

    [Fact]
    public async Task ResolveChildrenAsync_leaves_a_child_with_an_existing_parent_alone()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                Region("region:first", [Square(0, 0, 10, 10, 2)], 2),
                Region("region:second", [Square(0, 0, 10, 10, 2)], 2),
                Region("region:child", [Square(1, 1, 2, 2, 4)], 4),
            ],
            [],
            CancellationToken.None
        );

        await store.ResolveChildrenAsync("region:first", 4);
        var secondChildren = await store.ResolveChildrenAsync("region:second", 4);

        secondChildren.Should().BeEmpty();

        await using var context = await database.Factory.CreateDbContextAsync();
        var child = await context.Regions.SingleAsync(region => region.Id == "region:child");
        child.ParentId.Should().Be("region:first");
    }

    [Fact]
    public async Task GetChildrenAsync_returns_children_ordered_by_area()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                Region("region:parent", [Square(0, 0, 10, 10, 2)], 2),
                Region("region:big", [Square(1, 1, 3, 3, 4)], 4, area: 100),
                Region("region:small", [Square(1, 1, 1.5, 1.5, 4)], 4, area: 5),
            ],
            [],
            CancellationToken.None
        );
        await store.ResolveChildrenAsync("region:parent", 4);

        var children = await store.GetChildrenAsync("region:parent", 4);

        children.Select(region => region.Id).Should().Equal("region:small", "region:big");
    }

    [Fact]
    public async Task WriteSourceAsync_clears_parent_links_and_resolutions()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                Region("africa", "region:parent", [Square(0, 0, 10, 10, 2)], 2),
                Region("africa", "region:child", [Square(1, 1, 2, 2, 4)], 4),
            ],
            [],
            CancellationToken.None
        );
        await store.ResolveChildrenAsync("region:parent", 4);

        await store.WriteSourceAsync(
            "europe",
            "key-b",
            [Region("europe", "region:other", [Square(0, 0, 1, 1, 2)], 2)],
            [],
            CancellationToken.None
        );

        await using var context = await database.Factory.CreateDbContextAsync();
        (await context.Regions.SingleAsync(region => region.Id == "region:child"))
            .ParentId.Should()
            .BeNull();
        (await context.Regions.SingleAsync(region => region.Id == "region:parent"))
            .ParentResolved.Should()
            .BeFalse();
        (await context.RegionResolutions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteExceptSourcesAsync_clears_parent_links_and_resolutions()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database);
        await store.WriteSourceAsync(
            "africa",
            "key-a",
            [
                Region("region:parent", [Square(0, 0, 10, 10, 2)], 2),
                Region("region:child", [Square(1, 1, 2, 2, 4)], 4),
            ],
            [],
            CancellationToken.None
        );
        await store.ResolveChildrenAsync("region:parent", 4);

        await store.DeleteExceptSourcesAsync(["key-a"], CancellationToken.None);

        await using var context = await database.Factory.CreateDbContextAsync();
        (await context.Regions.SingleAsync(region => region.Id == "region:child"))
            .ParentId.Should()
            .BeNull();
        (await context.RegionResolutions.CountAsync()).Should().Be(0);
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
        IReadOnlyList<GeoPolyline> polylines,
        int level,
        string? name = null,
        double area = 1,
        string? borderType = "nation"
    ) => Region("africa", id, polylines, level, name, area, borderType);

    private static RegionWrite Region(
        string region,
        string id,
        IReadOnlyList<GeoPolyline> polylines,
        int level,
        string? name = null,
        double area = 1,
        string? borderType = "nation"
    )
    {
        var bounds = GeoBounds.FromPolylines(polylines, GeoBounds.World);
        return new RegionWrite(
            id,
            region,
            id["region:".Length..],
            name,
            borderType,
            level,
            bounds,
            area,
            polylines.Sum(polyline => polyline.Points.Count),
            GeometryBlobCodec.Encode([new GeometryFeature(null, polylines)], bounds)
        );
    }

    private static GeoPolyline Square(
        double minLon,
        double minLat,
        double maxLon,
        double maxLat,
        int level
    ) =>
        new(
            [
                new GeoPoint(minLon, minLat),
                new GeoPoint(maxLon, minLat),
                new GeoPoint(maxLon, maxLat),
                new GeoPoint(minLon, maxLat),
                new GeoPoint(minLon, minLat),
            ],
            Closed: true,
            AdminLevel: level
        );
}
