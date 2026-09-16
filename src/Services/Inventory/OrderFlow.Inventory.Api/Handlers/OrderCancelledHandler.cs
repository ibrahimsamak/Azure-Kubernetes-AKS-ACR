namespace OrderFlow.Inventory.Api.Handlers;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts.Inventory;
using OrderFlow.Contracts.Orders;
using OrderFlow.Inventory.Api.Persistence;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Outbox;
using OrderFlow.Messaging.Serialization;

public sealed partial class OrderCancelledHandler(InventoryDbContext db, ILogger<OrderCancelledHandler> logger)
    : IIntegrationEventHandler<OrderCancelled>
{
    public async Task HandleAsync(OrderCancelled e, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);

        var items = await db.StockItems
            .Include(s => s.Reservations)
            .Where(s => s.Reservations.Any(r => r.OrderId == e.OrderId && r.IsActive))
            .ToListAsync(ct);

        // No active reservation: either we never reserved (failure happened upstream) or a
        // duplicate delivery already released it. Both are fine — ACK anyway so the saga
        // is not left waiting for a StockReleased that will never come.
        foreach (var item in items) { item.Release(e.OrderId); }

        db.Set<OutboxMessage>().Add(Outbox(new StockReleased
        {
            OrderId = e.OrderId,
            ReservationId = Guid.Empty,
            CorrelationId = e.CorrelationId,
            CausationId = e.MessageId
        }, e.OrderId));

        LogReleased(logger, items.Count, e.OrderId);
    }

    /// <summary>Stage an outbox row. NOT a broker call — the dispatcher publishes it after
    /// this transaction commits.</summary>
    private static OutboxMessage Outbox(OrderFlow.Contracts.IntegrationEvent @event, Guid partitionKey) => new()
    {
        Id = @event.MessageId,
        Type = EventTypeRegistry.NameOf(@event.GetType()),
        Content = MessageEnvelope.Wrap(@event).ToJson(),
        PartitionKey = partitionKey.ToString()
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Released {Count} reservation(s) for cancelled order {OrderId}.")]
    private static partial void LogReleased(ILogger logger, int count, Guid orderId);
}
