namespace OrderFlow.Messaging.Outbox;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Serialization;
using System.Diagnostics; 
public sealed class EfOutboxStore<TContext>(TContext db) : IOutboxStore
    where TContext : DbContext
{
    public void Enqueue(IntegrationEvent integrationEvent, string partitionKey)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = integrationEvent.MessageId,
            Type = EventTypeRegistry.NameOf(integrationEvent.GetType()),
            Content = MessageEnvelope.Wrap(integrationEvent).ToJson(),
            PartitionKey = partitionKey,
            OccurredOnUtc = integrationEvent.OccurredOnUtc,
            TraceParent = Activity.Current?.Id
        });
    }
}
