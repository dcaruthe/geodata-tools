using System.Threading.Channels;
using GeoTools.Shared.Application.Geodata;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Geodata;

internal sealed class GeodataWorker(
    IGeodataPipeline pipeline,
    IJobStore jobs,
    IOptions<GeodataOptions> options,
    ILogger<GeodataWorker> logger
) : BackgroundService, IMessageSubscription
{
    private readonly Channel<BootstrapRequest> _requests =
        Channel.CreateUnbounded<BootstrapRequest>(
            new UnboundedChannelOptions { SingleReader = true }
        );
    private readonly object _runGate = new();
    private CancellationTokenSource? _current;
    private bool _cancelPending;

    public Task AttachAsync(IMessageBus bus, CancellationToken cancellationToken = default) =>
        bus.SubscribeAsync(
            "geodata-worker",
            [MessageTopics.RebuildRequested, MessageTopics.CancelRequested],
            HandleAsync,
            startAt: null,
            cancellationToken
        );

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.RunOnStartup)
            _requests.Writer.TryWrite(BootstrapRequest.Startup);

        await foreach (var request in _requests.Reader.ReadAllAsync(stoppingToken))
        {
            using var current = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            lock (_runGate)
            {
                _current = current;
                if (_cancelPending)
                {
                    _cancelPending = false;
                    current.Cancel();
                }
            }

            try
            {
                var outcome = await pipeline.RunAsync(request, current.Token);
                logger.LogInformation(
                    "Bootstrap run {RunId} finished with {Status}.",
                    outcome.RunId,
                    outcome.Status
                );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Bootstrap run failed.");
            }
            finally
            {
                lock (_runGate)
                    _current = null;
            }
        }
    }

    private async Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        switch (envelope.Topic)
        {
            case MessageTopics.RebuildRequested:
                var rebuild = MessageSerialization.Deserialize<RebuildRequested>(envelope);
                if (await jobs.IsRequestHandledAsync(rebuild.RequestId, cancellationToken))
                    return;

                await jobs.MarkRequestHandledAsync(rebuild.RequestId, cancellationToken);
                _requests.Writer.TryWrite(
                    new BootstrapRequest(
                        $"rebuild:{rebuild.Scope}",
                        rebuild.Scope,
                        rebuild.Force,
                        rebuild.Region
                    )
                );
                break;
            case MessageTopics.CancelRequested:
                CancellationTokenSource? running = null;
                lock (_runGate)
                {
                    // A cancel can arrive after the rebuild request is queued but before the
                    // worker loop starts the run. Remember it so the run starts cancelled.
                    if (_current is null)
                        _cancelPending = true;
                    else
                        running = _current;
                }

                try
                {
                    running?.Cancel();
                }
                catch (ObjectDisposedException) { }

                break;
        }
    }
}
