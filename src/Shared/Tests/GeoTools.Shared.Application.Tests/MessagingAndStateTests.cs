using System.Reflection;
using FluentAssertions;
using GeoTools.Shared.Application.Geodata;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Tests;

public sealed class MessageContractTests
{
    [Fact]
    public void Every_message_type_has_a_unique_topic_listed_in_the_catalog()
    {
        var types = typeof(BootstrapRunStarted)
            .Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<MessageTopicAttribute>() is not null)
            .ToArray();

        var topics = types.Select(MessageSerialization.TopicFor).ToArray();

        topics.Should().OnlyHaveUniqueItems();
        topics.Should().BeEquivalentTo(MessageTopics.All);
    }

    [Fact]
    public void A_type_without_a_topic_is_rejected()
    {
        var act = () => MessageSerialization.TopicFor<NoTopic>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*MessageTopic*");
    }

    [Fact]
    public void Messages_round_trip_through_the_serializer()
    {
        var message = new BootstrapStepChanged(
            RunId.New(),
            new StepKey("extract:africa"),
            "extract",
            "Extracting africa",
            StepStatus.Running,
            new StepProgress(0.25, 10, 40, 5, 6),
            null,
            DateTimeOffset.UnixEpoch
        );

        var envelope = new MessageEnvelope(
            7,
            MessageTopics.BootstrapStepChanged,
            MessageSerialization.Serialize(message),
            DateTimeOffset.UnixEpoch,
            "test",
            "correlation"
        );

        var roundTripped = MessageSerialization.Deserialize<BootstrapStepChanged>(envelope);

        roundTripped.Should().Be(message);
    }

    [Fact]
    public void Rebuild_requests_round_trip()
    {
        var message = new RebuildRequested(
            "request",
            RebuildScope.Extract,
            true,
            "asia",
            DateTimeOffset.UnixEpoch
        );

        var envelope = new MessageEnvelope(
            1,
            MessageTopics.RebuildRequested,
            MessageSerialization.Serialize(message),
            DateTimeOffset.UnixEpoch,
            "test",
            null
        );

        MessageSerialization.Deserialize<RebuildRequested>(envelope).Should().Be(message);
    }

    private sealed record NoTopic(string Value);
}

public sealed class ProgressAggregatorTests
{
    [Fact]
    public void A_missing_run_is_idle()
    {
        ProgressAggregator
            .Aggregate(null, [], DateTimeOffset.UnixEpoch)
            .Phase.Should()
            .Be(BootstrapPhase.Idle);
    }

    [Fact]
    public void A_running_run_weights_completed_and_in_progress_steps()
    {
        var run = new RunRecord(
            RunId.New(),
            "test",
            RunStatus.Running,
            DateTimeOffset.UnixEpoch,
            null,
            null
        );
        var steps = new[]
        {
            Step(StepStatus.Succeeded, 1, "load"),
            Step(StepStatus.Running, 0.5, "extract"),
        };

        var snapshot = ProgressAggregator.Aggregate(run, steps, DateTimeOffset.UnixEpoch);

        snapshot.Phase.Should().Be(BootstrapPhase.Running);
        snapshot.Fraction.Should().Be(0.75);
        snapshot.Label.Should().Be("extract");
        snapshot.Progress.Fraction.Should().Be(0.5);
    }

    [Fact]
    public void A_completed_run_is_complete()
    {
        var run = new RunRecord(
            RunId.New(),
            "test",
            RunStatus.Succeeded,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null
        );

        var snapshot = ProgressAggregator.Aggregate(
            run,
            [Step(StepStatus.Succeeded, 1, "load")],
            DateTimeOffset.UnixEpoch
        );

        snapshot.Phase.Should().Be(BootstrapPhase.Succeeded);
        snapshot.Fraction.Should().Be(1);
    }

    [Fact]
    public void A_failed_run_carries_the_error_and_stage()
    {
        var run = new RunRecord(
            RunId.New(),
            "test",
            RunStatus.Failed,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            "boom"
        );

        var snapshot = ProgressAggregator.Aggregate(
            run,
            [Step(StepStatus.Failed, 0, "pull") with { Error = "network" }],
            DateTimeOffset.UnixEpoch
        );

        snapshot.Phase.Should().Be(BootstrapPhase.Failed);
        snapshot.Error.Should().Be("boom");
        snapshot.Label.Should().Be("pull");
    }

    [Fact]
    public void Byte_details_use_human_units()
    {
        var run = new RunRecord(
            RunId.New(),
            "test",
            RunStatus.Running,
            DateTimeOffset.UnixEpoch,
            null,
            null
        );
        var step = Step(StepStatus.Running, 0.5, "pull") with
        {
            Progress = new StepProgress(0.5, 1024, 2048, 1, 2),
        };

        var snapshot = ProgressAggregator.Aggregate(run, [step], DateTimeOffset.UnixEpoch);

        snapshot.Detail.Should().Be("1 KB of 2 KB");
    }

    [Fact]
    public void FormatBytes_scales_units()
    {
        ProgressAggregator.FormatBytes(0).Should().Be("0 B");
        ProgressAggregator.FormatBytes(1536).Should().Be("1.5 KB");
        ProgressAggregator.FormatBytes(1024L * 1024 * 1024).Should().Be("1 GB");
    }

    private static StepRecord Step(StepStatus status, double fraction, string key) =>
        new(RunId.New(), new StepKey(key), key, key, status, 1, new StepProgress(fraction), null);
}

public sealed class StateStoreTests
{
    [Fact]
    public void The_progress_store_swaps_snapshots_and_raises_changed()
    {
        var store = new ProgressStore();
        var raised = 0;
        store.Changed += (_, _) => raised++;

        store.Set(BootstrapProgressSnapshot.Idle with { Fraction = 0.5 });

        store.Current.Fraction.Should().Be(0.5);
        raised.Should().Be(1);
    }

    [Fact]
    public void The_geometry_store_versions_every_update()
    {
        var store = new BorderGeometryStore();
        var raised = 0;
        store.Changed += (_, _) => raised++;

        store.Set(BorderGeometry.Empty, [2]);
        store.Set(
            new BorderGeometry(
                [new GeoPolyline([new GeoPoint(0, 0), new GeoPoint(1, 1)], false, 2)],
                GeoBounds.World
            ),
            [2, 4]
        );

        store.Version.Should().Be(2);
        store.AvailableAdminLevels.Should().Equal(2, 4);
        store.Geometry.Polylines.Should().ContainSingle();
        raised.Should().Be(2);
    }

    [Fact]
    public void Settings_validation_accepts_empty_display_levels_and_rejects_invalid_values()
    {
        SettingsValidation.ValidateAdminLevels([], allowEmpty: true).Accepted.Should().BeTrue();
        SettingsValidation.ValidateAdminLevels([], allowEmpty: false).Accepted.Should().BeFalse();
        SettingsValidation
            .ValidateAdminLevels([2, 4], allowEmpty: false)
            .Accepted.Should()
            .BeTrue();
        SettingsValidation.ValidateAdminLevels([0], allowEmpty: true).Accepted.Should().BeFalse();
        SettingsValidation.ValidateTolerance(0.002).Accepted.Should().BeTrue();
        SettingsValidation.ValidateTolerance(-0.1).Accepted.Should().BeFalse();
        SettingsValidation.ValidateTolerance(double.NaN).Accepted.Should().BeFalse();
    }

    [Fact]
    public void Relative_paths_resolve_against_the_given_root()
    {
        GeoTools
            .Shared.Application.Storage.RepositoryPaths.Resolve("geodata/state", "/tmp/root")
            .Should()
            .Be(Path.GetFullPath("/tmp/root/geodata/state"));
        GeoTools
            .Shared.Application.Storage.RepositoryPaths.Resolve("/tmp/absolute", "/ignored")
            .Should()
            .Be(Path.GetFullPath("/tmp/absolute"));
    }
}
