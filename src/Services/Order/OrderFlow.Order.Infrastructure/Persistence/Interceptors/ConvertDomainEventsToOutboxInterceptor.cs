using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OrderFlow.Messaging.Outbox;
using OrderFlow.Messaging.Serialization;
using OrderFlow.Order.Application.EventMapping;
using OrderFlow.Order.Domain.Common;

namespace OrderFlow.Order.Infrastructure.Persistence.Interceptors;

public sealed class ConvertDomainEventsToOutboxInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
      DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null) { return base.SavingChangesAsync(eventData, result, cancellationToken); }

        var aggregates = context.ChangeTracker.Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        var outbox = new List<OutboxMessage>();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var integrationEvent = DomainToIntegrationEvent.Map(domainEvent);
                if (integrationEvent is null) { continue; }

                var id = Guid.CreateVersion7();
                integrationEvent = integrationEvent with { MessageId = id };

                outbox.Add(new OutboxMessage
                {
                    Id = id,
                    Type = EventTypeRegistry.NameOf(integrationEvent.GetType()),
                    Content = MessageEnvelope.Wrap(integrationEvent).ToJson(),
                    PartitionKey = integrationEvent.CorrelationId.ToString(),
                    OccurredOnUtc = integrationEvent.OccurredOnUtc,
                    TraceParent = Activity.Current?.Id
                });
            }

            aggregate.ClearDomainEvents();
        }

        if (outbox.Count > 0)
        {
            context.Set<OutboxMessage>().AddRange(outbox);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
