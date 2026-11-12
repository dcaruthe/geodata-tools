using FluentAssertions;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Presentation.Views;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class MaintenanceControllerEdgeTests
{
    [Fact]
    public async Task Up_and_left_change_the_selection_and_value()
    {
        var fixture = Create();
        await fixture.Controller.AttachAsync(new FakeMessageBus());
        Select(fixture.Controller, "tolerance");

        fixture.Controller.Handle(ViewKey.Left);
        fixture.Settings.Geodata.SimplifyToleranceDegrees.Should().Be(0.001);

        fixture.Controller.Handle(ViewKey.Up);
        fixture.Controller.Snapshot.Selected!.Id.Should().Be("displayLevels");
    }

    [Fact]
    public async Task An_unknown_key_is_not_handled()
    {
        var fixture = Create();
        await fixture.Controller.AttachAsync(new FakeMessageBus());

        fixture.Controller.Handle((ViewKey)99).Should().BeFalse();
    }

    [Fact]
    public async Task A_pending_confirmation_survives_a_refresh()
    {
        var fixture = Create();
        await fixture.Controller.AttachAsync(new FakeMessageBus());
        Select(fixture.Controller, "action:repull");
        fixture.Controller.Handle(ViewKey.Enter);

        await fixture.Controller.RefreshAsync();

        fixture.Controller.Snapshot.PendingConfirmation.Should().Be("action:repull");
    }

    [Fact]
    public async Task A_subscribed_message_refreshes_an_active_maintenance_view()
    {
        var fixture = Create();
        var bus = new FakeMessageBus();
        await fixture.Controller.AttachAsync(bus);
        fixture.Mediator.NavigateTo(ViewId.Maintenance);
        fixture.Artifacts.Records.Add(Artifact());

        await bus.DeliverAsync(
            MessageTopics.FeatureFlagChanged,
            new FeatureFlagChanged(FeatureFlag.MapGrid, false, DateTimeOffset.UnixEpoch)
        );

        await WaitForAsync(
            () => fixture.Controller.Snapshot.Rows.Any(row => row.Id == "artifact:africa"),
            TimeSpan.FromSeconds(5)
        );
    }

    [Fact]
    public async Task Navigating_to_maintenance_refreshes_the_rows()
    {
        var fixture = Create();
        await fixture.Controller.AttachAsync(new FakeMessageBus());
        fixture.Artifacts.Records.Add(Artifact());

        fixture.Mediator.NavigateTo(ViewId.Maintenance);

        await WaitForAsync(
            () => fixture.Controller.Snapshot.Rows.Any(row => row.Id == "artifact:africa"),
            TimeSpan.FromSeconds(5)
        );
    }

    [Fact]
    public async Task A_failed_refresh_does_not_throw()
    {
        var fixture = Create();
        fixture.Artifacts.ThrowOnList = true;

        var act = async () => await fixture.Controller.AttachAsync(new FakeMessageBus());

        await act.Should().NotThrowAsync();
        fixture.Controller.Snapshot.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task A_cancelled_refresh_does_not_throw()
    {
        var fixture = Create();
        fixture.Artifacts.CancelOnList = true;

        var act = async () => await fixture.Controller.AttachAsync(new FakeMessageBus());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_failing_setting_is_reported()
    {
        var fixture = Create();
        await fixture.Controller.AttachAsync(new FakeMessageBus());
        Select(fixture.Controller, "displayLevels");
        fixture.Settings.ThrowOnSet = true;

        fixture.Controller.Handle(ViewKey.Right);

        fixture.Controller.Snapshot.Status.Should().Contain("Could not save");
    }

    [Fact]
    public async Task A_failing_publish_is_reported()
    {
        var fixture = Create();
        await fixture.Controller.AttachAsync(new FakeMessageBus());
        Select(fixture.Controller, "action:check");
        fixture.Bus.ThrowOnPublish = true;

        fixture.Controller.Handle(ViewKey.Enter);

        fixture.Controller.Snapshot.Status.Should().Contain("Could not queue");
    }

    [Fact]
    public async Task A_failing_cancel_is_reported()
    {
        var fixture = Create();
        await fixture.Controller.AttachAsync(new FakeMessageBus());
        fixture.Bus.ThrowOnPublish = true;

        fixture.Controller.Handle(ViewKey.Backspace);

        fixture.Controller.Snapshot.Status.Should().Contain("Could not cancel");
    }

    [Fact]
    public async Task A_failing_retry_is_reported()
    {
        var fixture = Create();
        await fixture.Controller.AttachAsync(new FakeMessageBus());
        Select(fixture.Controller, "action:retry");
        fixture.Bus.ThrowOnReset = true;

        fixture.Controller.Handle(ViewKey.Enter);

        fixture.Controller.Snapshot.Status.Should().Contain("Could not retry");
    }

    [Fact]
    public async Task A_custom_level_set_cycles_to_the_first_preset()
    {
        var fixture = Create();
        await fixture.Controller.AttachAsync(new FakeMessageBus());
        fixture.Settings.Map = new MapSettings([3]);
        Select(fixture.Controller, "displayLevels");

        fixture.Controller.Handle(ViewKey.Right);

        fixture.Settings.Map.AdminLevels.Should().Equal(2);
    }

    [Fact]
    public async Task A_custom_tolerance_cycles_from_the_nearest_preset()
    {
        var fixture = Create();
        await fixture.Controller.AttachAsync(new FakeMessageBus());
        fixture.Settings.Geodata = fixture.Settings.Geodata with
        {
            SimplifyToleranceDegrees = 0.003,
        };
        Select(fixture.Controller, "tolerance");

        fixture.Controller.Handle(ViewKey.Right);

        fixture.Settings.Geodata.SimplifyToleranceDegrees.Should().Be(0.005);
    }

    [Fact]
    public async Task A_disabled_row_ignores_adjustment()
    {
        var fixture = Create(running: true);
        await fixture.Controller.AttachAsync(new FakeMessageBus());
        Select(fixture.Controller, "action:repull");

        fixture.Controller.Handle(ViewKey.Right);

        fixture.Bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Enter_on_an_info_row_is_not_handled()
    {
        var fixture = Create();
        await fixture.Controller.AttachAsync(new FakeMessageBus());
        Select(fixture.Controller, "artifact:africa");

        fixture.Controller.Handle(ViewKey.Enter).Should().BeFalse();
    }

    [Fact]
    public async Task Home_and_end_jump_to_the_first_and_last_selectable_rows()
    {
        var fixture = Create();
        await fixture.Controller.AttachAsync(new FakeMessageBus());

        fixture.Controller.Handle(ViewKey.End);
        fixture.Controller.Snapshot.Selected!.Id.Should().Be("action:reset");

        fixture.Controller.Handle(ViewKey.Home);
        fixture.Controller.Snapshot.Selected!.Id.Should().Be("displayLevels");
    }

    [Fact]
    public async Task Any_other_key_cancels_a_pending_confirmation()
    {
        var fixture = Create();
        await fixture.Controller.AttachAsync(new FakeMessageBus());
        Select(fixture.Controller, "action:repull");
        fixture.Controller.Handle(ViewKey.Enter);

        fixture.Controller.Handle(ViewKey.Down);

        fixture.Controller.Snapshot.PendingConfirmation.Should().BeNull();
    }

    [Fact]
    public async Task Leaving_maintenance_cancels_a_pending_confirmation()
    {
        var fixture = Create();
        await fixture.Controller.AttachAsync(new FakeMessageBus());
        fixture.Mediator.NavigateTo(ViewId.Maintenance);
        Select(fixture.Controller, "action:repull");
        fixture.Controller.Handle(ViewKey.Enter);

        fixture.Mediator.NavigateTo(ViewId.Map);

        fixture.Controller.Snapshot.PendingConfirmation.Should().BeNull();
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

    private static ArtifactRecord Artifact() =>
        new(
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
        );

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(50);
        }

        throw new TimeoutException("The condition was not met before the timeout.");
    }

    private static Fixture Create(bool running = false)
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

        var artifacts = new FakeArtifactStore();
        artifacts.Records.Add(Artifact());
        var bus = new FakeMessageBus();
        var mediator = new FakeNavigationMediator();
        var controller = new MaintenanceController(
            settings,
            new FakeJobStore(),
            artifacts,
            progress,
            new BorderGeometryStore(),
            bus,
            Microsoft.Extensions.Options.Options.Create(
                new GeodataOptions { Regions = ["africa"] }
            ),
            mediator,
            TimeProvider.System,
            NullLogger<MaintenanceController>.Instance
        );

        return new Fixture(controller, settings, bus, mediator, artifacts);
    }

    private sealed record Fixture(
        MaintenanceController Controller,
        TestSettingsStore Settings,
        FakeMessageBus Bus,
        FakeNavigationMediator Mediator,
        FakeArtifactStore Artifacts
    );
}
