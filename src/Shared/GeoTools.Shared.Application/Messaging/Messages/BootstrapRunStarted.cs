using GeoTools.Shared.Application.State;

namespace GeoTools.Shared.Application.Messaging.Messages;

public sealed record BootstrapStep(StepKey Key, string Stage, string Label);

[MessageTopic(MessageTopics.BootstrapRunStarted)]
public sealed record BootstrapRunStarted(
    RunId RunId,
    string Reason,
    IReadOnlyList<BootstrapStep> Steps,
    DateTimeOffset StartedAt
);
