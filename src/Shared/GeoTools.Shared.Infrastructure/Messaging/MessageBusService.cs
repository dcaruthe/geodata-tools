using GeoTools.Shared.Application.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Infrastructure.Messaging;

internal sealed class MessageBusService(
    IMessageBus bus,
    IEnumerable<IMessageSubscription> subscriptions,
    ILogger<MessageBusService> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var subscription in subscriptions)
            await subscription.AttachAsync(bus, cancellationToken);

        await bus.StartAsync(cancellationToken);
        logger.LogInformation("Message bus started.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => bus.StopAsync(cancellationToken);
}
