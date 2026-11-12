using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Presentation.Views;

internal sealed class ViewTransitionSubscriber(
    INavigationMediator mediator,
    ILogger<ViewTransitionSubscriber> logger
) : IMessageSubscription
{
    public Task AttachAsync(IMessageBus bus, CancellationToken cancellationToken = default) =>
        bus.SubscribeAsync(
            "view-transition",
            [MessageTopics.BordersAvailable, MessageTopics.BootstrapRunFinished],
            HandleAsync,
            startAt: null,
            cancellationToken
        );

    private Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        if (mediator.ActiveView != ViewId.Loading)
            return Task.CompletedTask;

        switch (envelope.Topic)
        {
            case MessageTopics.BordersAvailable:
                mediator.NavigateTo(ViewId.Map);
                logger.LogInformation("Border data is ready; showing the map.");
                break;
            case MessageTopics.BootstrapRunFinished:
                var finished = MessageSerialization.Deserialize<BootstrapRunFinished>(envelope);
                if (finished.Status == RunStatus.Succeeded)
                {
                    mediator.NavigateTo(ViewId.Map);
                    logger.LogInformation("Bootstrap finished; showing the map.");
                }

                break;
        }

        return Task.CompletedTask;
    }
}
