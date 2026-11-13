using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Tests;

public sealed class RegionGeometryTests
{
    [Fact]
    public void Contains_returns_true_inside_a_shell()
    {
        var rings = new[] { Square(0, 0, 10, 10) };

        RegionGeometry.Contains(rings, new GeoPoint(5, 5)).Should().BeTrue();
    }

    [Fact]
    public void Contains_returns_false_inside_a_hole()
    {
        var rings = new[] { Square(0, 0, 10, 10), Square(4, 4, 6, 6) };

        RegionGeometry.Contains(rings, new GeoPoint(5, 5)).Should().BeFalse();
        RegionGeometry.Contains(rings, new GeoPoint(2, 2)).Should().BeTrue();
    }

    [Fact]
    public void Contains_returns_false_outside_the_shell()
    {
        var rings = new[] { Square(0, 0, 10, 10) };

        RegionGeometry.Contains(rings, new GeoPoint(20, 20)).Should().BeFalse();
    }

    [Fact]
    public void Contains_handles_a_multipolygon()
    {
        var rings = new[] { Square(0, 0, 1, 1), Square(5, 5, 6, 6) };

        RegionGeometry.Contains(rings, new GeoPoint(5.5, 5.5)).Should().BeTrue();
        RegionGeometry.Contains(rings, new GeoPoint(3, 3)).Should().BeFalse();
    }

    [Fact]
    public void Contains_ignores_open_polylines()
    {
        var open = new GeoPolyline(
            [new GeoPoint(0, 0), new GeoPoint(10, 0), new GeoPoint(10, 10), new GeoPoint(0, 10)],
            Closed: false,
            AdminLevel: 2
        );

        RegionGeometry.Contains([open], new GeoPoint(5, 5)).Should().BeFalse();
    }

    [Fact]
    public void Contains_rejects_a_point_in_a_date_line_region()
    {
        var dateLine = new GeoPolyline(
            [
                new GeoPoint(170, -10),
                new GeoPoint(170, 10),
                new GeoPoint(-170, 10),
                new GeoPoint(-170, -10),
                new GeoPoint(170, -10),
            ],
            Closed: true,
            AdminLevel: 2
        );

        RegionGeometry.Contains([dateLine], new GeoPoint(0, 0)).Should().BeFalse();
    }

    [Fact]
    public void RepresentativePoints_skips_the_duplicate_closing_point()
    {
        var square = Square(0, 0, 10, 10);

        var points = RegionGeometry.RepresentativePoints([square], count: 9);

        points.Should().HaveCount(4);
        points[0].Should().Be(new GeoPoint(0, 0));
        points[^1].Should().NotBe(points[0]);
    }

    [Fact]
    public void RepresentativePoints_samples_evenly_up_to_the_count()
    {
        var vertices = new List<GeoPoint>();
        for (var i = 0; i < 19; i++)
            vertices.Add(new GeoPoint(i, Math.Sin(i)));
        vertices.Add(vertices[0]);
        var ring = new GeoPolyline(vertices, Closed: true, AdminLevel: 2);

        var points = RegionGeometry.RepresentativePoints([ring], count: 9);

        points.Should().HaveCount(9);
        points[0].Should().Be(vertices[0]);
        points[^1].Should().Be(vertices[18]);
    }

    [Fact]
    public void RepresentativePoints_returns_empty_without_a_closed_polyline()
    {
        var open = new GeoPolyline([new GeoPoint(0, 0), new GeoPoint(1, 1)], false, 2);

        RegionGeometry.RepresentativePoints([open]).Should().BeEmpty();
    }

    [Fact]
    public void Area_orders_a_larger_ring_above_a_smaller_one()
    {
        var small = RegionGeometry.Area([Square(0, 0, 1, 1)]);
        var large = RegionGeometry.Area([Square(0, 0, 10, 10)]);

        large.Should().BeGreaterThan(small);
    }

    [Fact]
    public void Area_sums_every_ring()
    {
        var square = Square(0, 0, 1, 1);

        var doubled = RegionGeometry.Area([square, square]);

        doubled.Should().BeApproximately(2 * RegionGeometry.Area([square]), 1e-12);
    }

    [Fact]
    public void Rank_orders_non_territorial_then_deepest_then_smallest()
    {
        var territorial = Summary("territorial", level: 6, area: 0.5, territorial: true);
        var small = Summary("small", level: 4, area: 1);
        var deep = Summary("deep", level: 6, area: 5);
        var big = Summary("big", level: 4, area: 10);

        var ordered = RegionGeometry
            .Rank([(territorial, true), (small, false), (deep, false), (big, false)])
            .Select(summary => summary.Id)
            .ToArray();

        ordered.Should().Equal("deep", "small", "big", "territorial");
    }

    [Fact]
    public void Best_returns_the_top_match()
    {
        var territorial = Summary("territorial", level: 6, area: 0.5, territorial: true);
        var deep = Summary("deep", level: 6, area: 5);

        var best = RegionGeometry.Best([(territorial, true), (deep, false)]);

        best.Should().NotBeNull();
        best!.Id.Should().Be("deep");
    }

    [Fact]
    public void Best_returns_null_for_no_matches()
    {
        RegionGeometry.Best([]).Should().BeNull();
    }

    private static GeoPolyline Square(double minLon, double minLat, double maxLon, double maxLat) =>
        new(
            [
                new GeoPoint(minLon, minLat),
                new GeoPoint(maxLon, minLat),
                new GeoPoint(maxLon, maxLat),
                new GeoPoint(minLon, maxLat),
                new GeoPoint(minLon, minLat),
            ],
            Closed: true,
            AdminLevel: 2
        );

    private static RegionSummary Summary(
        string id,
        int level,
        double area,
        bool territorial = false
    ) =>
        new(
            id,
            null,
            territorial ? "territorial" : "nation",
            level,
            GeoBounds.World,
            null,
            area,
            0
        );
}
