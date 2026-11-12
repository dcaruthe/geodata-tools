using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoTools.Shared.Infrastructure.State;

internal sealed class JobStore(IDbContextFactory<GeoToolsDbContext> factory, TimeProvider clock)
    : IJobStore
{
    private RunRecord? _run;
    private StepRecord[] _steps = [];

    public RunRecord? LatestRun => Volatile.Read(ref _run);

    public IReadOnlyList<StepRecord> LatestSteps => Volatile.Read(ref _steps);

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow();

        var interrupted = await context
            .Runs.Where(row => row.Status == nameof(RunStatus.Running))
            .ToListAsync(cancellationToken);

        if (interrupted.Count > 0)
        {
            var ids = interrupted.Select(row => row.Id).ToArray();
            foreach (var run in interrupted)
            {
                run.Status = nameof(RunStatus.Failed);
                run.Error = "Interrupted by restart.";
                run.FinishedAt = StoreMappings.ToUnixMilliseconds(now);
            }

            var steps = await context
                .Steps.Where(row =>
                    ids.Contains(row.RunId)
                    && (
                        row.Status == nameof(StepStatus.Running)
                        || row.Status == nameof(StepStatus.Pending)
                    )
                )
                .ToListAsync(cancellationToken);

            foreach (var step in steps)
            {
                step.Status = nameof(StepStatus.Failed);
                step.Error = "Interrupted by restart.";
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        var latest = await context
            .Runs.AsNoTracking()
            .OrderByDescending(row => row.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            Volatile.Write(ref _run, null);
            Volatile.Write(ref _steps, []);
            return;
        }

        await ReloadAsync(context, latest.Id, cancellationToken);
    }

    public async Task ApplyAsync(
        BootstrapRunStarted message,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var id = message.RunId.ToString();

        var run = await context.Runs.FindAsync([id], cancellationToken);
        if (run is null)
        {
            run = new RunRow { Id = id };
            context.Runs.Add(run);
        }

        run.Reason = message.Reason;
        run.Status = nameof(RunStatus.Running);
        run.StartedAt = StoreMappings.ToUnixMilliseconds(message.StartedAt);
        run.FinishedAt = null;
        run.Error = null;

        var existing = await context
            .Steps.Where(row => row.RunId == id)
            .ToListAsync(cancellationToken);
        context.Steps.RemoveRange(existing);

        var ordinal = 0;
        foreach (var step in message.Steps)
        {
            context.Steps.Add(
                new StepRow
                {
                    RunId = id,
                    StepKey = step.Key.Value,
                    Ordinal = ordinal++,
                    Stage = step.Stage,
                    Label = step.Label,
                    Status = nameof(StepStatus.Pending),
                    Attempts = 0,
                    Fraction = 0,
                }
            );
        }

        await context.SaveChangesAsync(cancellationToken);
        await ReloadAsync(context, id, cancellationToken);
    }

    public async Task ApplyAsync(
        BootstrapStepChanged message,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var id = message.RunId.ToString();

        var row = await context.Steps.FindAsync([id, message.Key.Value], cancellationToken);
        if (row is null)
        {
            row = new StepRow
            {
                RunId = id,
                StepKey = message.Key.Value,
                Ordinal = LatestSteps.Count,
            };
            context.Steps.Add(row);
        }
        else if (
            message.Status == StepStatus.Running
            && row.Status
                is nameof(StepStatus.Succeeded)
                    or nameof(StepStatus.Failed)
                    or nameof(StepStatus.Skipped)
                    or nameof(StepStatus.Cancelled)
        )
        {
            // A late progress message must not reopen a step that already finished.
            return;
        }

        row.Stage = message.Stage;
        row.Label = message.Label;
        row.Status = message.Status.ToString();
        row.Fraction = Math.Clamp(message.Progress.Fraction, 0, 1);
        row.BytesReceived = message.Progress.BytesReceived;
        row.TotalBytes = message.Progress.TotalBytes;
        row.BytesPerSecond = message.Progress.BytesPerSecond;
        row.EtaSeconds = message.Progress.EtaSeconds;
        row.Error = message.Error;
        if (message.Status == StepStatus.Running && row.Attempts == 0)
            row.Attempts = 1;

        await context.SaveChangesAsync(cancellationToken);
        await ReloadAsync(context, id, cancellationToken);
    }

    public async Task ApplyAsync(
        BootstrapRunFinished message,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var id = message.RunId.ToString();

        var run = await context.Runs.FindAsync([id], cancellationToken);
        if (run is null)
        {
            run = new RunRow
            {
                Id = id,
                Reason = "unknown",
                StartedAt = StoreMappings.ToUnixMilliseconds(message.FinishedAt),
            };
            context.Runs.Add(run);
        }

        run.Status = message.Status.ToString();
        run.FinishedAt = StoreMappings.ToUnixMilliseconds(message.FinishedAt);
        run.Error = message.Error;

        await context.SaveChangesAsync(cancellationToken);
        await ReloadAsync(context, id, cancellationToken);
    }

    public async Task<bool> IsRequestHandledAsync(
        string requestId,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.HandledRequests.AnyAsync(
            row => row.RequestId == requestId,
            cancellationToken
        );
    }

    public async Task MarkRequestHandledAsync(
        string requestId,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        if (
            await context.HandledRequests.AnyAsync(
                row => row.RequestId == requestId,
                cancellationToken
            )
        )
            return;

        context.HandledRequests.Add(
            new HandledRequestRow
            {
                RequestId = requestId,
                HandledAt = StoreMappings.ToUnixMilliseconds(clock.GetUtcNow()),
            }
        );
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task ReloadAsync(
        GeoToolsDbContext context,
        string runId,
        CancellationToken cancellationToken
    )
    {
        var runRow = await context
            .Runs.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == runId, cancellationToken);

        if (runRow is null)
        {
            Volatile.Write(ref _run, null);
            Volatile.Write(ref _steps, []);
            return;
        }

        var stepRows = await context
            .Steps.AsNoTracking()
            .Where(row => row.RunId == runId)
            .OrderBy(row => row.Ordinal)
            .ToListAsync(cancellationToken);

        Volatile.Write(ref _run, ToRecord(runRow));
        Volatile.Write(ref _steps, stepRows.Select(ToRecord).ToArray());
    }

    private static RunRecord ToRecord(RunRow row) =>
        new(
            new RunId(Guid.Parse(row.Id)),
            row.Reason,
            StoreMappings.ParseEnum<RunStatus>(row.Status),
            StoreMappings.FromUnixMilliseconds(row.StartedAt),
            row.FinishedAt is { } finished ? StoreMappings.FromUnixMilliseconds(finished) : null,
            row.Error
        );

    private static StepRecord ToRecord(StepRow row) =>
        new(
            new RunId(Guid.Parse(row.RunId)),
            new StepKey(row.StepKey),
            row.Stage,
            row.Label,
            StoreMappings.ParseEnum<StepStatus>(row.Status),
            row.Attempts,
            new StepProgress(
                row.Fraction,
                row.BytesReceived,
                row.TotalBytes,
                row.BytesPerSecond,
                row.EtaSeconds
            ),
            row.Error
        );
}
