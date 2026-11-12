using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace GeoTools.Shared.Application.Messaging;

public static class MessageSerialization
{
    private static readonly ConcurrentDictionary<Type, string> TopicCache = new();
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string TopicFor<TMessage>() => TopicFor(typeof(TMessage));

    public static string TopicFor(Type messageType) =>
        TopicCache.GetOrAdd(messageType, type =>
            type.GetCustomAttribute<MessageTopicAttribute>()?.Topic
            ?? throw new InvalidOperationException(
                $"{type.Name} has no {nameof(MessageTopicAttribute)}."
            )
        );

    public static byte[] Serialize<TMessage>(TMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, Options);

    public static TMessage Deserialize<TMessage>(MessageEnvelope envelope) =>
        JsonSerializer.Deserialize<TMessage>(envelope.Body.Span, Options)
        ?? throw new JsonException($"Could not read {envelope.Topic} message {envelope.Sequence}.");
}
