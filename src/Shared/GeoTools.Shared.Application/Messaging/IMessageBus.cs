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

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
