namespace OrderFlow.Messaging.Outbox;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Serialization;
using System.Diagnostics; 
/// <summary>The counterpart to <see cref="Inbox.EfInboxStore{TContext}"/>. Services whose
/// aggregates do not raise domain events stage their outbox rows through this instead of
/// hand-rolling the same four lines in every handler.</summary>
public sealed class EfOutboxStore<TContext>(TContext db) : IOutboxStore
    where TContext : DbContext
{
    public void Enqueue(IntegrationEvent integrationEvent, string partitionKey)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            // The event's own MessageId is the row id, so a re-publish keeps the identity
            // consumers dedup on.
            Id = integrationEvent.MessageId,
            Type = EventTypeRegistry.NameOf(integrationEvent.GetType()),
            Content = MessageEnvelope.Wrap(integrationEvent).ToJson(),
            PartitionKey = partitionKey,
            OccurredOnUtc = integrationEvent.OccurredOnUtc,
            // The consume span (or HTTP request span) that is running right now.
            TraceParent = Activity.Current?.Id
        });
    }
}
