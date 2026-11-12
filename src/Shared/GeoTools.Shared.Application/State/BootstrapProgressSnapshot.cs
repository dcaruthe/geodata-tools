namespace GeoTools.Shared.Application.State;

public sealed record BootstrapProgressSnapshot(
    BootstrapPhase Phase,
    RunId? RunId,
    string Stage,
    string Label,
    string? Detail,
    double Fraction,
    StepProgress Progress,
    string? Error,
    DateTimeOffset UpdatedAt
)
{
    public static BootstrapProgressSnapshot Idle { get; } =
        new(
            BootstrapPhase.Idle,
            null,
            "idle",
            "Starting",
            null,
            0,
            StepProgress.Zero,
            null,
            DateTimeOffset.UnixEpoch
        );
}
