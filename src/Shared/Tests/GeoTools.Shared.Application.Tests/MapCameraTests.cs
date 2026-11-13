using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Tests;

public sealed class MapCameraTests
{
    [Fact]
    public void Fit_centers_the_bounds_in_the_viewport()
    {
        var bounds = new GeoBounds(-10, -20, 30, 40);
        var camera = MapCamera.Fit(bounds, 800, 600);

        var screen = camera.Viewport(800, 600).ToScreen(camera.CenterX, camera.CenterY);

        ((double)screen.X).Should().BeApproximately(400, 0.01);
        ((double)screen.Y).Should().BeApproximately(300, 0.01);
    }

    [Fact]
    public void Fit_puts_all_four_projected_corners_inside_the_padded_rectangle()
    {
        var bounds = new GeoBounds(-10, -20, 30, 40);
        var camera = MapCamera.Fit(bounds, 800, 600, 24f);
        var viewport = camera.Viewport(800, 600);

        foreach (var corner in Corners(bounds))
        {
            var (x, y) = MapProjection.Project(corner);
            var screen = viewport.ToScreen(x, y);

            ((double)screen.X).Should().BeInRange(24, 776);
            ((double)screen.Y).Should().BeInRange(24, 576);
        }
    }

    [Fact]
    public void Fit_leaves_at_least_one_axis_tight_against_the_padding()
    {
        var camera = MapCamera.Fit(GeoBounds.World, 1000, 400, 24f);
        var viewport = camera.Viewport(1000, 400);

        var north = viewport.ToScreen(new GeoPoint(0, 85.05112878));
        var south = viewport.ToScreen(new GeoPoint(0, -85.05112878));

        camera.Scale.Should().BeApproximately(352, 1e-6);
        ((double)north.Y).Should().BeApproximately(24, 0.01);
        ((double)south.Y).Should().BeApproximately(376, 0.01);
    }

    [Fact]
    public void Fit_treats_degenerate_bounds_as_a_single_point()
    {
        var camera = MapCamera.Fit(new GeoBounds(5, 5, 5, 5), 400, 300);

        var screen = camera.Viewport(400, 300).ToScreen(camera.CenterX, camera.CenterY);

        camera.Scale.Should().Be(1);
        ((double)screen.X).Should().BeApproximately(200, 0.01);
        ((double)screen.Y).Should().BeApproximately(150, 0.01);
    }

    [Fact]
    public void Pan_moves_the_camera_by_the_screen_distance_in_world_units()
    {
        var camera = new MapCamera(0.5, 0.5, 100);

        var panned = camera.Pan(5, -2.5, 800, 600);
        var screen = panned.Viewport(800, 600).ToScreen(0.5, 0.5);

        panned.CenterX.Should().BeApproximately(0.55, 1e-12);
        panned.CenterY.Should().BeApproximately(0.475, 1e-12);
        panned.Scale.Should().Be(100);
        ((double)screen.X).Should().BeApproximately(395, 0.01);
        ((double)screen.Y).Should().BeApproximately(302.5, 0.01);
    }

    [Fact]
    public void Pan_is_a_no_op_for_an_unusable_scale()
    {
        var camera = new MapCamera(0.5, 0.5, 0);

        camera.Pan(10, 10, 800, 600).Should().Be(camera);
    }

    [Fact]
    public void Zoom_scales_about_the_center()
    {
        var camera = new MapCamera(0.25, 0.75, 100);

        var zoomed = camera.Zoom(2.5);

        zoomed.CenterX.Should().Be(0.25);
        zoomed.CenterY.Should().Be(0.75);
        zoomed.Scale.Should().Be(250);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Zoom_ignores_a_non_positive_or_non_finite_factor(double factor)
    {
        var camera = new MapCamera(0.25, 0.75, 100);

        camera.Zoom(factor).Should().Be(camera);
    }

    [Fact]
    public void Clamp_keeps_a_larger_bounds_inside_the_viewport()
    {
        var clamped = new MapCamera(5, -5, 1000).Clamp(GeoBounds.World, 200, 200);

        clamped.CenterX.Should().BeApproximately(0.9, 1e-6);
        clamped.CenterY.Should().BeApproximately(0.1, 1e-6);

        var world = clamped.Viewport(200, 200).WorldBounds(200, 200);
        world.MinX.Should().BeApproximately(0.8, 1e-6);
        world.MaxX.Should().BeApproximately(1.0, 1e-6);
        world.MinY.Should().BeApproximately(0.0, 1e-6);
        world.MaxY.Should().BeApproximately(0.2, 1e-6);
    }

    [Fact]
    public void Clamp_keeps_a_center_that_is_already_inside()
    {
        var camera = new MapCamera(0.5, 0.5, 1000);

        camera.Clamp(GeoBounds.World, 200, 200).Should().Be(camera);
    }

    [Fact]
    public void Clamp_centers_an_axis_that_is_smaller_than_the_viewport()
    {
        var clamped = new MapCamera(5, -5, 1000).Clamp(GeoBounds.World, 5000, 5000);

        clamped.CenterX.Should().BeApproximately(0.5, 1e-6);
        clamped.CenterY.Should().BeApproximately(0.5, 1e-6);
    }

    [Fact]
    public void Clamp_is_a_no_op_for_an_unusable_scale()
    {
        var camera = new MapCamera(0.5, 0.5, 0);

        camera.Clamp(GeoBounds.World, 800, 600).Should().Be(camera);
    }

    [Fact]
    public void CenterGeo_unprojects_the_camera_center()
    {
        var camera = new MapCamera(0.5, 0.5, 100);

        camera.CenterGeo.Should().Be(new GeoPoint(0, 0));
    }

    private static GeoPoint[] Corners(GeoBounds bounds) =>
        [
            new GeoPoint(bounds.MinLongitude, bounds.MinLatitude),
            new GeoPoint(bounds.MaxLongitude, bounds.MinLatitude),
            new GeoPoint(bounds.MaxLongitude, bounds.MaxLatitude),
            new GeoPoint(bounds.MinLongitude, bounds.MaxLatitude),
        ];
}
