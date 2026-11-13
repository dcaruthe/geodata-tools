using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class MapViewTests
{
    [Fact]
    public void Compose_draws_borders_over_a_black_background_with_the_key_hint()
    {
        var view = CreateView(Borders());

        var scene = view.Compose(new ViewSize(800, 600));

        scene.Background.Should().Be(Color.Black);
        scene.Shapes.OfType<LineDto>().Should().NotBeEmpty();
        scene.Shapes.OfType<CircleDto>().Should().NotBeEmpty();
        scene.Shapes.OfType<PolygonDto>().Should().NotBeEmpty();
        scene.Shapes.OfType<PolylineDto>().Should().NotBeEmpty();
        scene.Shapes.OfType<TextDto>().Should().Contain(text => text.Text.Contains("Tab"));
        scene.Shapes.OfType<TextDto>().Should().Contain(text => text.Text.Contains("WASD"));
        scene.Shapes.OfType<TextDto>().Should().Contain(text => text.Text.Contains("zoom"));
    }

    [Fact]
    public void Compose_draws_the_camera_status_line_at_the_fit_scale()
    {
        var view = CreateView(Borders());

        var scene = view.Compose(new ViewSize(800, 600));

        scene
            .Shapes.OfType<TextDto>()
            .Should()
            .Contain(text => text.Text.Contains("Center") && text.Text.Contains("Zoom 1.0x"));
    }

    [Fact]
    public void Compose_draws_only_the_configured_admin_levels()
    {
        var view = CreateView(Borders());

        var scene = view.Compose(new ViewSize(800, 600));

        scene.Shapes.OfType<PolygonDto>().Should().ContainSingle();
        scene.Shapes.OfType<PolylineDto>().Should().ContainSingle();
    }

    [Fact]
    public void Compose_returns_the_same_scene_for_the_same_state()
    {
        var view = CreateView(Borders());
        var size = new ViewSize(800, 600);

        var first = view.Compose(size);
        var second = view.Compose(size);

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Compose_recomposes_for_a_different_size()
    {
        var view = CreateView(Borders());

        var first = view.Compose(new ViewSize(800, 600));
        var second = view.Compose(new ViewSize(1024, 768));

        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public void Compose_updates_when_the_geometry_changes()
    {
        var store = CreateStore(Borders());
        var view = CreateView(store);
        var size = new ViewSize(800, 600);

        var first = view.Compose(size);
        store.Set(BorderGeometry.Empty, []);
        var second = view.Compose(size);

        first.Shapes.OfType<PolygonDto>().Should().NotBeEmpty();
        second.Shapes.OfType<PolygonDto>().Should().BeEmpty();
    }

    [Fact]
    public void Compose_recomposes_when_the_map_state_changes()
    {
        var store = CreateStore(Borders());
        var mapState = new MapStateStore();
        var settings = new TestSettingsStore();
        var view = CreateView(store, settings, mapState: mapState);
        var size = new ViewSize(800, 600);

        var first = view.Compose(size);
        var fit = MapCamera.Fit(Borders().Bounds, size.Width, size.Height, 24f);
        var camera = fit.Zoom(2);
        mapState.Set(
            new MapViewState(
                camera,
                camera.CenterGeo,
                camera.Viewport(size.Width, size.Height).WorldBounds(size.Width, size.Height),
                "Focused.",
                [],
                []
            )
        );
        var second = view.Compose(size);

        second.Should().NotBeSameAs(first);
        second.Shapes.OfType<TextDto>().Should().Contain(text => text.Text == "Focused.");
        second.Shapes.OfType<TextDto>().Should().Contain(text => text.Text.Contains("Zoom 2.0x"));
    }

    [Fact]
    public void Compose_reuses_the_retained_scene_for_a_pan_at_the_same_scale()
    {
        var store = CreateStore(Borders());
        var mapState = new MapStateStore();
        var view = CreateView(store, mapState: mapState);
        var size = new ViewSize(800, 600);
        view.Compose(size);
        var fit = MapCamera.Fit(Borders().Bounds, size.Width, size.Height, 24f);

        var panned = fit.Pan(64, 0, size.Width, size.Height);
        Publish(mapState, panned, size);
        view.Compose(size);

        view.RetainedBuildCount.Should().Be(1);
        view.RetainedTransformCount.Should().Be(1);
    }

    [Fact]
    public void Compose_rebuilds_the_retained_scene_for_a_zoom()
    {
        var store = CreateStore(Borders());
        var mapState = new MapStateStore();
        var view = CreateView(store, mapState: mapState);
        var size = new ViewSize(800, 600);
        view.Compose(size);
        var fit = MapCamera.Fit(Borders().Bounds, size.Width, size.Height, 24f);

        Publish(mapState, fit.Zoom(1.4), size);
        view.Compose(size);

        view.RetainedBuildCount.Should().Be(2);
        view.RetainedTransformCount.Should().Be(0);
    }

    [Fact]
    public void Compose_labels_an_empty_admin_level_set()
    {
        var store = new BorderGeometryStore();
        store.Set(Borders(), [2, 4]);
        var settings = new TestSettingsStore { Map = new MapSettings([]) };
        var view = CreateView(store, settings);

        var scene = view.Compose(new ViewSize(800, 600));

        scene
            .Shapes.OfType<TextDto>()
            .Should()
            .Contain(text => text.Text.Contains("No admin levels selected"));
        scene.Shapes.OfType<PolygonDto>().Should().BeEmpty();
    }

    [Fact]
    public void Compose_labels_levels_that_are_not_in_the_data()
    {
        var store = new BorderGeometryStore();
        store.Set(Borders(), [2, 4]);
        var settings = new TestSettingsStore { Map = new MapSettings([6]) };
        var view = CreateView(store, settings);

        var scene = view.Compose(new ViewSize(800, 600));

        scene
            .Shapes.OfType<TextDto>()
            .Should()
            .Contain(text => text.Text.Contains("No borders at admin levels 6"));
        scene.Shapes.OfType<PolygonDto>().Should().BeEmpty();
    }

    [Fact]
    public void Compose_labels_missing_border_data()
    {
        var view = CreateView(BorderGeometry.Empty);

        var scene = view.Compose(new ViewSize(800, 600));

        scene
            .Shapes.OfType<TextDto>()
            .Should()
            .Contain(text => text.Text.Contains("No border data"));
        scene.Shapes.OfType<TextDto>().Should().Contain(text => text.Text.Contains("maintenance"));
    }

    [Fact]
    public void Compose_labels_a_running_bootstrap()
    {
        var progress = new ProgressStore();
        progress.Set(
            BootstrapProgressSnapshot.Idle with
            {
                Phase = BootstrapPhase.Running,
                Label = "Downloading europe",
                Fraction = 0.4,
            }
        );
        var view = CreateView(BorderGeometry.Empty, progress: progress);

        var scene = view.Compose(new ViewSize(800, 600));

        scene
            .Shapes.OfType<TextDto>()
            .Should()
            .Contain(text => text.Text.Contains("Loading border data"));
    }

    [Fact]
    public void Compose_labels_a_failed_bootstrap()
    {
        var progress = new ProgressStore();
        progress.Set(
            BootstrapProgressSnapshot.Idle with
            {
                Phase = BootstrapPhase.Failed,
                Error = "osmium is missing",
            }
        );
        var view = CreateView(BorderGeometry.Empty, progress: progress);

        var scene = view.Compose(new ViewSize(800, 600));

        scene
            .Shapes.OfType<TextDto>()
            .Should()
            .Contain(text => text.Text.Contains("osmium is missing"));
    }

    [Fact]
    public void Compose_omits_the_grid_and_crosshair_when_the_flags_are_off()
    {
        var store = new BorderGeometryStore();
        store.Set(Borders(), [2, 4]);
        var settings = new TestSettingsStore();
        settings.SetFlag(FeatureFlag.MapGrid, false);
        settings.SetFlag(FeatureFlag.MapCrosshair, false);
        var view = CreateView(store, settings);

        var scene = view.Compose(new ViewSize(800, 600));

        scene.Shapes.OfType<LineDto>().Should().BeEmpty();
        scene.Shapes.OfType<CircleDto>().Should().BeEmpty();
    }

    [Fact]
    public void Id_is_map()
    {
        CreateView(Borders()).Id.Should().Be(ViewId.Map);
    }

    [Fact]
    public void Compose_draws_the_focus_overlay_highlight_and_label()
    {
        var store = CreateStore(Borders());
        var overlay = new FocusOverlayStore();
        overlay.Set(
            GeometryProjector.Project([Ring(2, -10, -10, 10, 10)]),
            "Region A",
            new GeoBounds(-10, -10, 10, 10)
        );
        var view = CreateView(store, overlay: overlay);

        var scene = view.Compose(new ViewSize(800, 600));

        scene
            .Shapes.OfType<PolygonDto>()
            .Should()
            .Contain(polygon =>
                polygon.Color == new Color(255, 204, 102) && polygon.Thickness == 2
            );
        scene.Shapes.OfType<TextDto>().Should().Contain(text => text.Text == "Region A");
    }

    [Fact]
    public void Compose_omits_the_overlay_label_when_it_is_off_screen()
    {
        var store = CreateStore(Borders());
        var overlay = new FocusOverlayStore();
        overlay.Set(
            GeometryProjector.Project([Ring(2, -10, -10, 10, 10)]),
            "Region A",
            new GeoBounds(-10, -10, 10, 10)
        );
        var mapState = new MapStateStore();
        var view = CreateView(store, mapState: mapState, overlay: overlay);
        var size = new ViewSize(800, 600);
        var camera = new MapCamera(0.9, 0.9, 20000);
        mapState.Set(
            new MapViewState(
                camera,
                camera.CenterGeo,
                camera.Viewport(size.Width, size.Height).WorldBounds(size.Width, size.Height),
                string.Empty,
                [new FocusedRegion("A", "Region A", 2, new GeoBounds(-10, -10, 10, 10))],
                [2]
            )
        );

        var scene = view.Compose(size);

        scene.Shapes.OfType<TextDto>().Should().NotContain(text => text.Text == "Region A");
    }

    [Fact]
    public void Compose_colors_a_revealed_level_brighter_than_the_root_levels()
    {
        var store = CreateStore(Borders());
        var mapState = new MapStateStore();
        var view = CreateView(store, mapState: mapState);
        var size = new ViewSize(800, 600);
        var fit = MapCamera.Fit(Borders().Bounds, size.Width, size.Height, 24f);
        mapState.Set(
            new MapViewState(
                fit,
                fit.CenterGeo,
                fit.Viewport(size.Width, size.Height).WorldBounds(size.Width, size.Height),
                string.Empty,
                [new FocusedRegion("A", "Region A", 2, new GeoBounds(-10, -10, 10, 10))],
                [2, 4]
            )
        );

        var scene = view.Compose(size);

        scene
            .Shapes.OfType<PolygonDto>()
            .Should()
            .Contain(polygon => polygon.Color == new Color(140, 190, 235));
        scene
            .Shapes.OfType<PolygonDto>()
            .Should()
            .Contain(polygon => polygon.Color == new Color(200, 205, 215));
    }

    [Fact]
    public void Compose_hint_mentions_focus_and_back()
    {
        var view = CreateView(Borders());

        var scene = view.Compose(new ViewSize(800, 600));

        scene
            .Shapes.OfType<TextDto>()
            .Should()
            .Contain(text => text.Text.Contains("F focus") && text.Text.Contains("Backspace back"));
    }

    private static MapView CreateView(BorderGeometry geometry) =>
        CreateView(geometry, new ProgressStore());

    private static MapView CreateView(BorderGeometry geometry, ProgressStore progress) =>
        CreateView(CreateStore(geometry), new TestSettingsStore(), progress, new MapStateStore());

    private static MapView CreateView(
        BorderGeometryStore store,
        TestSettingsStore? settings = null,
        ProgressStore? progress = null,
        MapStateStore? mapState = null,
        FocusOverlayStore? overlay = null
    )
    {
        settings ??= new TestSettingsStore();
        mapState ??= new MapStateStore();
        overlay ??= new FocusOverlayStore();
        var focus = new MapFocusService(
            new FakeRegionIndex(),
            new FakeRegionIndexWriter(),
            store,
            mapState,
            overlay,
            settings,
            NullLogger<MapFocusService>.Instance
        );
        var controller = new MapController(
            mapState,
            store,
            settings,
            focus,
            NullLogger<MapController>.Instance
        );
        return new MapView(
            store,
            overlay,
            mapState,
            controller,
            settings,
            progress ?? new ProgressStore(),
            NullLogger<MapView>.Instance
        );
    }

    private static void Publish(MapStateStore store, MapCamera camera, ViewSize size) =>
        store.Set(
            new MapViewState(
                camera,
                camera.CenterGeo,
                camera.Viewport(size.Width, size.Height).WorldBounds(size.Width, size.Height),
                string.Empty,
                [],
                []
            )
        );

    private static BorderGeometryStore CreateStore(BorderGeometry geometry)
    {
        var store = new BorderGeometryStore();
        store.Set(
            geometry,
            geometry.Polylines.Select(polyline => polyline.AdminLevel).Distinct().Order().ToArray()
        );
        return store;
    }

    private static BorderGeometry Borders()
    {
        var polylines = new[]
        {
            Ring(2, -10, -10, 10, 10),
            new GeoPolyline([new GeoPoint(0, 0), new GeoPoint(5, 5)], Closed: false, AdminLevel: 2),
            Ring(4, 20, 20, 40, 40),
        };

        return BorderGeometry.FromGeo(
            polylines,
            GeoBounds.FromPolylines(polylines, GeoBounds.World)
        );
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
