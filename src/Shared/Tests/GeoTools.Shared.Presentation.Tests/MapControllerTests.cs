using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Presentation.Views;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class MapControllerTests
{
    private static readonly ViewSize Size = new(800, 600);

    [Fact]
    public void EnsureInitialised_fits_the_border_bounds_with_padding()
    {
        var fixture = new Fixture(Borders());

        fixture.Controller.EnsureInitialised(Size);

        var state = fixture.MapState.Current;
        var expected = MapCamera.Fit(fixture.Geometry.Bounds, Size.Width, Size.Height, 24f);
        state.IsInitialised.Should().BeTrue();
        state.Camera.Should().Be(expected);
        state.Center.Should().Be(expected.CenterGeo);
        state
            .Viewport.Should()
            .Be(expected.Viewport(Size.Width, Size.Height).WorldBounds(Size.Width, Size.Height));
        state.Status.Should().BeEmpty();
    }

    [Fact]
    public void EnsureInitialised_keeps_the_existing_state_and_version()
    {
        var fixture = new Fixture(Borders());
        fixture.Controller.EnsureInitialised(Size);
        var camera = fixture.MapState.Current.Camera;

        fixture.Controller.EnsureInitialised(new ViewSize(1000, 700));

        fixture.MapState.Current.Camera.Should().Be(camera);
        fixture.MapState.Version.Should().Be(1);
    }

    [Fact]
    public void EnsureInitialised_uses_the_world_when_the_geometry_is_empty()
    {
        var fixture = new Fixture(BorderGeometry.Empty);

        fixture.Controller.EnsureInitialised(Size);

        var expected = MapCamera.Fit(GeoBounds.World, Size.Width, Size.Height, 24f);
        fixture.MapState.Current.IsInitialised.Should().BeTrue();
        fixture.MapState.Current.Camera.Should().Be(expected);
        fixture
            .MapState.Current.Viewport.Should()
            .Be(expected.Viewport(Size.Width, Size.Height).WorldBounds(Size.Width, Size.Height));
    }

    [Fact]
    public void Pan_moves_the_camera_64_pixels_in_every_direction()
    {
        AssertPan(ViewKey.Left, -1, 0);
        AssertPan(ViewKey.Right, 1, 0);
        AssertPan(ViewKey.Up, 0, -1);
        AssertPan(ViewKey.Down, 0, 1);
    }

    private static void AssertPan(ViewKey key, int xDirection, int yDirection)
    {
        var fixture = new Fixture(Borders());
        fixture.Controller.EnsureInitialised(Size);
        fixture.ZoomIn(3);
        var before = fixture.MapState.Current.Camera;

        fixture.Controller.Handle(key, Size).Should().BeTrue();

        var after = fixture.MapState.Current.Camera;
        after
            .CenterX.Should()
            .BeApproximately(before.CenterX + xDirection * 64 / before.Scale, 1e-9);
        after
            .CenterY.Should()
            .BeApproximately(before.CenterY + yDirection * 64 / before.Scale, 1e-9);
        after.Scale.Should().Be(before.Scale);
    }

    [Fact]
    public void Pan_moves_west_and_east_in_longitude()
    {
        var west = new Fixture(Borders());
        west.Controller.EnsureInitialised(Size);
        west.ZoomIn(3);
        var westBefore = west.MapState.Current.Center;
        west.Controller.Handle(ViewKey.Left, Size);
        west.MapState.Current.Center.Longitude.Should().BeLessThan(westBefore.Longitude);

        var east = new Fixture(Borders());
        east.Controller.EnsureInitialised(Size);
        east.ZoomIn(3);
        var eastBefore = east.MapState.Current.Center;
        east.Controller.Handle(ViewKey.Right, Size);
        east.MapState.Current.Center.Longitude.Should().BeGreaterThan(eastBefore.Longitude);
    }

    [Fact]
    public void Pan_moves_north_and_south_in_latitude()
    {
        var north = new Fixture(Borders());
        north.Controller.EnsureInitialised(Size);
        north.ZoomIn(3);
        var northBefore = north.MapState.Current.Center;
        north.Controller.Handle(ViewKey.Up, Size);
        north.MapState.Current.Center.Latitude.Should().BeGreaterThan(northBefore.Latitude);

        var south = new Fixture(Borders());
        south.Controller.EnsureInitialised(Size);
        south.ZoomIn(3);
        var southBefore = south.MapState.Current.Center;
        south.Controller.Handle(ViewKey.Down, Size);
        south.MapState.Current.Center.Latitude.Should().BeLessThan(southBefore.Latitude);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(4000)]
    public void Pan_converts_a_fixed_64_pixels_to_world_units(double scale)
    {
        var fixture = new Fixture(BorderGeometry.Empty);
        fixture.SetCamera(new MapCamera(0.5, 0.5, scale));
        var before = fixture.MapState.Current.Camera;

        fixture.Controller.Handle(ViewKey.Right, Size);

        fixture
            .MapState.Current.Camera.CenterX.Should()
            .BeApproximately(before.CenterX + 64 / scale, 1e-12);
    }

    [Fact]
    public void Pan_stops_at_the_world_edge()
    {
        var fixture = new Fixture(BorderGeometry.Empty);
        fixture.SetCamera(new MapCamera(0.5, 0.5, 1000));

        for (var index = 0; index < 100; index++)
            fixture.Controller.Handle(ViewKey.Right, Size);

        fixture.MapState.Current.Camera.CenterX.Should().BeApproximately(0.6, 1e-9);
        fixture
            .MapState.Current.Camera.Viewport(Size.Width, Size.Height)
            .WorldBounds(Size.Width, Size.Height)
            .MaxX.Should()
            .BeApproximately(1, 1e-9);
    }

    [Fact]
    public void ZoomIn_multiplies_the_fit_scale_by_1_4()
    {
        var fixture = new Fixture(BorderGeometry.Empty);
        fixture.Controller.EnsureInitialised(Size);
        var fitScale = fixture.MapState.Current.Camera.Scale;

        fixture.Controller.Handle(ViewKey.ZoomIn, Size);

        fixture.MapState.Current.Camera.Scale.Should().BeApproximately(fitScale * 1.4, 1e-9);
    }

    [Fact]
    public void ZoomOut_divides_a_zoomed_scale_by_1_4()
    {
        var fixture = new Fixture(BorderGeometry.Empty);
        fixture.Controller.EnsureInitialised(Size);
        fixture.Controller.Handle(ViewKey.ZoomIn, Size);
        var zoomed = fixture.MapState.Current.Camera.Scale;

        fixture.Controller.Handle(ViewKey.ZoomOut, Size);

        fixture.MapState.Current.Camera.Scale.Should().BeApproximately(zoomed / 1.4, 1e-9);
    }

    [Fact]
    public void ZoomOut_stops_at_the_fit_scale()
    {
        var fixture = new Fixture(BorderGeometry.Empty);
        fixture.Controller.EnsureInitialised(Size);
        var fitCamera = fixture.MapState.Current.Camera;

        fixture.Controller.Handle(ViewKey.ZoomOut, Size);

        fixture.MapState.Current.Camera.Scale.Should().Be(fitCamera.Scale);
        fixture.MapState.Current.Camera.CenterX.Should().BeApproximately(fitCamera.CenterX, 1e-12);
    }

    [Fact]
    public void ZoomIn_stops_at_64_times_the_fit_scale()
    {
        var fixture = new Fixture(BorderGeometry.Empty);
        fixture.Controller.EnsureInitialised(Size);
        var fitScale = fixture.MapState.Current.Camera.Scale;

        for (var index = 0; index < 50; index++)
            fixture.Controller.Handle(ViewKey.ZoomIn, Size);

        fixture.MapState.Current.Camera.Scale.Should().BeApproximately(fitScale * 64, 1e-6);
    }

    [Fact]
    public void Handle_returns_false_and_leaves_the_state_for_unhandled_keys()
    {
        foreach (var key in new[] { ViewKey.Enter, ViewKey.Backspace, ViewKey.Home, ViewKey.End })
        {
            var fixture = new Fixture(Borders());

            fixture.Controller.Handle(key, Size).Should().BeFalse();

            fixture.MapState.Version.Should().Be(0);
            fixture.MapState.Current.IsInitialised.Should().BeFalse();
        }
    }

    private sealed class Fixture
    {
        public Fixture(BorderGeometry geometry)
        {
            Geometry = geometry;
            Store = new BorderGeometryStore();
            Store.Set(
                geometry,
                geometry
                    .Polylines.Select(polyline => polyline.AdminLevel)
                    .Distinct()
                    .Order()
                    .ToArray()
            );
            MapState = new MapStateStore();
            Controller = new MapController(
                MapState,
                Store,
                new TestSettingsStore(),
                NullLogger<MapController>.Instance
            );
        }

        public BorderGeometry Geometry { get; }

        public BorderGeometryStore Store { get; }

        public MapStateStore MapState { get; }

        public MapController Controller { get; }

        public void SetCamera(MapCamera camera) =>
            MapState.Set(
                new MapViewState(
                    camera,
                    camera.CenterGeo,
                    camera.Viewport(Size.Width, Size.Height).WorldBounds(Size.Width, Size.Height),
                    string.Empty
                )
            );

        public void ZoomIn(int times)
        {
            for (var index = 0; index < times; index++)
                Controller.Handle(ViewKey.ZoomIn, Size);
        }
    }

    private static BorderGeometry Borders()
    {
        var polylines = new[]
        {
            Ring(-10, -10, 10, 10),
            new GeoPolyline([new GeoPoint(0, 0), new GeoPoint(5, 5)], Closed: false, AdminLevel: 2),
            Ring(20, 20, 40, 40),
        };

        return BorderGeometry.FromGeo(
            polylines,
            GeoBounds.FromPolylines(polylines, GeoBounds.World)
        );
    }

    private static GeoPolyline Ring(
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
            AdminLevel: 2
        );
}
