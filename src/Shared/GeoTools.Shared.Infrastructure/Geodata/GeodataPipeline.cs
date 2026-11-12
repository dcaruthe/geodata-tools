using System.Diagnostics;
using GeoTools.Shared.Application.Geodata;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Storage;
using GeoTools.Shared.Domain.Geospatial;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Geodata;

internal sealed class GeodataPipeline(
    IOptions<GeodataOptions> options,
    ISettingsStore settings,
    IArtifactStore artifacts,
    IGeometryCache cache,
    IBorderGeometryLoader loader,
    IBorderGeometryStore geometry,
    IPbfDownloader downloader,
    IBorderExtractor extractor,
    IMessageBus bus,
    TimeProvider clock,
    ILogger<GeodataPipeline> logger
) : IGeodataPipeline
{
    private const string LocalFingerprint = "local";
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    public async Task<BootstrapOutcome> RunAsync(
        BootstrapRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var runId = RunId.New();
        var startedAt = clock.GetUtcNow();
        var levels = settings.Geodata.ExtractAdminLevels.Order().Distinct().ToArray();
        var tolerance = settings.Geodata.SimplifyToleranceDegrees;

        var regions = BootstrapPlanner.Scan(options.Value);
        await AdoptAsync(regions, levels, cancellationToken);

        var existing = (await artifacts.ListAsync(cancellationToken: cancellationToken))
            .GroupBy(artifact => artifact.Key)
            .ToDictionary(group => group.Key, group => group.First());
        var plan = BootstrapPlanner.Plan(request, regions, existing, levels);

        await bus.PublishAsync(
            new BootstrapRunStarted(
                runId,
                request.Reason,
                plan.Steps.Select(step => new BootstrapStep(step.Key, step.Stage, step.Label))
                    .ToArray(),
                startedAt
            ),
            cancellationToken: CancellationToken.None
        );

        var skippedExtractions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var step in plan.Steps)
        {
            if (cancellationToken.IsCancellationRequested)
                return await CancelAsync(runId, step, startedAt, cancellationToken);

            if (
                step.Action == BootstrapAction.Extract
                && step.Region is { } extractRegion
                && skippedExtractions.Contains(extractRegion.Region)
            )
            {
                await PublishStepAsync(
                    runId,
                    step,
                    StepStatus.Skipped,
                    new StepProgress(1),
                    "Up to date",
                    cancellationToken
                );
                continue;
            }

            await PublishStepAsync(
                runId,
                step,
                StepStatus.Running,
                StepProgress.Zero,
                null,
                cancellationToken
            );
            Interlocked.Exchange(ref _lastProgressTicks, 0);

            try
            {
                switch (step.Action)
                {
                    case BootstrapAction.Load:
                        await LoadAsync(runId, step, cancellationToken);
                        break;
                    case BootstrapAction.ClearCache:
                        await cache.ClearAsync(cancellationToken);
                        break;
                    case BootstrapAction.Pull:
                        var changed = await PullAsync(runId, step, cancellationToken);
                        if (!changed && step.Region is { } region)
                            skippedExtractions.Add(region.Region);
                        break;
                    case BootstrapAction.Extract:
                        await ExtractAsync(runId, step, levels, tolerance, cancellationToken);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown step {step.Action}.");
                }

                await PublishStepAsync(
                    runId,
                    step,
                    StepStatus.Succeeded,
                    new StepProgress(1),
                    null,
                    cancellationToken
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return await CancelAsync(runId, step, startedAt, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Bootstrap step {Step} failed.", step.Key.Value);
                await PublishStepAsync(
                    runId,
                    step,
                    StepStatus.Failed,
                    new StepProgress(0),
                    exception.Message,
                    cancellationToken
                );
                await FinishAsync(
                    runId,
                    RunStatus.Failed,
                    exception.Message,
                    startedAt,
                    cancellationToken
                );
                return new BootstrapOutcome(runId, RunStatus.Failed, exception.Message);
            }
        }

        await FinishAsync(runId, RunStatus.Succeeded, null, startedAt, cancellationToken);
        return new BootstrapOutcome(runId, RunStatus.Succeeded, null);
    }

    private async Task LoadAsync(RunId runId, PlannedStep step, CancellationToken cancellationToken)
    {
        var progress = new InlineProgress<double>(fraction =>
            Report(runId, step, new StepProgress(Math.Clamp(fraction, 0, 1)), cancellationToken)
        );
        var result = await loader.LoadAsync(progress, cancellationToken);
        geometry.Set(result.Geometry, result.AvailableLevels);

        if (result.Geometry.Polylines.Count == 0)
            return;

        await bus.PublishAsync(
            new BordersAvailable(
                result.Fingerprint,
                result.Geometry.Polylines.Count,
                result.AvailableLevels,
                result.Geometry.Bounds,
                clock.GetUtcNow()
            ),
            cancellationToken: cancellationToken
        );
    }

    private async Task<bool> PullAsync(
        RunId runId,
        PlannedStep step,
        CancellationToken cancellationToken
    )
    {
        var region = step.Region
            ?? throw new InvalidOperationException("A pull step needs a region.");

        if (!region.SourceExists && !settings.IsEnabled(FeatureFlag.GeodataAutoPull))
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(region.SourcePath)} is missing and automatic download is disabled. Enable geodata.auto-pull in Maintenance."
            );
        }

        var previous = await artifacts.GetAsync(ArtifactKeys.Pbf(region.Region), cancellationToken);
        var validator =
            previous?.Fingerprint is { Length: > 0 } fingerprint && fingerprint != LocalFingerprint
                ? fingerprint
                : null;
        var baseUri = new Uri(options.Value.SourceBaseUrl.TrimEnd('/') + "/");
        var url = new Uri(
            baseUri,
            options.Value.SourceFilePattern.Replace(
                "{region}",
                region.Region,
                StringComparison.Ordinal
            )
        );

        var progress = CreateProgress(runId, step, cancellationToken);
        var result = await downloader.DownloadAsync(
            new DownloadRequest(region.Region, url, region.SourcePath, step.ForcePull, validator),
            progress,
            cancellationToken
        );

        if (!result.Changed && previous is not null)
            return false;

        await artifacts.PutAsync(
            new ArtifactRecord(
                ArtifactKeys.Pbf(region.Region),
                ArtifactKind.Pbf,
                region.Region,
                region.SourcePath,
                result.SizeBytes,
                result.ModifiedAt,
                result.Validator.Length > 0 ? result.Validator : LocalFingerprint,
                [],
                null,
                ArtifactStatus.Current,
                clock.GetUtcNow()
            ),
            cancellationToken
        );

        return result.Changed;
    }

    private async Task ExtractAsync(
        RunId runId,
        PlannedStep step,
        IReadOnlyList<int> levels,
        double tolerance,
        CancellationToken cancellationToken
    )
    {
        var region = step.Region
            ?? throw new InvalidOperationException("An extract step needs a region.");

        if (!File.Exists(region.SourcePath))
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(region.SourcePath)} is missing. Download it from Maintenance."
            );
        }

        var workDirectory = Path.Combine(RepositoryPaths.Resolve(options.Value.RootPath), ".work");
        var progress = new InlineProgress<double>(fraction =>
            Report(runId, step, new StepProgress(Math.Clamp(fraction, 0, 1)), cancellationToken)
        );

        var result = await extractor.ExtractAsync(
            new ExtractionRequest(
                region.Region,
                region.SourcePath,
                region.BordersPath,
                region.LevelsPath,
                workDirectory,
                levels,
                tolerance
            ),
            progress,
            cancellationToken
        );

        await artifacts.PutAsync(
            new ArtifactRecord(
                ArtifactKeys.Borders(region.Region),
                ArtifactKind.Borders,
                region.Region,
                region.BordersPath,
                result.SizeBytes,
                result.ModifiedAt,
                string.Join(',', levels),
                levels,
                result.Bounds,
                ArtifactStatus.Current,
                clock.GetUtcNow()
            ),
            cancellationToken
        );
    }

    private IProgress<DownloadProgress> CreateProgress(
        RunId runId,
        PlannedStep step,
        CancellationToken cancellationToken
    ) =>
        new InlineProgress<DownloadProgress>(download =>
        {
            var fraction =
                download.TotalBytes > 0
                    ? Math.Clamp((double)download.BytesReceived / download.TotalBytes, 0, 1)
                    : 0;

            Report(
                runId,
                step,
                new StepProgress(
                    fraction,
                    download.BytesReceived,
                    download.TotalBytes > 0 ? download.TotalBytes : null,
                    download.BytesPerSecond,
                    download.EtaSeconds
                ),
                cancellationToken
            );
        });

    private long _lastProgressTicks;

    private void Report(
        RunId runId,
        PlannedStep step,
        StepProgress progress,
        CancellationToken cancellationToken
    )
    {
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Read(ref _lastProgressTicks);
        if (previous != 0 && Stopwatch.GetElapsedTime(previous, now) < ProgressInterval)
            return;

        Interlocked.Exchange(ref _lastProgressTicks, now);
        _ = PublishStepSafeAsync(
            runId,
            step,
            StepStatus.Running,
            progress,
            null,
            cancellationToken
        );
    }

    private async Task PublishStepSafeAsync(
        RunId runId,
        PlannedStep step,
        StepStatus status,
        StepProgress progress,
        string? error,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await PublishStepAsync(runId, step, status, progress, error, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not publish progress for {Step}.", step.Key.Value);
        }
        catch (OperationCanceledException) { }
    }

    // Lifecycle messages are durable even when the run is cancelled, so they ignore the run token.
    private Task PublishStepAsync(
        RunId runId,
        PlannedStep step,
        StepStatus status,
        StepProgress progress,
        string? error,
        CancellationToken _
    ) =>
        bus.PublishAsync(
                new BootstrapStepChanged(
                    runId,
                    step.Key,
                    step.Stage,
                    step.Label,
                    status,
                    progress,
                    error,
                    clock.GetUtcNow()
                ),
                cancellationToken: CancellationToken.None
            )
            .AsTask();

    private async Task<BootstrapOutcome> CancelAsync(
        RunId runId,
        PlannedStep step,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await PublishStepAsync(
                runId,
                step,
                StepStatus.Cancelled,
                new StepProgress(0),
                "Cancelled",
                CancellationToken.None
            );
            await FinishAsync(
                runId,
                RunStatus.Cancelled,
                "Cancelled",
                startedAt,
                CancellationToken.None
            );
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not publish the cancellation.");
        }

        return new BootstrapOutcome(runId, RunStatus.Cancelled, "Cancelled");
    }

    private Task FinishAsync(
        RunId runId,
        RunStatus status,
        string? error,
        DateTimeOffset startedAt,
        CancellationToken _
    ) =>
        bus.PublishAsync(
                new BootstrapRunFinished(
                    runId,
                    status,
                    error,
                    (clock.GetUtcNow() - startedAt).TotalSeconds,
                    clock.GetUtcNow()
                ),
                cancellationToken: CancellationToken.None
            )
            .AsTask();

    private async Task AdoptAsync(
        IReadOnlyList<RegionState> regions,
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken
    )
    {
        var now = clock.GetUtcNow();

        foreach (var region in regions)
        {
            if (
                region.SourceExists
                && await artifacts.GetAsync(ArtifactKeys.Pbf(region.Region), cancellationToken)
                    is null
            )
            {
                await artifacts.PutAsync(
                    new ArtifactRecord(
                        ArtifactKeys.Pbf(region.Region),
                        ArtifactKind.Pbf,
                        region.Region,
                        region.SourcePath,
                        region.SourceSize,
                        region.SourceModified,
                        LocalFingerprint,
                        [],
                        null,
                        ArtifactStatus.Current,
                        now
                    ),
                    cancellationToken
                );
            }

            if (
                region.BordersExists
                && BootstrapPlanner.IsBordersCurrent(region, null, levels)
                && await artifacts.GetAsync(ArtifactKeys.Borders(region.Region), cancellationToken)
                    is null
            )
            {
                await artifacts.PutAsync(
                    new ArtifactRecord(
                        ArtifactKeys.Borders(region.Region),
                        ArtifactKind.Borders,
                        region.Region,
                        region.BordersPath,
                        region.BordersSize,
                        region.BordersModified,
                        string.Join(',', levels),
                        levels,
                        null,
                        ArtifactStatus.Current,
                        now
                    ),
                    cancellationToken
                );
            }
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
