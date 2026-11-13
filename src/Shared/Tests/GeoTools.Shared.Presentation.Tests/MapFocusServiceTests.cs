using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Presentation.Views;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class MapFocusServiceTests
{
    private static readonly ViewSize Size = new(800, 600);

    [Fact]
    public void FocusAt_reports_no_match_and_keeps_the_camera()
    {
        var fixture = new Fixture();
        var camera = new MapCamera(0.5, 0.5, 100);
        fixture.MapState.Set(State(camera));

        fixture.Service.FocusAt(new GeoPoint(0, 0), Size);

        fixture.MapState.Current.Camera.Should().Be(camera);
        fixture
            .MapState.Current.Status.Should()
            .Be("No administrative region under the crosshair at levels 2.");
    }

    [Fact]
    public void FocusAt_reveals_level_4_children_of_a_level_2_region()
    {
        var fixture = new Fixture();
        var a = Summary("A", "Region A", 2, -80, -50, 80, 50);
        var b = Summary("B", "Region B", 4, -80, -50, 0, 0);
        var c = Summary("C", "Region C", 4, 0, 0, 80, 50);
        fixture.Index.Add(a, [Ring(2, -80, -50, 80, 50)]);
        fixture.Index.Add(b, [Ring(4, -80, -50, 0, 0)]);
        fixture.Index.Add(c, [Ring(4, 0, 0, 80, 50)]);
        fixture.Writer.AddChildren("A", 4, [b, c]);
        fixture.Index.FindContainingResult = [a];

        fixture.Service.FocusAt(new GeoPoint(0, 0), Size);

        var state = fixture.MapState.Current;
        state.Focus.Should().Equal(new FocusedRegion("A", "Region A", 2, a.Bounds));
        state.VisibleLevels.Should().Equal(2, 4);
        state.Status.Should().Contain("revealing level 4").And.Contain("2 regions");

        fixture.Geometry.AvailableAdminLevels.Should().Equal(2, 4);
        fixture
            .Geometry.Geometry.Polylines.Where(polyline => polyline.AdminLevel == 4)
            .Should()
            .HaveCount(2);

        fixture.Overlay.Label.Should().Be("Region A");
        fixture.Overlay.Bounds.Should().Be(a.Bounds);
        fixture.Overlay.Polylines.Should().ContainSingle();
    }

    [Fact]
    public void FocusAt_frames_the_deepest_level_without_writing_children()
    {
        var fixture = new Fixture();
        var d = Summary("D", "Region D", 4, -10, -10, 10, 10);
        fixture.Index.Add(d, [Ring(4, -10, -10, 10, 10)]);
        fixture.Index.FindContainingResult = [d];

        fixture.Service.FocusAt(new GeoPoint(0, 0), Size);

        fixture.Writer.ResolveChildrenCallCount.Should().Be(0);
        var state = fixture.MapState.Current;
        state.Focus.Should().Equal(new FocusedRegion("D", "Region D", 4, d.Bounds));
        state.Status.Should().Contain("deepest extracted level");
        fixture.Overlay.Label.Should().Be("Region D");
    }

    [Fact]
    public void FocusAt_refits_and_reports_an_already_focused_region()
    {
        var fixture = new Fixture();
        var a = Summary("A", "Region A", 2, -80, -50, 80, 50);
        fixture.Index.Add(a, [Ring(2, -80, -50, 80, 50)]);
        fixture.Index.FindContainingResult = [a];

        fixture.Service.FocusAt(new GeoPoint(0, 0), Size);
        fixture.Service.FocusAt(new GeoPoint(0, 0), Size);

        var state = fixture.MapState.Current;
        state.Focus.Should().HaveCount(1);
        state.Status.Should().Contain("Already focused on Region A");
    }

    [Fact]
    public void FocusAt_pushes_a_visible_child_onto_the_stack()
    {
        var fixture = new Fixture();
        var a = Summary("A", "Region A", 2, -80, -50, 80, 50);
        var b = Summary("B", "Region B", 4, -80, -50, 0, 0);
        fixture.Index.Add(a, [Ring(2, -80, -50, 80, 50)]);
        fixture.Index.Add(b, [Ring(4, -80, -50, 0, 0)]);
        fixture.Writer.AddChildren("A", 4, [b]);
        fixture.Index.FindContainingResult = [a];

        fixture.Service.FocusAt(new GeoPoint(0, 0), Size);
        fixture.Index.FindContainingResult = [b];
        fixture.Service.FocusAt(new GeoPoint(-50, -25), Size);

        var state = fixture.MapState.Current;
        state.Focus.Should().HaveCount(2);
        state.Focus[1].Id.Should().Be("B");
        state.Status.Should().Contain("deepest extracted level");
        fixture.Overlay.Label.Should().Be("Region B");
    }

    [Fact]
    public void Blur_pops_to_the_parent_and_refits_it()
    {
        var fixture = new Fixture();
        var a = Summary("A", "Region A", 2, -80, -50, 80, 50);
        var b = Summary("B", "Region B", 4, -80, -50, 0, 0);
        fixture.Index.Add(a, [Ring(2, -80, -50, 80, 50)]);
        fixture.Index.Add(b, [Ring(4, -80, -50, 0, 0)]);
        fixture.Writer.AddChildren("A", 4, [b]);
        fixture.Index.FindContainingResult = [a];
        fixture.Service.FocusAt(new GeoPoint(0, 0), Size);
        fixture.Index.FindContainingResult = [b];
        fixture.Service.FocusAt(new GeoPoint(-50, -25), Size);

        fixture.Service.Blur(Size).Should().BeTrue();

        var state = fixture.MapState.Current;
        state.Focus.Should().Equal(new FocusedRegion("A", "Region A", 2, a.Bounds));
        state.Status.Should().Contain("Region A");
        fixture.Overlay.Label.Should().Be("Region A");
    }

    [Fact]
    public void Blur_pops_to_empty_and_restores_the_root_geometry()
    {
        var fixture = new Fixture();
        var a = Summary("A", "Region A", 2, -80, -50, 80, 50);
        fixture.Index.Add(a, [Ring(2, -80, -50, 80, 50)]);
        fixture.Index.FindContainingResult = [a];
        var root = fixture.Geometry.Geometry;

        fixture.Service.FocusAt(new GeoPoint(0, 0), Size);
        fixture.Service.Blur(Size).Should().BeTrue();

        fixture.MapState.Current.Focus.Should().BeEmpty();
        fixture.Overlay.Polylines.Should().BeEmpty();
        fixture.Overlay.Label.Should().BeNull();
        fixture.Geometry.Geometry.Should().Be(root);
        fixture.Geometry.AvailableAdminLevels.Should().Equal(2);
    }

    [Fact]
    public void Blur_returns_false_when_the_stack_is_already_empty()
    {
        var fixture = new Fixture();

        fixture.Service.Blur(Size).Should().BeFalse();
    }

    [Fact]
    public void A_geodata_reload_clears_the_stack_and_the_overlay()
    {
        var fixture = new Fixture();
        var a = Summary("A", "Region A", 2, -80, -50, 80, 50);
        fixture.Index.Add(a, [Ring(2, -80, -50, 80, 50)]);
        fixture.Index.FindContainingResult = [a];
        fixture.Service.FocusAt(new GeoPoint(0, 0), Size);
        fixture.Overlay.Label.Should().Be("Region A");

        fixture.Geometry.Set(BorderGeometry.Empty, []);

        fixture.Service.Blur(Size).Should().BeFalse();
        fixture.Overlay.Polylines.Should().BeEmpty();
        fixture.MapState.Current.Focus.Should().BeEmpty();
    }

    [Fact]
    public void A_stale_completion_does_not_publish_after_a_newer_focus()
    {
        var fixture = new Fixture();
        var a = Summary("A", "Region A", 2, -80, -50, 80, 50);
        var b = Summary("B", "Region B", 2, 20, 20, 40, 40);
        fixture.Index.Add(a, [Ring(2, -80, -50, 80, 50)]);
        fixture.Index.Add(b, [Ring(2, 20, 20, 40, 40)]);

        var gate = new TaskCompletionSource<IReadOnlyList<RegionSummary>>();
        fixture.Index.FindContainingOverride = (_, _, _) => gate.Task;

        fixture.Service.FocusAt(new GeoPoint(0, 0), Size);
        fixture.Index.FindContainingOverride = null;
        fixture.Index.FindContainingResult = [b];
        fixture.Service.FocusAt(new GeoPoint(30, 30), Size);

        gate.SetResult([a]);

        fixture
            .MapState.Current.Focus.Should()
            .Equal(new FocusedRegion("B", "Region B", 2, b.Bounds));
    }

    [Fact]
    public void A_failed_load_publishes_a_readable_status()
    {
        var fixture = new Fixture();
        fixture.Index.FindContainingOverride = (_, _, _) =>
            Task.FromException<IReadOnlyList<RegionSummary>>(new InvalidOperationException("boom"));

        fixture.Service.FocusAt(new GeoPoint(0, 0), Size);

        fixture.MapState.Current.Status.Should().Contain("Focus failed").And.Contain("boom");
    }

    private static MapViewState State(MapCamera camera) =>
        new(
            camera,
            camera.CenterGeo,
            camera.Viewport(Size.Width, Size.Height).WorldBounds(Size.Width, Size.Height),
            string.Empty,
            [],
            []
        );

    private static RegionSummary Summary(
        string id,
        string name,
        int level,
        double minLon,
        double minLat,
        double maxLon,
        double maxLat
    ) => new(id, name, null, level, new GeoBounds(minLon, minLat, maxLon, maxLat), null, 1, 5);

    private static GeoPolyline Ring(
        int level,
        double minLon,
        double minLat,
        double maxLon,
        double maxLat
    ) =>
        new(
            [
                new GeoPoint(minLon, minLat),
                new GeoPoint(maxLon, minLat),
                new GeoPoint(maxLon, maxLat),
                new GeoPoint(minLon, maxLat),
                new GeoPoint(minLon, minLat),
            ],
            Closed: true,
            AdminLevel: level
        );

    private static BorderGeometry Root() =>
        BorderGeometry.FromGeo([Ring(2, -170, -80, 170, 80)], new GeoBounds(-170, -80, 170, 80));

    private sealed class Fixture
    {
        public Fixture()
        {
            Geometry = new BorderGeometryStore();
            Geometry.Set(Root(), [2]);
            MapState = new MapStateStore();
            Overlay = new FocusOverlayStore();
            Settings = new TestSettingsStore();
            Index = new FakeRegionIndex();
            Writer = new FakeRegionIndexWriter();
            Service = new MapFocusService(
                Index,
                Writer,
                Geometry,
                MapState,
                Overlay,
                Settings,
                NullLogger<MapFocusService>.Instance
            );
        }

        public BorderGeometryStore Geometry { get; }

        public MapStateStore MapState { get; }

        public FocusOverlayStore Overlay { get; }

        public TestSettingsStore Settings { get; }

        public FakeRegionIndex Index { get; }

        public FakeRegionIndexWriter Writer { get; }

        public MapFocusService Service { get; }
    }
}
