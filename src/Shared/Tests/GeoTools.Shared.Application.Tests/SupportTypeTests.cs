using FluentAssertions;
using GeoTools.Shared.Application.Geodata;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Storage;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Tests;

public sealed class SupportTypeTests
{
    [Fact]
    public void Branded_ids_render_their_value()
    {
        new ArtifactKey("pbf:africa").ToString().Should().Be("pbf:africa");
        new StepKey("load").ToString().Should().Be("load");
        var runId = RunId.New();
        runId.ToString().Should().Be(runId.Value.ToString("N"));
    }

    [Fact]
    public void Message_support_types_are_constructible()
    {
        var context = new MessageContext(1, "topic", DateTimeOffset.UnixEpoch, "correlation");
        var options = new MessagePublishOptions("publisher", "correlation");
        var envelope = new MessageEnvelope(
            1,
            "topic",
            new byte[] { 1 },
            DateTimeOffset.UnixEpoch,
            "publisher",
            null
        );
        var storage = new StorageOptions { DatabasePath = "state.db" };

        context.ToString().Should().Contain("topic");
        options.Publisher.Should().Be("publisher");
        envelope.ToString().Should().Contain("topic");
        storage.DatabasePath.Should().Be("state.db");
    }

    [Fact]
    public void Step_progress_clamps_to_the_valid_range()
    {
        new StepProgress(-1).Clamped().Fraction.Should().Be(0);
        new StepProgress(2).Clamped().Fraction.Should().Be(1);
        new StepProgress(0.5).Clamped().Fraction.Should().Be(0.5);
        StepProgress.Zero.Fraction.Should().Be(0);
    }

    [Fact]
    public void The_repository_root_is_discovered()
    {
        RepositoryPaths.FindRoot().Should().Contain("geodata-tools");
    }

    [Fact]
    public void Geodata_defaults_name_the_continents()
    {
        GeodataOptions.DefaultRegions.Should().Contain("africa");
        new GeodataOptions().Regions.Should().BeEquivalentTo(GeodataOptions.DefaultRegions);
    }

    [Fact]
    public void The_idle_snapshot_is_idle()
    {
        BootstrapProgressSnapshot.Idle.Phase.Should().Be(BootstrapPhase.Idle);
        BootstrapProgressSnapshot.Idle.Fraction.Should().Be(0);
    }

    [Fact]
    public void Records_render_for_logs()
    {
        var run = new RunRecord(
            RunId.New(),
            "test",
            RunStatus.Succeeded,
            DateTimeOffset.UnixEpoch,
            null,
            null
        );
        var step = new StepRecord(
            run.Id,
            new StepKey("load"),
            "load",
            "Loading",
            StepStatus.Succeeded,
            1,
            StepProgress.Zero,
            null
        );

        run.ToString().Should().Contain("test");
        step.ToString().Should().Contain("load");
    }

    [Fact]
    public void The_aggregator_handles_empty_and_terminal_runs()
    {
        var run = new RunRecord(
            RunId.New(),
            "test",
            RunStatus.Succeeded,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null
        );

        var empty = ProgressAggregator.Aggregate(run, [], DateTimeOffset.UnixEpoch);
        empty.Fraction.Should().Be(1);
        empty.Label.Should().Be("Finished");

        var completed = ProgressAggregator.Aggregate(
            run,
            [
                new StepRecord(
                    run.Id,
                    new StepKey("a"),
                    "a",
                    "A",
                    StepStatus.Succeeded,
                    1,
                    new StepProgress(1),
                    null
                ),
                new StepRecord(
                    run.Id,
                    new StepKey("b"),
                    "b",
                    "B",
                    StepStatus.Succeeded,
                    1,
                    new StepProgress(1),
                    null
                ),
            ],
            DateTimeOffset.UnixEpoch
        );
        completed.Label.Should().Be("B");
    }

    [Fact]
    public void The_aggregator_picks_a_failed_or_cancelled_step()
    {
        var run = new RunRecord(
            RunId.New(),
            "test",
            RunStatus.Failed,
            DateTimeOffset.UnixEpoch,
            null,
            "failed"
        );

        var snapshot = ProgressAggregator.Aggregate(
            run,
            [
                new StepRecord(
                    run.Id,
                    new StepKey("a"),
                    "a",
                    "A",
                    StepStatus.Succeeded,
                    1,
                    new StepProgress(1),
                    null
                ),
                new StepRecord(
                    run.Id,
                    new StepKey("b"),
                    "b",
                    "B",
                    StepStatus.Cancelled,
                    1,
                    new StepProgress(0),
                    "cancelled"
                ),
            ],
            DateTimeOffset.UnixEpoch
        );

        snapshot.Label.Should().Be("B");
        snapshot.Error.Should().Be("failed");
    }

    [Fact]
    public void FormatBytes_handles_terabytes()
    {
        ProgressAggregator.FormatBytes(1024L * 1024 * 1024 * 1024).Should().Be("1 TB");
    }
}
