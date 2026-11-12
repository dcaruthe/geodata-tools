using FluentAssertions;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Infrastructure.State;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class ArtifactStoreTests
{
    [Fact]
    public async Task Artifacts_round_trip_and_filter_by_kind()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new ArtifactStore(database.Factory);
        var record = new ArtifactRecord(
            new ArtifactKey("pbf:africa"),
            ArtifactKind.Pbf,
            "africa",
            "/tmp/africa-latest.osm.pbf",
            123,
            DateTimeOffset.UnixEpoch.AddDays(2),
            "etag",
            [2, 4],
            new GeoBounds(-20, -40, 60, 40),
            ArtifactStatus.Current,
            DateTimeOffset.UnixEpoch.AddDays(3)
        );

        await store.PutAsync(record);

        var loaded = await store.GetAsync(new ArtifactKey("pbf:africa"));
        loaded.Should().BeEquivalentTo(record);
        (await store.ListAsync(ArtifactKind.Pbf)).Should().ContainSingle();
        (await store.ListAsync(ArtifactKind.Borders)).Should().BeEmpty();

        await store.DeleteAsync(new ArtifactKey("pbf:africa"));
        (await store.GetAsync(new ArtifactKey("pbf:africa"))).Should().BeNull();
    }

    [Fact]
    public async Task Putting_the_same_key_updates_the_row()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new ArtifactStore(database.Factory);
        var key = new ArtifactKey("borders:africa");
        var first = new ArtifactRecord(
            key,
            ArtifactKind.Borders,
            "africa",
            "/tmp/a.geojsonseq",
            10,
            DateTimeOffset.UnixEpoch,
            "2,4",
            [2, 4],
            null,
            ArtifactStatus.Current,
            DateTimeOffset.UnixEpoch
        );

        await store.PutAsync(first);
        await store.PutAsync(first with { SizeBytes = 20, Status = ArtifactStatus.Stale });

        var loaded = await store.GetAsync(key);
        loaded!.SizeBytes.Should().Be(20);
        loaded.Status.Should().Be(ArtifactStatus.Stale);
        (await store.ListAsync()).Should().ContainSingle();
    }
}

public sealed class GeometryCacheStoreTests
{
    [Fact]
    public async Task Cache_entries_round_trip_and_prune()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new GeometryCacheStore(database.Factory);
        var entry = new GeometryCacheEntry(
            "key-a",
            "africa",
            [2, 4],
            0.002,
            1,
            42,
            new GeoBounds(-1, -2, 3, 4),
            [1, 2, 3],
            DateTimeOffset.UnixEpoch
        );

        await store.PutAsync(entry);
        (await store.TryGetAsync("key-a")).Should().BeEquivalentTo(entry);

        await store.PutAsync(entry with { Key = "key-b" });
        await store.DeleteExceptAsync(["key-b"]);
        (await store.TryGetAsync("key-a")).Should().BeNull();
        (await store.TryGetAsync("key-b")).Should().NotBeNull();

        await store.ClearAsync();
        (await store.TryGetAsync("key-b")).Should().BeNull();
    }
}

public sealed class JobStoreTests
{
    [Fact]
    public async Task Runs_and_steps_are_projected_in_order()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new JobStore(database.Factory, TimeProvider.System);
        var runId = RunId.New();

        await store.ApplyAsync(
            new BootstrapRunStarted(
                runId,
                "test",
                [new BootstrapStep(new StepKey("load"), "load", "Loading")],
                DateTimeOffset.UnixEpoch
            )
        );

        store.LatestRun!.Status.Should().Be(RunStatus.Running);
        store.LatestSteps.Should().ContainSingle();

        await store.ApplyAsync(
            new BootstrapStepChanged(
                runId,
                new StepKey("load"),
                "load",
                "Loading",
                StepStatus.Running,
                new StepProgress(0.5, 10, 20, 1, 2),
                null,
                DateTimeOffset.UnixEpoch
            )
        );

        store.LatestSteps[0].Progress.Fraction.Should().Be(0.5);
        store.LatestSteps[0].Status.Should().Be(StepStatus.Running);

        await store.ApplyAsync(
            new BootstrapStepChanged(
                runId,
                new StepKey("load"),
                "load",
                "Loading",
                StepStatus.Succeeded,
                new StepProgress(1),
                null,
                DateTimeOffset.UnixEpoch
            )
        );

        await store.ApplyAsync(
            new BootstrapStepChanged(
                runId,
                new StepKey("load"),
                "load",
                "Loading",
                StepStatus.Running,
                new StepProgress(0.1),
                null,
                DateTimeOffset.UnixEpoch
            )
        );

        store.LatestSteps[0].Status.Should().Be(StepStatus.Succeeded);

        await store.ApplyAsync(
            new BootstrapRunFinished(
                runId,
                RunStatus.Succeeded,
                null,
                3,
                DateTimeOffset.UnixEpoch.AddSeconds(3)
            )
        );

        store.LatestRun!.Status.Should().Be(RunStatus.Succeeded);
    }

    [Fact]
    public async Task Reconcile_marks_an_interrupted_run_as_failed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new JobStore(database.Factory, TimeProvider.System);
        var runId = RunId.New();
        await store.ApplyAsync(
            new BootstrapRunStarted(
                runId,
                "test",
                [new BootstrapStep(new StepKey("load"), "load", "Loading")],
                DateTimeOffset.UnixEpoch
            )
        );

        var reopened = new JobStore(database.Factory, TimeProvider.System);
        await reopened.ReconcileAsync();

        reopened.LatestRun!.Status.Should().Be(RunStatus.Failed);
        reopened.LatestRun.Error.Should().Contain("Interrupted");
        reopened.LatestSteps[0].Status.Should().Be(StepStatus.Failed);
    }

    [Fact]
    public async Task Handled_requests_are_recorded_once()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new JobStore(database.Factory, TimeProvider.System);

        (await store.IsRequestHandledAsync("request-1")).Should().BeFalse();
        await store.MarkRequestHandledAsync("request-1");
        (await store.IsRequestHandledAsync("request-1")).Should().BeTrue();
        var act = async () => await store.MarkRequestHandledAsync("request-1");
        await act.Should().NotThrowAsync();
    }
}

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task Defaults_come_from_options()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database, new FakeMessageBus());

        await store.ReloadAsync();

        store.Map.AdminLevels.Should().Equal(2);
        store.Geodata.ExtractAdminLevels.Should().Equal(2, 4);
        store.Geodata.SimplifyToleranceDegrees.Should().Be(0.002);
        store.IsEnabled(FeatureFlag.MapGrid).Should().BeTrue();
        store.Version.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Display_levels_persist_and_publish()
    {
        await using var database = await TestDatabase.CreateAsync();
        var bus = new FakeMessageBus();
        var store = CreateStore(database, bus);

        var result = await store.SetDisplayAdminLevelsAsync([4, 2, 4]);

        result.Accepted.Should().BeTrue();
        store.Map.AdminLevels.Should().Equal(2, 4);
        bus.Published.OfType<SettingsChanged>().Should().ContainSingle();

        var reloaded = CreateStore(database, new FakeMessageBus());
        await reloaded.ReloadAsync();
        reloaded.Map.AdminLevels.Should().Equal(2, 4);
    }

    [Fact]
    public async Task Invalid_settings_are_rejected_without_changing_state()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database, new FakeMessageBus());
        await store.ReloadAsync();

        var display = await store.SetDisplayAdminLevelsAsync([0]);
        var extract = await store.SetExtractAdminLevelsAsync([]);
        var tolerance = await store.SetSimplifyToleranceAsync(-1);

        display.Accepted.Should().BeFalse();
        extract.Accepted.Should().BeFalse();
        tolerance.Accepted.Should().BeFalse();
        store.Map.AdminLevels.Should().Equal(2);
        store.Geodata.ExtractAdminLevels.Should().Equal(2, 4);
        store.Geodata.SimplifyToleranceDegrees.Should().Be(0.002);
    }

    [Fact]
    public async Task Flags_persist_and_publish()
    {
        await using var database = await TestDatabase.CreateAsync();
        var bus = new FakeMessageBus();
        var store = CreateStore(database, bus);
        await store.ReloadAsync();

        var result = await store.SetFlagAsync(FeatureFlag.MapGrid, false);

        result.Accepted.Should().BeTrue();
        store.IsEnabled(FeatureFlag.MapGrid).Should().BeFalse();
        bus.Published.OfType<FeatureFlagChanged>().Should().ContainSingle();

        var reloaded = CreateStore(database, new FakeMessageBus());
        await reloaded.ReloadAsync();
        reloaded.IsEnabled(FeatureFlag.MapGrid).Should().BeFalse();
    }

    [Fact]
    public async Task Reset_restores_the_options_defaults()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database, new FakeMessageBus());
        await store.ReloadAsync();
        await store.SetDisplayAdminLevelsAsync([4]);
        await store.SetFlagAsync(FeatureFlag.MapGrid, false);

        var result = await store.ResetToConfigurationAsync();

        result.Accepted.Should().BeTrue();
        store.Map.AdminLevels.Should().Equal(2);
        store.IsEnabled(FeatureFlag.MapGrid).Should().BeTrue();
    }

    [Fact]
    public async Task A_corrupt_setting_falls_back_to_the_default()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var context = await database.Factory.CreateDbContextAsync())
        {
            context.Settings.Add(
                new GeoTools.Shared.Infrastructure.Persistence.SettingRow
                {
                    Key = SettingKeys.SimplifyTolerance,
                    Value = "not-a-number",
                    Source = "User",
                    UpdatedAt = 0,
                }
            );
            await context.SaveChangesAsync();
        }

        var store = CreateStore(database, new FakeMessageBus());
        await store.ReloadAsync();

        store.Geodata.SimplifyToleranceDegrees.Should().Be(0.002);
    }

    [Fact]
    public async Task Extract_levels_and_tolerance_persist()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database, new FakeMessageBus());
        await store.ReloadAsync();

        (await store.SetExtractAdminLevelsAsync([2, 4, 6])).Accepted.Should().BeTrue();
        (await store.SetSimplifyToleranceAsync(0.01)).Accepted.Should().BeTrue();

        var reloaded = CreateStore(database, new FakeMessageBus());
        await reloaded.ReloadAsync();
        reloaded.Geodata.ExtractAdminLevels.Should().Equal(2, 4, 6);
        reloaded.Geodata.SimplifyToleranceDegrees.Should().Be(0.01);
    }

    [Fact]
    public async Task A_corrupt_level_setting_falls_back_to_the_default()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var context = await database.Factory.CreateDbContextAsync())
        {
            context.Settings.Add(
                new GeoTools.Shared.Infrastructure.Persistence.SettingRow
                {
                    Key = SettingKeys.DisplayAdminLevels,
                    Value = "not-levels",
                    Source = "User",
                    UpdatedAt = 0,
                }
            );
            await context.SaveChangesAsync();
        }

        var store = CreateStore(database, new FakeMessageBus());
        await store.ReloadAsync();

        store.Map.AdminLevels.Should().Equal(2);
    }

    [Fact]
    public async Task A_publish_failure_does_not_reject_the_change()
    {
        await using var database = await TestDatabase.CreateAsync();
        var bus = new FakeMessageBus { ThrowOnPublish = true };
        var store = CreateStore(database, bus);
        await store.ReloadAsync();

        var result = await store.SetDisplayAdminLevelsAsync([4]);

        result.Accepted.Should().BeTrue();
        store.Map.AdminLevels.Should().Equal(4);
    }

    [Fact]
    public async Task Updating_a_flag_twice_updates_the_row()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = CreateStore(database, new FakeMessageBus());
        await store.ReloadAsync();

        await store.SetFlagAsync(FeatureFlag.MapCrosshair, false);
        await store.SetFlagAsync(FeatureFlag.MapCrosshair, true);

        var reloaded = CreateStore(database, new FakeMessageBus());
        await reloaded.ReloadAsync();
        reloaded.IsEnabled(FeatureFlag.MapCrosshair).Should().BeTrue();
    }

    private static SettingsStore CreateStore(TestDatabase database, FakeMessageBus bus) =>
        new(
            database.Factory,
            Options.Create(new MapOptions { AdminLevels = [2] }),
            Options.Create(new GeodataOptions()),
            Options.Create(new FeaturesOptions()),
            bus,
            TimeProvider.System,
            NullLogger<SettingsStore>.Instance
        );
}
