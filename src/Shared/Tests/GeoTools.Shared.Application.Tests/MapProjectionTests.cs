using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Tests;

public sealed class MapProjectionTests
{
    [Fact]
    public void Project_maps_the_origin_to_the_center()
    {
        var (x, y) = MapProjection.Project(new GeoPoint(0, 0));

        x.Should().BeApproximately(0.5, 1e-9);
        y.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Project_maps_longitude_180_to_the_right_edge()
    {
        var (x, _) = MapProjection.Project(new GeoPoint(180, 0));

        x.Should().BeApproximately(1, 1e-9);
    }

    [Fact]
    public void Project_maps_latitude_45_to_its_mercator_position()
    {
        var (_, y) = MapProjection.Project(new GeoPoint(0, 45));

        y.Should().BeApproximately(0.3597, 0.001);
    }

    [Fact]
    public void Project_clamps_latitude_90_to_the_north_edge()
    {
        var (_, y) = MapProjection.Project(new GeoPoint(0, 90));

        y.Should().BeApproximately(0, 1e-6);
    }

    [Fact]
    public void Project_and_Unproject_round_trip()
    {
        GeoPoint[] points =
        [
            new GeoPoint(0, 0),
            new GeoPoint(12.34, 56.78),
            new GeoPoint(-179.9, -84.5),
            new GeoPoint(180, 85.05112878),
        ];

        foreach (var point in points)
        {
            var (x, y) = MapProjection.Project(point);
            var roundTrip = MapProjection.Unproject(x, y);

            roundTrip.Longitude.Should().BeApproximately(point.Longitude, 1e-9);
            roundTrip.Latitude.Should().BeApproximately(point.Latitude, 1e-9);
        }
    }

    [Fact]
    public void Unproject_returns_the_clamped_latitude_for_a_pole()
    {
        var (_, y) = MapProjection.Project(new GeoPoint(0, 90));

        var point = MapProjection.Unproject(0.5, y);

        point.Longitude.Should().BeApproximately(0, 1e-9);
        point.Latitude.Should().BeApproximately(85.05112878, 1e-6);
    }

    [Fact]
    public void Unproject_clamps_world_coordinates_to_the_world_square()
    {
        var low = MapProjection.Unproject(-1, 2);
        var high = MapProjection.Unproject(2, -1);

        low.Longitude.Should().BeApproximately(-180, 1e-9);
        low.Latitude.Should().BeApproximately(-85.05112878, 1e-6);
        high.Longitude.Should().BeApproximately(180, 1e-9);
        high.Latitude.Should().BeApproximately(85.05112878, 1e-6);
    }
}
