using FluentAssertions;
using GeoTools.Shared.Application.Geodata;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Presentation.Views;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class MaintenanceControllerTests
{
    [Fact]
    public async Task Attach_loads_the_rows_from_the_stores()
    {
        var (controller, _, _) = Create();

        await controller.AttachAsync(new FakeMessageBus());

        controller.Snapshot.Rows.Should().NotBeEmpty();
        controller
            .Snapshot.Rows.Should()
            .Contain(row => row.Id == "artifact:africa" && row.Value.Contains("GB"));
    }

    [Fact]
    public async Task Adjusting_display_levels_updates_the_settings_store()
    {
        var (controller, settings, _) = Create();
        await controller.AttachAsync(new FakeMessageBus());
        Select(controller, "displayLevels");

        controller.Handle(ViewKey.Right);

        settings.Map.AdminLevels.Should().Equal(2, 4);
        controller.Snapshot.Status.Should().Be("Saved.");
    }

    [Fact]
    public async Task Toggling_a_flag_updates_the_settings_store()
    {
        var (controller, settings, _) = Create();
        await controller.AttachAsync(new FakeMessageBus());
        Select(controller, "flag:MapGrid");

        controller.Handle(ViewKey.Enter);

        settings.IsEnabled(FeatureFlag.MapGrid).Should().BeFalse();
    }

    [Fact]
    public async Task A_destructive_action_asks_for_confirmation_and_then_publishes()
    {
        var (controller, _, bus) = Create();
        await controller.AttachAsync(new FakeMessageBus());
        Select(controller, "action:repull");

        controller.Handle(ViewKey.Enter);
        controller.Snapshot.PendingConfirmation.Should().Be("action:repull");
        bus.Published.Should().BeEmpty();

        controller.Handle(ViewKey.Enter);

        controller.Snapshot.PendingConfirmation.Should().BeNull();
        bus.Published.OfType<RebuildRequested>().Should().ContainSingle();
        bus.Published.OfType<RebuildRequested>().Single().Force.Should().BeTrue();
    }

    [Fact]
    public async Task Backspace_requests_cancellation()
    {
        var (controller, _, bus) = Create();
        await controller.AttachAsync(new FakeMessageBus());

        controller.Handle(ViewKey.Backspace);

        bus.Published.OfType<CancelRequested>().Should().ContainSingle();
    }

    [Fact]
    public async Task A_non_selectable_row_is_not_activated()
    {
        var (controller, _, bus) = Create();
        await controller.AttachAsync(new FakeMessageBus());
        controller.Handle(ViewKey.Down);
        controller.Handle(ViewKey.Down);

        var handled = controller.Handle(ViewKey.Enter);

        handled.Should().BeTrue();
        bus.Published.Should().BeEmpty();
    }

    private static void Select(MaintenanceController controller, string id)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (controller.Snapshot.Selected?.Id == id)
                return;

            controller.Handle(ViewKey.Down);
        }

        throw new InvalidOperationException($"Could not select {id}.");
    }

    [Fact]
    public async Task Tolerance_adjustment_saves_and_queues_a_cache_rebuild()
    {
        var (controller, settings, bus) = Create();
        await controller.AttachAsync(new FakeMessageBus());
        Select(controller, "tolerance");

        controller.Handle(ViewKey.Right);

        settings.Geodata.SimplifyToleranceDegrees.Should().Be(0.005);
        controller.Snapshot.Status.Should().Be("Saved.");
        bus.Published.OfType<RebuildRequested>()
            .Should()
            .Contain(request => request.Scope == RebuildScope.GeometryCache);
    }

    [Fact]
    public async Task Extract_levels_cycle_through_the_presets()
    {
        var (controller, settings, _) = Create();
        await controller.AttachAsync(new FakeMessageBus());
        Select(controller, "extractLevels");

        controller.Handle(ViewKey.Right);

        settings.Geodata.ExtractAdminLevels.Should().Equal(2, 4, 6);
    }

    [Fact]
    public async Task The_update_check_queues_a_conditional_pull()
    {
        var (controller, _, bus) = Create();
        await controller.AttachAsync(new FakeMessageBus());
        Select(controller, "action:check");

        controller.Handle(ViewKey.Enter);

        var request = bus.Published.OfType<RebuildRequested>().Single();
        request.Scope.Should().Be(RebuildScope.Pull);
        request.Force.Should().BeFalse();
    }

    [Fact]
    public async Task Rebuilding_borders_confirms_then_queues_a_forced_extract()
    {
        var (controller, _, bus) = Create();
        await controller.AttachAsync(new FakeMessageBus());
        Select(controller, "action:extract");

        controller.Handle(ViewKey.Enter);
        controller.Handle(ViewKey.Enter);

        var request = bus.Published.OfType<RebuildRequested>().Single();
        request.Scope.Should().Be(RebuildScope.Extract);
        request.Force.Should().BeTrue();
    }

    [Fact]
    public async Task Rebuilding_the_cache_queues_the_cache_scope()
    {
        var (controller, _, bus) = Create();
        await controller.AttachAsync(new FakeMessageBus());
        Select(controller, "action:cache");

        controller.Handle(ViewKey.Enter);

        bus.Published.OfType<RebuildRequested>()
            .Single()
            .Scope.Should()
            .Be(RebuildScope.GeometryCache);
    }

    [Fact]
    public async Task Retrying_resets_the_known_subscriptions()
    {
        var (controller, _, bus) = Create();
        await controller.AttachAsync(new FakeMessageBus());
        Select(controller, "action:retry");

        controller.Handle(ViewKey.Enter);

        bus.ResetSubscriptions.Should().Contain(["bootstrap-projector", "geodata-worker"]);
        controller.Snapshot.Status.Should().Be("Failed events will be retried.");
    }

    [Fact]
    public async Task Resetting_settings_confirms_then_reports_saved()
    {
        var (controller, _, _) = Create();
        await controller.AttachAsync(new FakeMessageBus());
        Select(controller, "action:reset");

        controller.Handle(ViewKey.Enter);
        controller.Handle(ViewKey.Enter);

        controller.Snapshot.Status.Should().Be("Saved.");
    }

    [Fact]
    public async Task Cancelling_is_enabled_while_a_run_is_active()
    {
        var (controller, _, bus) = Create(running: true);
        await controller.AttachAsync(new FakeMessageBus());
        Select(controller, "action:cancel");

        controller.Handle(ViewKey.Enter);

        bus.Published.OfType<CancelRequested>().Should().ContainSingle();
    }

    [Fact]
    public async Task Disabled_actions_are_ignored_while_a_run_is_active()
    {
        var (controller, _, bus) = Create(running: true);
        await controller.AttachAsync(new FakeMessageBus());
        Select(controller, "action:repull");

        controller.Handle(ViewKey.Enter);

        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Selecting_a_row_by_id_does_not_need_key_presses()
    {
        var (controller, _, _) = Create();
        await controller.AttachAsync(new FakeMessageBus());

        var result = controller.SelectRow("tolerance");

        result.Accepted.Should().BeTrue();
        controller.Snapshot.Selected?.Id.Should().Be("tolerance");
    }

    [Fact]
    public async Task Adjusting_by_row_id_saves_the_setting()
    {
        var (controller, settings, _) = Create();
        await controller.AttachAsync(new FakeMessageBus());

        var result = controller.AdjustRow("displayLevels", 1);

        result.Accepted.Should().BeTrue();
        settings.Map.AdminLevels.Should().Equal(2, 4);
    }

    [Fact]
    public async Task A_destructive_action_needs_confirm_and_then_publishes()
    {
        var (controller, _, bus) = Create();
        await controller.AttachAsync(new FakeMessageBus());

        var pending = controller.ActivateRow("action:repull", confirm: false);

        pending.Accepted.Should().BeTrue();
        pending.PendingConfirmation.Should().Be("action:repull");
        bus.Published.Should().BeEmpty();

        var confirmed = controller.ActivateRow("action:repull", confirm: true);

        confirmed.Accepted.Should().BeTrue();
        confirmed.PendingConfirmation.Should().BeNull();
        bus.Published.OfType<RebuildRequested>().Should().ContainSingle();
    }

    [Fact]
    public async Task An_unknown_row_is_rejected()
    {
        var (controller, _, _) = Create();
        await controller.AttachAsync(new FakeMessageBus());

        controller.SelectRow("nope").Accepted.Should().BeFalse();
        controller.AdjustRow("nope", 1).Accepted.Should().BeFalse();
        controller.ActivateRow("nope", confirm: true).Accepted.Should().BeFalse();
    }

    [Fact]
    public async Task A_disabled_row_is_rejected()
    {
        var (controller, _, _) = Create(running: true);
        await controller.AttachAsync(new FakeMessageBus());

        var result = controller.ActivateRow("action:repull", confirm: true);

        result.Accepted.Should().BeFalse();
        result.Error.Should().Contain("disabled");
    }

    [Fact]
    public async Task Cancelling_by_command_publishes_a_cancel()
    {
        var (controller, _, bus) = Create(running: true);
        await controller.AttachAsync(new FakeMessageBus());

        controller.CancelRun().Accepted.Should().BeTrue();

        bus.Published.OfType<CancelRequested>().Should().ContainSingle();
    }

    private static (
        MaintenanceController Controller,
        TestSettingsStore Settings,
        FakeMessageBus Bus
    ) Create(bool running = false)
    {
        var settings = new TestSettingsStore();
        var progress = new ProgressStore();
        if (running)
        {
            progress.Set(
                BootstrapProgressSnapshot.Idle with
                {
                    Phase = BootstrapPhase.Running,
                    Label = "Extracting africa",
                }
            );
        }

        var jobs = new FakeJobStore();
        var artifacts = new FakeArtifactStore();
        artifacts.Records.Add(
            new ArtifactRecord(
                new ArtifactKey("pbf:africa"),
                ArtifactKind.Pbf,
                "africa",
                "/tmp/africa.pbf",
                7_500_000_000,
                DateTimeOffset.UnixEpoch,
                "etag",
                [],
                null,
                ArtifactStatus.Current,
                DateTimeOffset.UnixEpoch
            )
        );
        var bus = new FakeMessageBus();
        var controller = new MaintenanceController(
            settings,
            jobs,
            artifacts,
            progress,
            new BorderGeometryStore(),
            bus,
            Microsoft.Extensions.Options.Options.Create(
                new GeodataOptions { Regions = ["africa"] }
            ),
            new FakeNavigationMediator(),
            TimeProvider.System,
            NullLogger<MaintenanceController>.Instance
        );

        return (controller, settings, bus);
    }
}
