using FluentAssertions;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Domain.Tests;

public sealed class GeoBoundsTests
{
    [Fact]
    public void FromPolylines_uses_the_extent_of_every_point()
    {
        var bounds = GeoBounds.FromPolylines(
            [
                new GeoPolyline([new GeoPoint(-10, -5), new GeoPoint(20, 15)], false, 2),
                new GeoPolyline([], false, 2),
            ],
            GeoBounds.World
        );

        bounds.MinLongitude.Should().Be(-10);
        bounds.MinLatitude.Should().Be(-5);
        bounds.MaxLongitude.Should().Be(20);
        bounds.MaxLatitude.Should().Be(15);
    }

    [Fact]
    public void FromPolylines_falls_back_when_there_are_no_points()
    {
        var bounds = GeoBounds.FromPolylines([], GeoBounds.World);

        bounds.Should().Be(GeoBounds.World);
    }
}
