namespace OrderFlow.Messaging.Serialization;

using System.Text.Json;
using OrderFlow.Contracts;

public sealed record MessageEnvelope(string Type, Guid MessageId, Guid CorrelationId, string Payload)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static MessageEnvelope Wrap(IntegrationEvent e) =>
        new(EventTypeRegistry.NameOf(e.GetType()), e.MessageId, e.CorrelationId, JsonSerializer.Serialize(e, e.GetType(), Json));

    public IntegrationEvent? Unwrap()
    {
        var clrType = EventTypeRegistry.Resolve(Type);
        return clrType is null ? null : (IntegrationEvent?)JsonSerializer.Deserialize(Payload, clrType, Json);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static MessageEnvelope? FromJson(string json) => JsonSerializer.Deserialize<MessageEnvelope>(json, Json);
}
