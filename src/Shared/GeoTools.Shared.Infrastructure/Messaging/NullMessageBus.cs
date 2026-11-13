using GeoTools.Shared.Application.Messaging;

namespace GeoTools.Shared.Infrastructure.Messaging;

// The data layer registers this so a read-only host can build the settings store without the
// message log. The runtime layer registers SqliteMessageBus after it, which wins resolution.
internal sealed class NullMessageBus : IMessageBus
{
    public ValueTask<long> PublishAsync<TMessage>(
        TMessage message,
        MessagePublishOptions? options = null,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotSupportedException(
            "This host only reads state. Publish through the launcher instead."
        );

    public Task SubscribeAsync(
        string subscriberId,
        IReadOnlyList<string> topics,
        Func<MessageEnvelope, CancellationToken, Task> handler,
        long? startAt = null,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task ResetSubscriptionAsync(
        string subscriberId,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task DrainAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
