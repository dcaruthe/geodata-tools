using FluentAssertions;
using GeoTools.Shared.Application.Geodata;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Infrastructure.Rendering;
using GeoTools.Shared.Presentation.Link;
using GeoTools.Shared.Presentation.Views;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class WindowCommandPumpTests
{
    [Fact]
    public async Task A_navigate_command_changes_the_view()
    {
        var harness = await CreateAsync();

        var pending = harness.Enqueue(new NavigateRequest("1", ViewId.Maintenance));
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.Ok.Should().BeTrue();
        response.ReadPayload<CommandResultDto>()!.ActiveView.Should().Be(ViewId.Maintenance);
        harness.Mediator.ActiveView.Should().Be(ViewId.Maintenance);
    }

    [Fact]
    public async Task Pressing_keys_reaches_the_active_view()
    {
        var harness = await CreateAsync();

        var pending = harness.Enqueue(new PressKeysRequest("1", [WindowKey.Down, WindowKey.Enter]));
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.Ok.Should().BeTrue();
        harness.Router.Keys.Should().Equal(ViewKey.Down, ViewKey.Enter);
    }

    [Fact]
    public async Task A_screenshot_returns_a_png_of_the_requested_size()
    {
        var harness = await CreateAsync();

        var pending = harness.Enqueue(new ScreenshotRequest("1", 120, 90, ScreenshotMode.Scene));
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.Ok.Should().BeTrue(response.Error?.Message);
        var shot = response.ReadPayload<ScreenshotDto>()!;
        shot.Png.Take(4).Should().Equal(0x89, 0x50, 0x4E, 0x47);
        using var bitmap = SKBitmap.Decode(shot.Png);
        bitmap.Width.Should().Be(120);
        bitmap.Height.Should().Be(90);
        shot.Width.Should().Be(120);
        shot.Mode.Should().Be(nameof(ScreenshotMode.Scene));
    }

    [Fact]
    public async Task A_screenshot_defaults_to_the_window_size()
    {
        var harness = await CreateAsync();

        var pending = harness.Enqueue(new ScreenshotRequest("1", null, null, ScreenshotMode.Scene));
        harness.Pump.Drain(new ViewSize(640, 480));
        var shot = (await pending).ReadPayload<ScreenshotDto>()!;

        shot.Width.Should().Be(640);
        shot.Height.Should().Be(480);
    }

    [Fact]
    public async Task An_activate_command_runs_the_row_action()
    {
        var harness = await CreateAsync();

        var pending = harness.Enqueue(
            new MaintenanceCommandRequest(
                "1",
                MaintenanceOperation.Activate,
                "displayLevels",
                0,
                false
            )
        );
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.ReadPayload<CommandResultDto>()!.Accepted.Should().BeTrue();
        harness.Settings.Map.AdminLevels.Should().Equal(2, 4);
    }

    [Fact]
    public async Task A_pipeline_command_without_confirm_asks_first()
    {
        var harness = await CreateAsync();

        var pending = harness.Enqueue(
            new PipelineCommandRequest("1", PipelineAction.Repull, false)
        );
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.Ok.Should().BeTrue();
        harness.Bus.Published.Should().BeEmpty();

        var confirmed = harness.Enqueue(
            new PipelineCommandRequest("2", PipelineAction.Repull, true)
        );
        harness.Pump.Drain(new ViewSize(800, 600));
        await confirmed;

        harness.Bus.Published.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_request_that_is_not_a_window_command_is_refused()
    {
        var harness = await CreateAsync();

        var pending = harness.Enqueue(new GetLogsRequest("1", null, 10, null));
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.Ok.Should().BeFalse();
        response.Error!.Code.Should().Be(LinkError.Unsupported);
    }

    [Fact]
    public async Task An_adjust_command_changes_the_row_without_keys()
    {
        var harness = await CreateAsync();

        var pending = harness.Enqueue(
            new MaintenanceCommandRequest(
                "1",
                MaintenanceOperation.Adjust,
                "displayLevels",
                1,
                false
            )
        );
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.ReadPayload<CommandResultDto>()!.Accepted.Should().BeTrue();
        harness.Settings.Map.AdminLevels.Should().Equal(2, 4);
    }

    [Fact]
    public async Task Cancelling_the_pipeline_requests_cancellation()
    {
        var harness = await CreateAsync();

        var pending = harness.Enqueue(
            new PipelineCommandRequest("1", PipelineAction.Cancel, false)
        );
        harness.Pump.Drain(new ViewSize(800, 600));
        await pending;

        harness.Bus.Published.OfType<CancelRequested>().Should().ContainSingle();
    }

    [Fact]
    public async Task A_reindex_pipeline_command_runs_the_reindex_action()
    {
        var harness = await CreateAsync();

        var pending = harness.Enqueue(
            new PipelineCommandRequest("1", PipelineAction.Reindex, false)
        );
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.Ok.Should().BeTrue(response.Error?.Message);
        harness.Bus.Published.OfType<RebuildRequested>().Should().ContainSingle();
        harness
            .Bus.Published.OfType<RebuildRequested>()
            .Single()
            .Scope.Should()
            .Be(RebuildScope.RegionIndex);
    }

    [Fact]
    public async Task A_tiny_capture_size_is_clamped()
    {
        var harness = await CreateAsync();

        var pending = harness.Enqueue(new ScreenshotRequest("1", 10, 10, ScreenshotMode.Scene));
        harness.Pump.Drain(new ViewSize(800, 600));
        var shot = (await pending).ReadPayload<ScreenshotDto>()!;

        shot.Width.Should().Be(64);
        shot.Height.Should().Be(64);
    }

    [Fact]
    public async Task A_scene_failure_is_reported_as_internal()
    {
        var harness = await CreateAsync(failingScene: true);

        var pending = harness.Enqueue(new ScreenshotRequest("1", 100, 100, ScreenshotMode.Scene));
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.Ok.Should().BeFalse();
        response.Error!.Code.Should().Be(LinkError.Internal);
    }

    [Fact]
    public async Task A_focus_point_command_reaches_the_controller()
    {
        var harness = await CreateAsync();
        GeoPoint? captured = null;
        harness.Index.FindContainingOverride = (point, _, _) =>
        {
            captured = point;
            return Task.FromResult<IReadOnlyList<RegionSummary>>([]);
        };

        var pending = harness.Enqueue(new FocusPointRequest("1", 10.45, 51.16));
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.Ok.Should().BeTrue(response.Error?.Message);
        response
            .ReadPayload<CommandResultDto>()!
            .Status.Should()
            .Contain("Focusing the region at 10.45");
        captured.Should().Be(new GeoPoint(10.45, 51.16));
    }

    [Fact]
    public async Task A_focus_region_command_reaches_the_controller()
    {
        var harness = await CreateAsync();
        string? captured = null;
        harness.Index.GetRegionOverride = (id, _) =>
        {
            captured = id;
            return Task.FromResult<RegionDetail?>(null);
        };

        var pending = harness.Enqueue(new FocusRegionRequest("1", "region:51477"));
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.Ok.Should().BeTrue(response.Error?.Message);
        response
            .ReadPayload<CommandResultDto>()!
            .Status.Should()
            .Contain("Focusing region region:51477");
        captured.Should().Be("region:51477");
    }

    [Fact]
    public async Task A_blur_command_fails_when_nothing_is_focused()
    {
        var harness = await CreateAsync();

        var pending = harness.Enqueue(new BlurFocusRequest("1"));
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.Ok.Should().BeFalse();
        response.Error!.Code.Should().Be(LinkError.InvalidRequest);
    }

    [Fact]
    public async Task A_blur_command_pops_after_a_focus()
    {
        var harness = await CreateAsync();
        var a = Summary("A", "Region A", 2, -80, -50, 80, 50);
        harness.Index.Add(a, [Ring(2, -80, -50, 80, 50)]);
        harness.Index.FindContainingResult = [a];

        var focus = harness.Enqueue(new FocusPointRequest("1", 0, 0));
        harness.Pump.Drain(new ViewSize(800, 600));
        await focus;

        var blur = harness.Enqueue(new BlurFocusRequest("2"));
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await blur;

        response.Ok.Should().BeTrue(response.Error?.Message);
        response.ReadPayload<CommandResultDto>()!.Status.Should().Contain("Popped the focus stack");
        harness.MapState.Current.Focus.Should().BeEmpty();
    }

    [Fact]
    public async Task A_pan_command_reaches_the_controller()
    {
        var harness = await CreateAsync();
        var before = harness.MapState.Version;

        var pending = harness.Enqueue(new PanRequest("1", 64, 0));
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.Ok.Should().BeTrue(response.Error?.Message);
        response.ReadPayload<CommandResultDto>()!.Status.Should().Contain("Panned by 64,0 px");
        harness.MapState.Version.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task A_zoom_command_changes_the_camera()
    {
        var harness = await CreateAsync();
        var before = harness.MapState.Current.Camera.Scale;

        var pending = harness.Enqueue(new ZoomRequest("1", 1));
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.Ok.Should().BeTrue(response.Error?.Message);
        response.ReadPayload<CommandResultDto>()!.Status.Should().Contain("Zoomed in");
        harness.MapState.Current.Camera.Scale.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task A_zoom_out_command_shrinks_the_camera()
    {
        var harness = await CreateAsync();
        harness.Map.ZoomByDirection(1, new ViewSize(800, 600));
        var before = harness.MapState.Current.Camera.Scale;

        var pending = harness.Enqueue(new ZoomRequest("1", -1));
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.Ok.Should().BeTrue(response.Error?.Message);
        response.ReadPayload<CommandResultDto>()!.Status.Should().Contain("Zoomed out");
        harness.MapState.Current.Camera.Scale.Should().BeLessThan(before);
    }

    [Fact]
    public async Task An_invalid_zoom_direction_is_refused()
    {
        var harness = await CreateAsync();

        var pending = harness.Enqueue(new ZoomRequest("1", 0));
        harness.Pump.Drain(new ViewSize(800, 600));
        var response = await pending;

        response.Ok.Should().BeFalse();
        response.Error!.Code.Should().Be(LinkError.InvalidRequest);
    }

    private static async Task<Harness> CreateAsync(bool failingScene = false)
    {
        var settings = new TestSettingsStore();
        var bus = new FakeMessageBus();
        var mediator = new FakeNavigationMediator();
        var geometry = new BorderGeometryStore();
        geometry.Set(Borders(), [2]);
        var mapState = new MapStateStore();
        var overlay = new FocusOverlayStore();
        var index = new FakeRegionIndex();
        var writer = new FakeRegionIndexWriter();
        var focus = new MapFocusService(
            index,
            writer,
            geometry,
            mapState,
            overlay,
            settings,
            NullLogger<MapFocusService>.Instance
        );
        var map = new MapController(
            mapState,
            geometry,
            settings,
            focus,
            NullLogger<MapController>.Instance
        );
        map.EnsureInitialised(new ViewSize(800, 600));

        var controller = new MaintenanceController(
            settings,
            new FakeJobStore(),
            new FakeArtifactStore(),
            new ProgressStore(),
            geometry,
            index,
            writer,
            bus,
            Microsoft.Extensions.Options.Options.Create(
                new GeodataOptions { Regions = ["africa"] }
            ),
            mediator,
            TimeProvider.System,
            NullLogger<MaintenanceController>.Instance
        );
        await controller.AttachAsync(bus);

        var inbox = new WindowCommandInbox();
        var router = new FakeInputRouter();
        var pump = new WindowCommandPump(
            inbox,
            mediator,
            router,
            new FakeSceneSource(failingScene),
            controller,
            map,
            new SkiaOffscreenSurface(),
            TimeProvider.System,
            NullLogger<WindowCommandPump>.Instance
        );

        return new Harness(
            inbox,
            pump,
            mediator,
            router,
            settings,
            bus,
            mapState,
            geometry,
            index,
            map
        );
    }

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

    private static BorderGeometry Borders() =>
        BorderGeometry.FromGeo([Ring(2, -170, -80, 170, 80)], new GeoBounds(-170, -80, 170, 80));

    private sealed record Harness(
        WindowCommandInbox Inbox,
        WindowCommandPump Pump,
        FakeNavigationMediator Mediator,
        FakeInputRouter Router,
        TestSettingsStore Settings,
        FakeMessageBus Bus,
        MapStateStore MapState,
        BorderGeometryStore Geometry,
        FakeRegionIndex Index,
        MapController Map
    )
    {
        public Task<LinkResponse> Enqueue(LinkRequest request) =>
            Inbox.EnqueueAsync(request, TimeSpan.FromSeconds(2), CancellationToken.None);
    }

    private sealed class FakeInputRouter : IViewInputRouter
    {
        public List<ViewKey> Keys { get; } = [];

        public bool Handle(ViewKey key, ViewSize size)
        {
            Keys.Add(key);
            return true;
        }
    }

    private sealed class FakeSceneSource(bool failing) : ISceneSource
    {
        public SceneDto Compose(ViewSize size) =>
            failing
                ? throw new InvalidOperationException("scene failed")
                : new SceneDto([], new Color(10, 12, 18));
    }
}
