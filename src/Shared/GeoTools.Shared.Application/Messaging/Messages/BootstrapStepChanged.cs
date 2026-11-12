using GeoTools.Shared.Application.State;

namespace GeoTools.Shared.Application.Messaging.Messages;

[MessageTopic(MessageTopics.BootstrapStepChanged)]
public sealed record BootstrapStepChanged(
    RunId RunId,
    StepKey Key,
    string Stage,
    string Label,
    StepStatus Status,
    StepProgress Progress,
    string? Error,
    DateTimeOffset ChangedAt
);
