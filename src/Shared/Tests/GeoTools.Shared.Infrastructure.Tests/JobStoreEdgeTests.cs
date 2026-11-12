using FluentAssertions;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Infrastructure.Persistence;
using GeoTools.Shared.Infrastructure.State;
using Microsoft.EntityFrameworkCore;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class JobStoreEdgeTests
{
    [Fact]
    public async Task A_step_update_inserts_a_missing_row()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new JobStore(database.Factory, TimeProvider.System);
        var runId = RunId.New();
        await store.ApplyAsync(
            new BootstrapRunStarted(runId, "test", [], DateTimeOffset.UnixEpoch)
        );

        await store.ApplyAsync(
            new BootstrapStepChanged(
                runId,
                new StepKey("extra"),
                "extra",
                "Extra",
                StepStatus.Running,
                new StepProgress(0.25),
                null,
                DateTimeOffset.UnixEpoch
            )
        );

        store.LatestSteps.Should().ContainSingle();
        store.LatestSteps[0].Key.Value.Should().Be("extra");
    }

    [Fact]
    public async Task A_finished_run_without_a_start_is_recorded()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new JobStore(database.Factory, TimeProvider.System);

        await store.ApplyAsync(
            new BootstrapRunFinished(
                RunId.New(),
                RunStatus.Failed,
                "boom",
                1,
                DateTimeOffset.UnixEpoch
            )
        );

        store.LatestRun!.Reason.Should().Be("unknown");
        store.LatestRun.Status.Should().Be(RunStatus.Failed);
        store.LatestRun.Error.Should().Be("boom");
    }

    [Fact]
    public async Task Reconcile_with_no_runs_clears_the_cache()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new JobStore(database.Factory, TimeProvider.System);

        await store.ReconcileAsync();

        store.LatestRun.Should().BeNull();
        store.LatestSteps.Should().BeEmpty();
    }

    [Fact]
    public async Task Reconcile_rejects_a_corrupt_status()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var context = await database.Factory.CreateDbContextAsync())
        {
            context.Runs.Add(
                new RunRow
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Reason = "test",
                    Status = "not-a-status",
                    StartedAt = 0,
                }
            );
            await context.SaveChangesAsync();
        }

        var store = new JobStore(database.Factory, TimeProvider.System);

        var act = async () => await store.ReconcileAsync();

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task A_step_update_for_an_unknown_run_fails()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new JobStore(database.Factory, TimeProvider.System);

        var act = async () =>
            await store.ApplyAsync(
                new BootstrapStepChanged(
                    RunId.New(),
                    new StepKey("load"),
                    "load",
                    "Load",
                    StepStatus.Running,
                    StepProgress.Zero,
                    null,
                    DateTimeOffset.UnixEpoch
                )
            );

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
