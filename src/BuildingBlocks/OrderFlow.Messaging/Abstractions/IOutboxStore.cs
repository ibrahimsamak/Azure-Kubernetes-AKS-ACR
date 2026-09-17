namespace OrderFlow.Messaging.Abstractions;

using OrderFlow.Contracts;

/// <summary>Producer-side durability: stages an event as a row in the caller's own database.
/// Deliberately NOT a broker call — the dispatcher publishes it after this transaction
/// commits, which is what stops a crash between "saved" and "published".</summary>
public interface IOutboxStore
{
    /// <summary>Stages the row; the caller saves it together with the business change.
    /// <paramref name="partitionKey"/> is usually the orderId — it is what keeps one
    /// business process's events ordered relative to each other on the topic.</summary>
    void Enqueue(IntegrationEvent integrationEvent, string partitionKey);
}
