namespace GeoTools.Shared.Application.Messaging;

public sealed record MessagePublishOptions(string? Publisher = null, string? CorrelationId = null);
