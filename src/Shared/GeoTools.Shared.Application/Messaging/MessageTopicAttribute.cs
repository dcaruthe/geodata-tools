namespace GeoTools.Shared.Application.Messaging;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MessageTopicAttribute(string topic) : Attribute
{
    public string Topic { get; } = topic;
}
