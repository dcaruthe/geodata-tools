using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;

namespace GeoTools.Shared.Application.Tests;

public sealed class WorldRectTests
{
    [Fact]
    public void World_covers_the_normalized_square()
    {
        WorldRect.World.MinX.Should().Be(0);
        WorldRect.World.MinY.Should().Be(0);
        WorldRect.World.MaxX.Should().Be(1);
        WorldRect.World.MaxY.Should().Be(1);
    }

    [Fact]
    public void FromPoints_returns_the_bounding_box()
    {
        var bounds = WorldRect.FromPoints([
            new WorldPoint(0.2, 0.8),
            new WorldPoint(0.6, 0.1),
            new WorldPoint(0.4, 0.5),
        ]);

        bounds.MinX.Should().BeApproximately(0.2, 1e-6);
        bounds.MinY.Should().BeApproximately(0.1, 1e-6);
        bounds.MaxX.Should().BeApproximately(0.6, 1e-6);
        bounds.MaxY.Should().BeApproximately(0.8, 1e-6);
    }

    [Fact]
    public void FromPoints_returns_an_empty_rect_for_no_points()
    {
        WorldRect.FromPoints([]).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Intersects_detects_overlap()
    {
        var left = new WorldRect(0, 0, 0.5, 0.5);
        var right = new WorldRect(0.25, 0.25, 0.75, 0.75);

        left.Intersects(right).Should().BeTrue();
        right.Intersects(left).Should().BeTrue();
    }

    [Fact]
    public void Intersects_detects_a_touching_edge()
    {
        var left = new WorldRect(0, 0, 0.5, 0.5);
        var right = new WorldRect(0.5, 0.25, 0.75, 0.75);

        left.Intersects(right).Should().BeTrue();
    }

    [Fact]
    public void Intersects_is_false_for_separated_rects()
    {
        var left = new WorldRect(0, 0, 0.4, 0.4);
        var right = new WorldRect(0.5, 0.5, 0.9, 0.9);

        left.Intersects(right).Should().BeFalse();
    }

    [Fact]
    public void Intersects_is_false_for_an_empty_rect()
    {
        WorldRect.World.Intersects(WorldRect.Empty).Should().BeFalse();
        WorldRect.Empty.Intersects(WorldRect.World).Should().BeFalse();
    }

    [Fact]
    public void Inflate_grows_every_edge()
    {
        var inflated = new WorldRect(0.2, 0.3, 0.6, 0.7).Inflate(0.05);

        inflated.MinX.Should().BeApproximately(0.15, 1e-12);
        inflated.MinY.Should().BeApproximately(0.25, 1e-12);
        inflated.MaxX.Should().BeApproximately(0.65, 1e-12);
        inflated.MaxY.Should().BeApproximately(0.75, 1e-12);
    }

    [Fact]
    public void Inflate_keeps_an_empty_rect_empty()
    {
        WorldRect.Empty.Inflate(0.5).IsEmpty.Should().BeTrue();
    }
}
