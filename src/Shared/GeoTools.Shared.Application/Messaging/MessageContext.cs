namespace GeoTools.Shared.Application.Messaging;

public sealed record MessageContext(
    long Sequence,
    string Topic,
    DateTimeOffset PublishedAt,
    string? CorrelationId
);
