using GeoTools.Shared.Application.State;

namespace GeoTools.Shared.Application.Messaging.Messages;

[MessageTopic(MessageTopics.BootstrapRunFinished)]
public sealed record BootstrapRunFinished(
    RunId RunId,
    RunStatus Status,
    string? Error,
    double DurationSeconds,
    DateTimeOffset FinishedAt
);
