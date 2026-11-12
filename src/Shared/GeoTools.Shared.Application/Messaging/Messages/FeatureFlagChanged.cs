using GeoTools.Shared.Application.State;

namespace GeoTools.Shared.Application.Messaging.Messages;

[MessageTopic(MessageTopics.FeatureFlagChanged)]
public sealed record FeatureFlagChanged(FeatureFlag Flag, bool Enabled, DateTimeOffset ChangedAt);
