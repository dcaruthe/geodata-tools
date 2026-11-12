namespace GeoTools.Shared.Application.Messaging;

public sealed record MessageEnvelope(
    long Sequence,
    string Topic,
    ReadOnlyMemory<byte> Body,
    DateTimeOffset PublishedAt,
    string Publisher,
    string? CorrelationId
);
