using GeoTools.Shared.Application.Messaging.Messages;

namespace GeoTools.Shared.Application.State;

public interface IJobStore
{
    RunRecord? LatestRun { get; }

    IReadOnlyList<StepRecord> LatestSteps { get; }

    Task ReconcileAsync(CancellationToken cancellationToken = default);

    Task ApplyAsync(BootstrapRunStarted message, CancellationToken cancellationToken = default);

    Task ApplyAsync(BootstrapStepChanged message, CancellationToken cancellationToken = default);

    Task ApplyAsync(BootstrapRunFinished message, CancellationToken cancellationToken = default);

    Task<bool> IsRequestHandledAsync(
        string requestId,
        CancellationToken cancellationToken = default
    );

    Task MarkRequestHandledAsync(string requestId, CancellationToken cancellationToken = default);
}
