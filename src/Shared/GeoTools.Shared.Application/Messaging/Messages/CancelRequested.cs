namespace GeoTools.Shared.Application.Messaging.Messages;

[MessageTopic(MessageTopics.CancelRequested)]
public sealed record CancelRequested(string RequestId, DateTimeOffset RequestedAt);
