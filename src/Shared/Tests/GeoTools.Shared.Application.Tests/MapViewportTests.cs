using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Tests;

public sealed class MapViewportTests
{
    [Fact]
    public void Fit_places_the_world_origin_at_the_view_center()
    {
        var viewport = MapViewport.Fit(GeoBounds.World, 1000, 500, 0);

        var center = viewport.ToScreen(new GeoPoint(0, 0));

        ((double)center.X).Should().BeApproximately(500, 0.01);
        ((double)center.Y).Should().BeApproximately(250, 0.01);
    }

    [Fact]
    public void Fit_falls_back_to_unit_scale_for_bounds_with_no_span()
    {
        var viewport = MapViewport.Fit(new GeoBounds(0, 0, 0, 0), 100, 100, 0);

        viewport.Scale.Should().Be(1);
    }

    [Fact]
    public void Fit_centers_aspect_preserved_content_in_a_wider_window()
    {
        var viewport = MapViewport.Fit(GeoBounds.World, 2000, 500, 0);

        var left = viewport.ToScreen(new GeoPoint(-180, 0));
        var right = viewport.ToScreen(new GeoPoint(180, 0));
        var center = viewport.ToScreen(new GeoPoint(0, 0));

        ((double)left.X).Should().BeApproximately(750, 0.01);
        ((double)right.X).Should().BeApproximately(1250, 0.01);
        ((double)center.X).Should().BeApproximately(1000, 0.01);
        ((double)center.Y).Should().BeApproximately(250, 0.01);
    }
}
