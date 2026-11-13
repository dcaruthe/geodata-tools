using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Tests;

public sealed class GeometryProjectorTests
{
    [Fact]
    public void Project_keeps_a_ring_closed_when_it_does_not_cross_the_antimeridian()
    {
        var world = GeometryProjector.Project([Ring(2, 0, 0, 10, 10)]);

        world.Should().ContainSingle();
        world[0].Closed.Should().BeTrue();
        world[0].AdminLevel.Should().Be(2);
        world[0].Points.Should().HaveCount(5);
        world[0].Points[0].Should().Be(world[0].Points[^1]);
    }

    [Fact]
    public void Project_computes_bounds_that_contain_every_point()
    {
        var world = GeometryProjector.Project([Ring(2, 0, 0, 10, 10)]);
        var expectedMin = MapProjection.Project(new GeoPoint(0, 0));
        var expectedMax = MapProjection.Project(new GeoPoint(10, 10));
        var bounds = world[0].Bounds;

        bounds.MinX.Should().BeApproximately(Math.Min(expectedMin.X, expectedMax.X), 1e-6);
        bounds.MinY.Should().BeApproximately(Math.Min(expectedMin.Y, expectedMax.Y), 1e-6);
        bounds.MaxX.Should().BeApproximately(Math.Max(expectedMin.X, expectedMax.X), 1e-6);
        bounds.MaxY.Should().BeApproximately(Math.Max(expectedMin.Y, expectedMax.Y), 1e-6);

        foreach (var point in world[0].Points)
        {
            point.X.Should().BeInRange(bounds.MinX, bounds.MaxX);
            point.Y.Should().BeInRange(bounds.MinY, bounds.MaxY);
        }
    }

    [Fact]
    public void Project_splits_a_ring_that_crosses_the_antimeridian_into_open_segments()
    {
        var ring = new GeoPolyline(
            [
                new GeoPoint(-170, 0),
                new GeoPoint(170, 0),
                new GeoPoint(170, 10),
                new GeoPoint(-170, 10),
                new GeoPoint(-170, 0),
            ],
            Closed: true,
            AdminLevel: 2
        );

        var world = GeometryProjector.Project([ring]);

        world.Should().HaveCount(2);
        world.Should().OnlyContain(polyline => !polyline.Closed);
        world.Should().OnlyContain(polyline => polyline.AdminLevel == 2);
    }

    [Fact]
    public void Project_splits_an_open_polyline_that_crosses_the_antimeridian()
    {
        var line = new GeoPolyline(
            [
                new GeoPoint(160, 0),
                new GeoPoint(170, 0),
                new GeoPoint(-170, 0),
                new GeoPoint(-160, 0),
            ],
            Closed: false,
            AdminLevel: 4
        );

        var world = GeometryProjector.Project([line]);

        world.Should().HaveCount(2);
        world.Should().OnlyContain(polyline => !polyline.Closed);
        world.Should().OnlyContain(polyline => polyline.AdminLevel == 4);
    }

    [Fact]
    public void Project_drops_the_single_point_before_a_leading_antimeridian_jump()
    {
        var line = new GeoPolyline(
            [new GeoPoint(170, 0), new GeoPoint(-170, 0), new GeoPoint(-160, 0)],
            Closed: false,
            AdminLevel: 2
        );

        var world = GeometryProjector.Project([line]);

        world.Should().ContainSingle();
        world[0].Points.Should().HaveCount(2);
    }

    [Fact]
    public void Project_splits_a_ring_that_closes_across_the_antimeridian()
    {
        var ring = new GeoPolyline(
            [
                new GeoPoint(-170, 0),
                new GeoPoint(-175, 0),
                new GeoPoint(-175, 5),
                new GeoPoint(-170, 5),
                new GeoPoint(170, 0),
            ],
            Closed: true,
            AdminLevel: 2
        );

        var world = GeometryProjector.Project([ring]);

        world.Should().ContainSingle();
        world[0].Closed.Should().BeFalse();
        world[0].Points.Should().HaveCount(4);
    }

    [Fact]
    public void Project_keeps_a_degenerate_ring_as_one_world_polyline()
    {
        var degenerate = new GeoPolyline(
            [new GeoPoint(0, 0), new GeoPoint(0, 0), new GeoPoint(0, 0)],
            Closed: true,
            AdminLevel: 2
        );

        var world = GeometryProjector.Project([degenerate]);

        world.Should().ContainSingle();
        world[0].Points.Should().HaveCount(3);
    }

    [Fact]
    public void Project_skips_a_polyline_with_no_points()
    {
        GeometryProjector
            .Project([new GeoPolyline([], Closed: false, AdminLevel: 2)])
            .Should()
            .BeEmpty();
    }

    private static GeoPolyline Ring(
        int adminLevel,
        double minLongitude,
        double minLatitude,
        double maxLongitude,
        double maxLatitude
    ) =>
        new(
            [
                new GeoPoint(minLongitude, minLatitude),
                new GeoPoint(maxLongitude, minLatitude),
                new GeoPoint(maxLongitude, maxLatitude),
                new GeoPoint(minLongitude, maxLatitude),
                new GeoPoint(minLongitude, minLatitude),
            ],
            Closed: true,
            AdminLevel: adminLevel
        );
}
