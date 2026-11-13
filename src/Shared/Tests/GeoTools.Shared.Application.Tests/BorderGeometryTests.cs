using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Tests;

public sealed class BorderGeometryTests
{
    [Fact]
    public void FromGeo_builds_world_polylines_and_keeps_the_geo_polylines()
    {
        var polylines = new[]
        {
            new GeoPolyline([new GeoPoint(0, 0), new GeoPoint(10, 10)], false, 2),
        };
        var bounds = new GeoBounds(0, 0, 10, 10);

        var geometry = BorderGeometry.FromGeo(polylines, bounds);

        geometry.Polylines.Should().BeSameAs(polylines);
        geometry.WorldPolylines.Should().ContainSingle();
        geometry.Bounds.Should().Be(bounds);
    }

    [Fact]
    public void Empty_has_no_geo_or_world_polylines()
    {
        BorderGeometry.Empty.Polylines.Should().BeEmpty();
        BorderGeometry.Empty.WorldPolylines.Should().BeEmpty();
        BorderGeometry.Empty.Bounds.Should().Be(GeoBounds.World);
    }
}
