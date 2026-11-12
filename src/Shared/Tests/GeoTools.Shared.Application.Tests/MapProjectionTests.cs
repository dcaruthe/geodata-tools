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
}
