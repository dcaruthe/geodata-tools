namespace GeoTools.Shared.Application.Messaging;

public interface IMessageSubscription
{
    Task AttachAsync(IMessageBus bus, CancellationToken cancellationToken = default);
}
