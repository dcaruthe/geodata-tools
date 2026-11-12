namespace GeoTools.Shared.Application.State;

public sealed record StepRecord(
    RunId RunId,
    StepKey Key,
    string Stage,
    string Label,
    StepStatus Status,
    int Attempts,
    StepProgress Progress,
    string? Error
);
