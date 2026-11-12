namespace GeoTools.Shared.Application.State;

public sealed record RunRecord(
    RunId Id,
    string Reason,
    RunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error
);
