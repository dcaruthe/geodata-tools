namespace GeoTools.Shared.Application.State;

public static class ProgressAggregator
{
    public static BootstrapProgressSnapshot Aggregate(
        RunRecord? run,
        IReadOnlyList<StepRecord> steps,
        DateTimeOffset updatedAt
    )
    {
        if (run is null)
            return BootstrapProgressSnapshot.Idle;

        var phase = run.Status switch
        {
            RunStatus.Running => BootstrapPhase.Running,
            RunStatus.Succeeded => BootstrapPhase.Succeeded,
            RunStatus.Failed => BootstrapPhase.Failed,
            RunStatus.Cancelled => BootstrapPhase.Cancelled,
            _ => BootstrapPhase.Idle,
        };

        if (steps.Count == 0)
        {
            return new BootstrapProgressSnapshot(
                phase,
                run.Id,
                "bootstrap",
                run.Status == RunStatus.Running ? "Planning" : "Finished",
                null,
                run.Status == RunStatus.Succeeded ? 1 : 0,
                StepProgress.Zero,
                run.Error,
                updatedAt
            );
        }

        var completed = 0.0;
        foreach (var step in steps)
        {
            completed += step.Status switch
            {
                StepStatus.Succeeded or StepStatus.Skipped => 1,
                StepStatus.Running => Math.Clamp(step.Progress.Fraction, 0, 1),
                _ => 0,
            };
        }

        var current = steps.FirstOrDefault(step => step.Status == StepStatus.Running)
            ?? steps.LastOrDefault(step => step.Status == StepStatus.Pending)
            ?? steps.FirstOrDefault(step =>
                step.Status is StepStatus.Failed or StepStatus.Cancelled
            )
            ?? steps[^1];

        var fraction = run.Status == RunStatus.Succeeded ? 1 : completed / steps.Count;
        var detail =
            current.Progress.BytesReceived is { } received
            && current.Progress.TotalBytes is { } total
                ? $"{FormatBytes(received)} of {FormatBytes(total)}"
                : null;

        return new BootstrapProgressSnapshot(
            phase,
            run.Id,
            current.Stage,
            current.Label,
            detail,
            fraction,
            current.Progress,
            run.Error ?? current.Error,
            updatedAt
        );
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
