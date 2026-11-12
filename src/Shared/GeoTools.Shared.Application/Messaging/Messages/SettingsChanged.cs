namespace GeoTools.Shared.Application.Messaging.Messages;

[MessageTopic(MessageTopics.SettingsChanged)]
public sealed record SettingsChanged(string Key, string Value, DateTimeOffset ChangedAt);
