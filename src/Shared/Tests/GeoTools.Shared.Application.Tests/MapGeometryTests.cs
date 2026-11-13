using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Tests;

public sealed class MapGeometryTests
{
    private static readonly MapViewport Viewport = MapViewport.Fit(GeoBounds.World, 1000, 500, 0);

    [Fact]
    public void Project_filters_polylines_by_admin_level()
    {
        var polylines = GeometryProjector.Project([Ring(2, 0, 0, 10, 10), Ring(4, 20, 20, 30, 30)]);

        var screen = MapGeometry.Project(
            polylines,
            new HashSet<int> { 2 },
            Viewport,
            WorldRect.World
        );

        screen.Should().ContainSingle();
    }

    [Fact]
    public void Project_keeps_a_ring_closed_when_it_does_not_cross_the_antimeridian()
    {
        var polylines = GeometryProjector.Project([Ring(2, 0, 0, 10, 10)]);

        var screen = MapGeometry.Project(
            polylines,
            new HashSet<int> { 2 },
            Viewport,
            WorldRect.World
        );

        screen.Should().ContainSingle();
        screen[0].Closed.Should().BeTrue();
        screen[0].Points[0].Should().Be(screen[0].Points[^1]);
    }

    [Fact]
    public void Project_preserves_the_admin_level_on_each_polyline()
    {
        var polylines = GeometryProjector.Project([Ring(2, 0, 0, 10, 10), Ring(4, 20, 20, 30, 30)]);

        var screen = MapGeometry.Project(
            polylines,
            new HashSet<int> { 2, 4 },
            Viewport,
            WorldRect.World
        );

        screen.Should().HaveCount(2);
        screen.Select(polyline => polyline.AdminLevel).Should().BeEquivalentTo([2, 4]);
    }

    [Fact]
    public void Project_keeps_the_antimeridian_split_segments_open()
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
        var polylines = GeometryProjector.Project([ring]);

        var screen = MapGeometry.Project(
            polylines,
            new HashSet<int> { 2 },
            Viewport,
            WorldRect.World
        );

        screen.Should().HaveCount(2);
        screen.Should().OnlyContain(polyline => !polyline.Closed);
    }

    [Fact]
    public void Project_skips_a_polyline_with_no_points()
    {
        var empty = new GeoPolyline([], Closed: false, AdminLevel: 2);

        var polylines = GeometryProjector.Project([empty]);

        MapGeometry
            .Project(polylines, new HashSet<int> { 2 }, Viewport, WorldRect.World)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Project_skips_a_ring_that_projects_to_a_single_point()
    {
        var degenerate = new GeoPolyline(
            [new GeoPoint(0, 0), new GeoPoint(0, 0), new GeoPoint(0, 0)],
            Closed: true,
            AdminLevel: 2
        );
        var polylines = GeometryProjector.Project([degenerate]);

        MapGeometry
            .Project(polylines, new HashSet<int> { 2 }, Viewport, WorldRect.World)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Project_culls_polylines_outside_the_visible_world_rect()
    {
        var polylines = GeometryProjector.Project([Ring(2, 0, 0, 10, 10)]);
        var (minX, minY) = MapProjection.Project(new GeoPoint(20, 20));
        var (maxX, maxY) = MapProjection.Project(new GeoPoint(30, 30));
        var visible = new WorldRect(
            Math.Min(minX, maxX),
            Math.Min(minY, maxY),
            Math.Max(minX, maxX),
            Math.Max(minY, maxY)
        );

        var screen = MapGeometry.Project(polylines, new HashSet<int> { 2 }, Viewport, visible);

        screen.Should().BeEmpty();
    }

    [Fact]
    public void Project_keeps_polylines_that_touch_the_visible_world_rect()
    {
        var polylines = GeometryProjector.Project([Ring(2, 0, 0, 10, 10)]);
        var (minX, minY) = MapProjection.Project(new GeoPoint(10, 0));
        var (maxX, maxY) = MapProjection.Project(new GeoPoint(20, 20));
        var visible = new WorldRect(
            Math.Min(minX, maxX),
            Math.Min(minY, maxY),
            Math.Max(minX, maxX),
            Math.Max(minY, maxY)
        );

        var screen = MapGeometry.Project(polylines, new HashSet<int> { 2 }, Viewport, visible);

        screen.Should().ContainSingle();
    }

    [Fact]
    public void CountVisible_applies_the_level_and_cull_filters()
    {
        var polylines = GeometryProjector.Project([Ring(2, 0, 0, 10, 10), Ring(4, 20, 20, 30, 30)]);

        var visible = MapGeometry.CountVisible(polylines, new HashSet<int> { 2 }, WorldRect.World);

        visible.Should().Be(1);
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
