using OrderFlow.Contracts;

namespace OrderFlow.Messaging.Abstractions;

public interface IEventPublisher
{
#pragma warning disable CA1716
    Task PublishAsync(IntegrationEvent @event, string partitionKey, CancellationToken ct = default);
#pragma warning restore CA1716

    Task PublishRawAsync(string topic, string key, string value, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default);
}
