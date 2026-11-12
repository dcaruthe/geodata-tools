namespace GeoTools.Shared.Application.Messaging.Messages;

[MessageTopic(MessageTopics.SubscriptionFaulted)]
public sealed record SubscriptionFaulted(
    string Subscriber,
    string Topic,
    long Sequence,
    string Error,
    DateTimeOffset FaultedAt
);
