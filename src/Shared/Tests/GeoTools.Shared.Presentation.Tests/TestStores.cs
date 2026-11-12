using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.State;

namespace GeoTools.Shared.Presentation.Tests;

internal sealed class FakeJobStore : IJobStore
{
    public RunRecord? LatestRun { get; set; }

    public IReadOnlyList<StepRecord> LatestSteps { get; set; } = [];

    public Task ReconcileAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

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
    ) => Task.FromResult(false);

    public Task MarkRequestHandledAsync(
        string requestId,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;
}

internal sealed class FakeArtifactStore : IArtifactStore
{
    public List<ArtifactRecord> Records { get; } = [];

    public bool ThrowOnList { get; set; }

    public bool CancelOnList { get; set; }

    public Task<ArtifactRecord?> GetAsync(
        ArtifactKey key,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Records.FirstOrDefault(record => record.Key == key));

    public Task<IReadOnlyList<ArtifactRecord>> ListAsync(
        ArtifactKind? kind = null,
        CancellationToken cancellationToken = default
    )
    {
        if (ThrowOnList)
            throw new InvalidOperationException("list failed");
        if (CancelOnList)
            throw new OperationCanceledException();

        return Task.FromResult<IReadOnlyList<ArtifactRecord>>(
            kind is null ? Records : Records.Where(record => record.Kind == kind.Value).ToArray()
        );
    }

    public Task PutAsync(ArtifactRecord record, CancellationToken cancellationToken = default)
    {
        Records.RemoveAll(existing => existing.Key == record.Key);
        Records.Add(record);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(ArtifactKey key, CancellationToken cancellationToken = default)
    {
        Records.RemoveAll(record => record.Key == key);
        return Task.CompletedTask;
    }
}

internal sealed class FakeMessageBus : IMessageBus
{
    public List<object> Published { get; } = [];

    public List<string> ResetSubscriptions { get; } = [];

    public bool ThrowOnPublish { get; set; }

    public bool ThrowOnReset { get; set; }

    public Func<MessageEnvelope, CancellationToken, Task>? Handler { get; private set; }

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
        if (ThrowOnReset)
            throw new InvalidOperationException("reset failed");

        ResetSubscriptions.Add(subscriberId);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

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
