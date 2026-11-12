using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class MapViewTests
{
    [Fact]
    public void Compose_draws_borders_over_a_black_background_with_the_key_hint()
    {
        var scene = CreateView().Compose(new ViewSize(800, 600));

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
        var scene = CreateView().Compose(new ViewSize(800, 600));

        scene.Shapes.OfType<PolygonDto>().Should().ContainSingle();
    }

    [Fact]
    public void Compose_adds_levels_from_a_configuration_change_without_reloading_geometry()
    {
        var source = new FakeBorderSource(Borders());
        var options = new TestOptionsMonitor<MapOptions>(
            new MapOptions { AdminLevels = [2], BordersDirectory = "geodata/borders" }
        );
        var view = CreateView(source, options);
        var size = new ViewSize(800, 600);

        var before = view.Compose(size);
        options.CurrentValue = new MapOptions
        {
            AdminLevels = [2, 4],
            BordersDirectory = "geodata/borders",
        };
        var after = view.Compose(size);

        before.Shapes.OfType<PolygonDto>().Should().ContainSingle();
        after.Shapes.OfType<PolygonDto>().Should().HaveCount(2);
        after.Should().NotBeSameAs(before);
        source.LoadCalls.Should().Be(1);
    }

    [Fact]
    public void Compose_returns_the_same_scene_for_the_same_size_and_levels()
    {
        var view = CreateView();
        var size = new ViewSize(800, 600);

        var first = view.Compose(size);
        var second = view.Compose(size);

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Compose_recomposes_for_a_different_size()
    {
        var view = CreateView();

        var first = view.Compose(new ViewSize(800, 600));
        var second = view.Compose(new ViewSize(1024, 768));

        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public void Compose_labels_an_empty_admin_level_set()
    {
        var view = CreateView(
            new FakeBorderSource(Borders()),
            new TestOptionsMonitor<MapOptions>(new MapOptions { AdminLevels = [] })
        );

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
        var view = CreateView(
            new FakeBorderSource(Borders()),
            new TestOptionsMonitor<MapOptions>(new MapOptions { AdminLevels = [6] })
        );

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
        var view = new MapView(
            new FakeBorderSource(BorderGeometry.Empty),
            new TestOptionsMonitor<MapOptions>(
                new MapOptions { BordersDirectory = "missing/borders" }
            ),
            NullLogger<MapView>.Instance
        );

        var scene = view.Compose(new ViewSize(800, 600));

        scene
            .Shapes.OfType<TextDto>()
            .Should()
            .Contain(text => text.Text.Contains("No border data in missing/borders"));
        scene.Shapes.OfType<TextDto>().Should().Contain(text => text.Text.Contains("Tab"));
    }

    [Fact]
    public void Id_is_map()
    {
        CreateView().Id.Should().Be(ViewId.Map);
    }

    [Fact]
    public void Compose_uses_default_options_when_the_monitor_throws_before_any_good_value()
    {
        var monitor = new ThrowingOptionsMonitor(
            new MapOptions { AdminLevels = [2], BordersDirectory = "geodata/borders" }
        )
        {
            Throw = true,
        };
        var view = new MapView(
            new FakeBorderSource(Borders()),
            monitor,
            NullLogger<MapView>.Instance
        );

        var scene = view.Compose(new ViewSize(800, 600));

        scene.Shapes.OfType<PolygonDto>().Should().ContainSingle();
        scene.Shapes.OfType<PolylineDto>().Should().ContainSingle();
    }

    [Fact]
    public void Compose_reuses_the_last_good_options_when_the_monitor_starts_throwing()
    {
        var monitor = new ThrowingOptionsMonitor(
            new MapOptions { AdminLevels = [4], BordersDirectory = "geodata/borders" }
        );
        var view = new MapView(
            new FakeBorderSource(Borders()),
            monitor,
            NullLogger<MapView>.Instance
        );
        view.Compose(new ViewSize(800, 600));

        monitor.Throw = true;
        var scene = view.Compose(new ViewSize(1024, 768));

        scene.Shapes.OfType<PolygonDto>().Should().ContainSingle();
        scene.Shapes.OfType<PolylineDto>().Should().BeEmpty();
    }

    private static MapView CreateView() =>
        CreateView(
            new FakeBorderSource(Borders()),
            new TestOptionsMonitor<MapOptions>(new MapOptions { AdminLevels = [2] })
        );

    private static MapView CreateView(IBorderSource source, IOptionsMonitor<MapOptions> options) =>
        new(source, options, NullLogger<MapView>.Instance);

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

    private sealed class FakeBorderSource(BorderGeometry geometry) : IBorderSource
    {
        public int LoadCalls { get; private set; }

        public BorderGeometry Load()
        {
            LoadCalls++;
            return geometry;
        }
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; set; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class ThrowingOptionsMonitor(MapOptions value) : IOptionsMonitor<MapOptions>
    {
        public bool Throw { get; set; }

        public MapOptions CurrentValue =>
            Throw
                ? throw new OptionsValidationException("Map", typeof(MapOptions), ["invalid"])
                : value;

        public MapOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<MapOptions, string?> listener) => null;
    }
}
