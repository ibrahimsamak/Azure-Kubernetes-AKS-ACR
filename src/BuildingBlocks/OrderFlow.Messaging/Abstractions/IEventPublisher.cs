using OrderFlow.Contracts;

namespace OrderFlow.Messaging.Abstractions;

public interface IEventPublisher
{
#pragma warning disable CA1716 // Identifiers should not match keywords
    Task PublishAsync(IntegrationEvent @event, string partitionKey, CancellationToken ct = default);
#pragma warning restore CA1716 // Identifiers should not match keywords

    /// <summary>Raw escape hatch used by the DLQ publisher, which must forward bytes it
    /// could not deserialize.</summary>
    Task PublishRawAsync(string topic, string key, string value, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default);
}