using GeoTools.Shared.Application.Geodata;

namespace GeoTools.Shared.Application.Messaging.Messages;

[MessageTopic(MessageTopics.RebuildRequested)]
public sealed record RebuildRequested(
    string RequestId,
    RebuildScope Scope,
    bool Force,
    string? Region,
    DateTimeOffset RequestedAt
);
