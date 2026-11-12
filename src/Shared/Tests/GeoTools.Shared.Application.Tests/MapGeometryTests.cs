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
        var polylines = new[] { Ring(2, 0, 0, 10, 10), Ring(4, 20, 20, 30, 30) };

        var screen = MapGeometry.Project(polylines, new HashSet<int> { 2 }, Viewport);

        screen.Should().ContainSingle();
    }

    [Fact]
    public void Project_keeps_a_ring_closed_when_it_does_not_cross_the_antimeridian()
    {
        var screen = MapGeometry.Project([Ring(2, 0, 0, 10, 10)], new HashSet<int> { 2 }, Viewport);

        screen.Should().ContainSingle();
        screen[0].Closed.Should().BeTrue();
        screen[0].Points[0].Should().Be(screen[0].Points[^1]);
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

        var screen = MapGeometry.Project([ring], new HashSet<int> { 2 }, Viewport);

        screen.Should().HaveCount(2);
        screen.Should().OnlyContain(polyline => !polyline.Closed);
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
