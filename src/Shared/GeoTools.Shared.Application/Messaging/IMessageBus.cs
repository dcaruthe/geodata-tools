namespace GeoTools.Shared.Application.Messaging;

public interface IMessageBus
{
    ValueTask<long> PublishAsync<TMessage>(
        TMessage message,
        MessagePublishOptions? options = null,
        CancellationToken cancellationToken = default
    );

    Task SubscribeAsync(
        string subscriberId,
        IReadOnlyList<string> topics,
        Func<MessageEnvelope, CancellationToken, Task> handler,
        long? startAt = null,
        CancellationToken cancellationToken = default
    );

    Task ResetSubscriptionAsync(string subscriberId, CancellationToken cancellationToken = default);

    // Delivers every message that is already persisted, without waiting for the poll. A short
    // lived host uses it before stopping so its final state reaches the subscribers.
    Task DrainAsync(CancellationToken cancellationToken = default);

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
