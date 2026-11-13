using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.State;

namespace GeoTools.Shared.Infrastructure.Tests;

internal sealed class FakeMessageBus : IMessageBus
{
    public List<object> Published { get; } = [];

    public List<string> ResetSubscriptions { get; } = [];

    public Func<MessageEnvelope, CancellationToken, Task>? Handler { get; private set; }

    public bool Started { get; private set; }

    public bool Stopped { get; private set; }

    public bool ThrowOnPublish { get; set; }

    public ValueTask<long> PublishAsync<TMessage>(
        TMessage message,
        MessagePublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        if (ThrowOnPublish)
            throw new InvalidOperationException("publish failed");

        Published.Add(message!);
        return ValueTask.FromResult((long)Published.Count);
    }

    public Task SubscribeAsync(
        string subscriberId,
        IReadOnlyList<string> topics,
        Func<MessageEnvelope, CancellationToken, Task> handler,
        long? startAt = null,
        CancellationToken cancellationToken = default
    )
    {
        Handler = handler;
        return Task.CompletedTask;
    }

    public Task ResetSubscriptionAsync(
        string subscriberId,
        CancellationToken cancellationToken = default
    )
    {
        ResetSubscriptions.Add(subscriberId);
        return Task.CompletedTask;
    }

    public Task DrainAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Started = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Stopped = true;
        return Task.CompletedTask;
    }

    public Task DeliverAsync<TMessage>(string topic, TMessage message) =>
        Handler!(
            new MessageEnvelope(
                1,
                topic,
                MessageSerialization.Serialize(message),
                DateTimeOffset.UnixEpoch,
                "test",
                null
            ),
            CancellationToken.None
        );
}

internal class FakeJobStore : IJobStore
{
    private readonly HashSet<string> _handled = [];

    public RunRecord? LatestRun { get; set; }

    public IReadOnlyList<StepRecord> LatestSteps { get; set; } = [];

    public virtual Task ReconcileAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<RunRecord?> FindLatestRunAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(LatestRun);

    public Task<IReadOnlyList<StepRecord>> ListStepsAsync(
        RunId runId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(LatestSteps);

    public Task ApplyAsync(
        BootstrapRunStarted message,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task ApplyAsync(
        BootstrapStepChanged message,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task ApplyAsync(
        BootstrapRunFinished message,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task<bool> IsRequestHandledAsync(
        string requestId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(_handled.Contains(requestId));

    public Task MarkRequestHandledAsync(
        string requestId,
        CancellationToken cancellationToken = default
    )
    {
        _handled.Add(requestId);
        return Task.CompletedTask;
    }
}
