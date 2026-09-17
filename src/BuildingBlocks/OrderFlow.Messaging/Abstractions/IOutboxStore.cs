namespace OrderFlow.Messaging.Abstractions;

using OrderFlow.Contracts;

public interface IOutboxStore
{
    void Enqueue(IntegrationEvent integrationEvent, string partitionKey);
}
