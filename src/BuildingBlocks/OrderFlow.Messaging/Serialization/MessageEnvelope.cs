namespace OrderFlow.Messaging.Serialization;

using System.Text.Json;
using OrderFlow.Contracts;

/// <summary>Wire format: { "type": "OrderPlaced", "messageId": "...", "payload": { ... } }</summary>
public sealed record MessageEnvelope(string Type, Guid MessageId, Guid CorrelationId, string Payload)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static MessageEnvelope Wrap(IntegrationEvent e) =>
        new(EventTypeRegistry.NameOf(e.GetType()), e.MessageId, e.CorrelationId, JsonSerializer.Serialize(e, e.GetType(), Json));

    /// <summary>Returns null for an unknown type name. That is NOT an error: a consumer
    /// legitimately ignores event types it does not care about on a shared topic.
    /// Throwing here would DLQ half the traffic on a busy topic.</summary>
    public IntegrationEvent? Unwrap()
    {
        var clrType = EventTypeRegistry.Resolve(Type);
        return clrType is null ? null : (IntegrationEvent?)JsonSerializer.Deserialize(Payload, clrType, Json);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static MessageEnvelope? FromJson(string json) => JsonSerializer.Deserialize<MessageEnvelope>(json, Json);
}
