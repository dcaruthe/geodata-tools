using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Domain.Primatives;

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

    [Fact]
    public void FromCamera_places_the_camera_center_at_the_view_center()
    {
        var viewport = MapViewport.FromCamera(new MapCamera(0.25, 0.75, 400), 800, 600);

        var screen = viewport.ToScreen(0.25, 0.75);

        viewport.Scale.Should().Be(400);
        ((double)screen.X).Should().BeApproximately(400, 0.01);
        ((double)screen.Y).Should().BeApproximately(300, 0.01);
    }

    [Fact]
    public void ToWorld_round_trips_ToScreen()
    {
        var viewport = MapViewport.Fit(GeoBounds.World, 1000, 500, 0);

        var screen = viewport.ToScreen(0.37, 0.62);
        var (x, y) = viewport.ToWorld(screen);

        x.Should().BeApproximately(0.37, 1e-6);
        y.Should().BeApproximately(0.62, 1e-6);
    }

    [Fact]
    public void WorldBounds_matches_the_view_corners()
    {
        var viewport = MapViewport.Fit(GeoBounds.World, 1000, 500, 0);
        var bounds = viewport.WorldBounds(1000, 500);

        var (topLeftX, topLeftY) = viewport.ToWorld(new Point(0, 0));
        var (bottomRightX, bottomRightY) = viewport.ToWorld(new Point(1000, 500));

        bounds.MinX.Should().BeApproximately(topLeftX, 1e-9);
        bounds.MinY.Should().BeApproximately(topLeftY, 1e-9);
        bounds.MaxX.Should().BeApproximately(bottomRightX, 1e-9);
        bounds.MaxY.Should().BeApproximately(bottomRightY, 1e-9);
    }
}
