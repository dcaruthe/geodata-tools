using FluentAssertions;
using GeoTools.Shared.Application.Geodata;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Infrastructure.Geodata;
using GeoTools.Shared.Infrastructure.Messaging;
using GeoTools.Shared.Infrastructure.State;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class BootstrapProjectorTests
{
    [Fact]
    public async Task Projector_writes_the_run_and_publishes_the_snapshot()
    {
        await using var database = await TestDatabase.CreateAsync();
        var bus = new FakeMessageBus();
        var jobs = new JobStore(database.Factory, TimeProvider.System);
        var progress = new ProgressStore();
        var projector = new BootstrapProjector(
            jobs,
            progress,
            TimeProvider.System,
            NullLogger<BootstrapProjector>.Instance
        );
        await projector.AttachAsync(bus);
        var runId = RunId.New();

        await bus.DeliverAsync(
            MessageTopics.BootstrapRunStarted,
            new BootstrapRunStarted(
                runId,
                "test",
                [new BootstrapStep(new StepKey("load"), "load", "Loading")],
                DateTimeOffset.UnixEpoch
            )
        );

        jobs.LatestRun!.Status.Should().Be(RunStatus.Running);
        progress.Current.Phase.Should().Be(BootstrapPhase.Running);
        progress.Current.Label.Should().Be("Loading");

        await bus.DeliverAsync(
            MessageTopics.BootstrapStepChanged,
            new BootstrapStepChanged(
                runId,
                new StepKey("load"),
                "load",
                "Loading",
                StepStatus.Running,
                new StepProgress(0.5),
                null,
                DateTimeOffset.UnixEpoch
            )
        );

        progress.Current.Fraction.Should().Be(0.5);

        await bus.DeliverAsync(
            MessageTopics.BootstrapRunFinished,
            new BootstrapRunFinished(runId, RunStatus.Succeeded, null, 1, DateTimeOffset.UnixEpoch)
        );

        progress.Current.Phase.Should().Be(BootstrapPhase.Succeeded);
        progress.Current.Fraction.Should().Be(1);
    }

    [Fact]
    public async Task Projector_ignores_an_unexpected_topic()
    {
        await using var database = await TestDatabase.CreateAsync();
        var bus = new FakeMessageBus();
        var projector = new BootstrapProjector(
            new JobStore(database.Factory, TimeProvider.System),
            new ProgressStore(),
            TimeProvider.System,
            NullLogger<BootstrapProjector>.Instance
        );
        await projector.AttachAsync(bus);

        var act = async () =>
            await bus.DeliverAsync(
                MessageTopics.SettingsChanged,
                new SettingsChanged("a", "b", DateTimeOffset.UnixEpoch)
            );

        await act.Should().NotThrowAsync();
    }
}

public sealed class GeodataWorkerTests
{
    [Fact]
    public async Task A_rebuild_request_runs_the_pipeline_once()
    {
        var pipeline = new ImmediatePipeline();
        var worker = CreateWorker(pipeline, runOnStartup: false);
        var bus = new FakeMessageBus();
        await worker.AttachAsync(bus);
        await worker.StartAsync(CancellationToken.None);

        await bus.DeliverAsync(
            MessageTopics.RebuildRequested,
            new RebuildRequested(
                "request-1",
                RebuildScope.Extract,
                true,
                "africa",
                DateTimeOffset.UnixEpoch
            )
        );
        await WaitForAsync(() => pipeline.Requests.Count == 1, TimeSpan.FromSeconds(5));
        await bus.DeliverAsync(
            MessageTopics.RebuildRequested,
            new RebuildRequested(
                "request-1",
                RebuildScope.Extract,
                true,
                "africa",
                DateTimeOffset.UnixEpoch
            )
        );
        await Task.Delay(100);

        pipeline.Requests.Should().ContainSingle();
        pipeline.Requests[0].Scope.Should().Be(RebuildScope.Extract);
        pipeline.Requests[0].Force.Should().BeTrue();
        pipeline.Requests[0].Region.Should().Be("africa");
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_startup_run_is_queued()
    {
        var pipeline = new ImmediatePipeline();
        var worker = CreateWorker(pipeline, runOnStartup: true);

        await worker.StartAsync(CancellationToken.None);
        await WaitForAsync(() => pipeline.Requests.Count == 1, TimeSpan.FromSeconds(5));

        pipeline.Requests[0].Reason.Should().Be("startup");
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_cancel_request_cancels_the_running_pipeline()
    {
        var pipeline = new BlockingPipeline();
        var worker = CreateWorker(pipeline, runOnStartup: false);
        var bus = new FakeMessageBus();
        await worker.AttachAsync(bus);
        await worker.StartAsync(CancellationToken.None);
        await bus.DeliverAsync(
            MessageTopics.RebuildRequested,
            new RebuildRequested(
                "request-1",
                RebuildScope.All,
                false,
                null,
                DateTimeOffset.UnixEpoch
            )
        );
        await pipeline.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await bus.DeliverAsync(
            MessageTopics.CancelRequested,
            new CancelRequested("cancel-1", DateTimeOffset.UnixEpoch)
        );

        await pipeline.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_cancel_that_arrives_before_the_run_starts_still_cancels_it()
    {
        var pipeline = new BlockingPipeline();
        var worker = CreateWorker(pipeline, runOnStartup: false);
        var bus = new FakeMessageBus();
        await worker.AttachAsync(bus);
        await worker.StartAsync(CancellationToken.None);

        await bus.DeliverAsync(
            MessageTopics.RebuildRequested,
            new RebuildRequested(
                "request-1",
                RebuildScope.All,
                false,
                null,
                DateTimeOffset.UnixEpoch
            )
        );
        await bus.DeliverAsync(
            MessageTopics.CancelRequested,
            new CancelRequested("cancel-1", DateTimeOffset.UnixEpoch)
        );

        await pipeline.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
    }

    private static GeodataWorker CreateWorker(IGeodataPipeline pipeline, bool runOnStartup) =>
        new(
            pipeline,
            new FakeJobStore(),
            Options.Create(new GeodataOptions { RunOnStartup = runOnStartup }),
            NullLogger<GeodataWorker>.Instance
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

    private sealed class ImmediatePipeline : IGeodataPipeline
    {
        public List<BootstrapRequest> Requests { get; } = [];

        public Task<BootstrapOutcome> RunAsync(
            BootstrapRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Requests.Add(request);
            return Task.FromResult(new BootstrapOutcome(RunId.New(), RunStatus.Succeeded, null));
        }
    }

    private sealed class BlockingPipeline : IGeodataPipeline
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<BootstrapOutcome> RunAsync(
            BootstrapRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }

            return new BootstrapOutcome(RunId.New(), RunStatus.Succeeded, null);
        }
    }
}

public sealed class MessageBusServiceTests
{
    [Fact]
    public async Task Start_attaches_the_subscriptions_before_starting_the_bus()
    {
        var bus = new FakeMessageBus();
        var subscription = new TrackingSubscription();
        var service = new MessageBusService(
            bus,
            [subscription],
            NullLogger<MessageBusService>.Instance
        );

        await service.StartAsync(CancellationToken.None);

        subscription.Attached.Should().BeTrue();
        bus.Started.Should().BeTrue();

        await service.StopAsync(CancellationToken.None);
        bus.Stopped.Should().BeTrue();
    }

    private sealed class TrackingSubscription
        : GeoTools.Shared.Application.Messaging.IMessageSubscription
    {
        public bool Attached { get; private set; }

        public Task AttachAsync(
            GeoTools.Shared.Application.Messaging.IMessageBus messageBus,
            CancellationToken cancellationToken = default
        )
        {
            Attached = true;
            return Task.CompletedTask;
        }
    }
}

public sealed class StateInitializerTests
{
    [Fact]
    public async Task Initialize_migrates_and_reloads_the_state()
    {
        await using var database = await TestDatabase.CreateAsync();
        var jobs = new TrackingJobs();
        var settings = new TrackingSettings();
        var initializer = new StateInitializer(
            database.Factory,
            jobs,
            settings,
            NullLogger<StateInitializer>.Instance
        );

        await initializer.InitializeAsync();

        jobs.Reconciled.Should().BeTrue();
        settings.Reloaded.Should().BeTrue();
    }

    private sealed class TrackingJobs : FakeJobStore
    {
        public bool Reconciled { get; private set; }

        public override Task ReconcileAsync(CancellationToken cancellationToken = default)
        {
            Reconciled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingSettings : ISettingsStore
    {
        public bool Reloaded { get; private set; }

        public MapSettings Map { get; } = new([2]);

        public GeodataSettings Geodata { get; } = new([2, 4], 0.002);

        public long Version => 0;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public bool IsEnabled(FeatureFlag flag) => true;

        public Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            Reloaded = true;
            return Task.CompletedTask;
        }

        public Task<SettingsResult> SetDisplayAdminLevelsAsync(
            IReadOnlyList<int> levels,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(SettingsResult.Ok);

        public Task<SettingsResult> SetExtractAdminLevelsAsync(
            IReadOnlyList<int> levels,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(SettingsResult.Ok);

        public Task<SettingsResult> SetSimplifyToleranceAsync(
            double tolerance,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(SettingsResult.Ok);

        public Task<SettingsResult> SetFlagAsync(
            FeatureFlag flag,
            bool enabled,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(SettingsResult.Ok);

        public Task<SettingsResult> ResetToConfigurationAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(SettingsResult.Ok);
    }
}
