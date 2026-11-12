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

    private static MapView CreateView(BorderGeometry geometry) =>
        CreateView(geometry, new ProgressStore());

    private static MapView CreateView(BorderGeometry geometry, ProgressStore progress) =>
        new(CreateStore(geometry), new TestSettingsStore(), progress, NullLogger<MapView>.Instance);

    private static MapView CreateView(
        BorderGeometryStore store,
        TestSettingsStore? settings = null,
        ProgressStore? progress = null
    ) =>
        new(
            store,
            settings ?? new TestSettingsStore(),
            progress ?? new ProgressStore(),
            NullLogger<MapView>.Instance
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

        return new BorderGeometry(polylines, GeoBounds.FromPolylines(polylines, GeoBounds.World));
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
