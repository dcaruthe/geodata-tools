using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.State;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Infrastructure.Messaging;

internal sealed class BootstrapProjector(
    IJobStore jobs,
    IProgressStore progress,
    TimeProvider clock,
    ILogger<BootstrapProjector> logger
) : IMessageSubscription
{
    public Task AttachAsync(IMessageBus bus, CancellationToken cancellationToken = default) =>
        bus.SubscribeAsync(
            "bootstrap-projector",
            [
                MessageTopics.BootstrapRunStarted,
                MessageTopics.BootstrapStepChanged,
                MessageTopics.BootstrapRunFinished,
            ],
            HandleAsync,
            startAt: 0,
            cancellationToken
        );

    private async Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        switch (envelope.Topic)
        {
            case MessageTopics.BootstrapRunStarted:
                await jobs.ApplyAsync(
                    MessageSerialization.Deserialize<BootstrapRunStarted>(envelope),
                    cancellationToken
                );
                break;
            case MessageTopics.BootstrapStepChanged:
                await jobs.ApplyAsync(
                    MessageSerialization.Deserialize<BootstrapStepChanged>(envelope),
                    cancellationToken
                );
                break;
            case MessageTopics.BootstrapRunFinished:
                await jobs.ApplyAsync(
                    MessageSerialization.Deserialize<BootstrapRunFinished>(envelope),
                    cancellationToken
                );
                break;
            default:
                logger.LogWarning("Ignoring unexpected topic {Topic}.", envelope.Topic);
                return;
        }

        progress.Set(
            ProgressAggregator.Aggregate(jobs.LatestRun, jobs.LatestSteps, clock.GetUtcNow())
        );
    }
}
