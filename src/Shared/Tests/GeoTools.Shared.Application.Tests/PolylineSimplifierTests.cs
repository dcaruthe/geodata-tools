using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Application.Tests;

public sealed class PolylineSimplifierTests
{
    [Fact]
    public void Simplify_removes_collinear_points()
    {
        var points = new[]
        {
            new GeoPoint(0, 0),
            new GeoPoint(1, 1),
            new GeoPoint(2, 2),
            new GeoPoint(3, 3),
        };

        var simplified = PolylineSimplifier.Simplify(points, 0.001);

        simplified.Should().Equal(points[0], points[3]);
    }

    [Fact]
    public void Simplify_keeps_both_endpoints()
    {
        var points = new[]
        {
            new GeoPoint(0, 0),
            new GeoPoint(1, 2),
            new GeoPoint(2, 1),
            new GeoPoint(3, 4),
        };

        var simplified = PolylineSimplifier.Simplify(points, 100);

        simplified.Should().HaveCount(2);
        simplified[0].Should().Be(points[0]);
        simplified[^1].Should().Be(points[^1]);
    }

    [Fact]
    public void Simplify_recloses_a_ring()
    {
        var ring = new[]
        {
            new GeoPoint(0, 0),
            new GeoPoint(10, 0),
            new GeoPoint(10, 10),
            new GeoPoint(0, 10),
            new GeoPoint(0, 0),
        };

        var simplified = PolylineSimplifier.Simplify(ring, 0.001);

        simplified.First().Should().Be(simplified.Last());
        simplified.Should().HaveCountGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void Simplify_drops_a_ring_with_fewer_than_three_distinct_points()
    {
        var ring = new[]
        {
            new GeoPoint(0, 0),
            new GeoPoint(5, 0),
            new GeoPoint(10, 0),
            new GeoPoint(0, 0),
        };

        var simplified = PolylineSimplifier.Simplify(ring, 0.001);

        simplified.Should().BeEmpty();
    }

    [Fact]
    public void Simplify_returns_a_polyline_shorter_than_three_points_unchanged()
    {
        var points = new[] { new GeoPoint(0, 0), new GeoPoint(1, 1) };

        PolylineSimplifier.Simplify(points, 0.001).Should().Equal(points);
    }

    [Fact]
    public void Simplify_drops_a_ring_whose_points_are_all_identical()
    {
        var ring = new[] { new GeoPoint(1, 1), new GeoPoint(1, 1), new GeoPoint(1, 1) };

        PolylineSimplifier.Simplify(ring, 0.5).Should().BeEmpty();
    }

    [Fact]
    public void Simplify_drops_a_screen_ring_whose_points_are_all_identical()
    {
        var ring = new[] { new Point(1, 1), new Point(1, 1), new Point(1, 1) };

        PolylineSimplifier.Simplify(ring, 0.5f).Should().BeEmpty();
    }

    [Fact]
    public void Simplify_drops_a_screen_ring_with_only_two_distinct_points()
    {
        var ring = new[] { new Point(0, 0), new Point(10, 0), new Point(0, 0) };

        PolylineSimplifier.Simplify(ring, 0.5f).Should().BeEmpty();
    }
}
