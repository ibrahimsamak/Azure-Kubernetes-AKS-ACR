namespace OrderFlow.Inventory.Api.Handlers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderFlow.Contracts.Inventory;
using OrderFlow.Contracts.Orders;
using OrderFlow.Inventory.Api.Persistence;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Outbox;
using OrderFlow.Messaging.Serialization;

public sealed partial class OrderPlacedHandler(
    InventoryDbContext db,
    ILogger<OrderPlacedHandler> logger) : IIntegrationEventHandler<OrderPlaced>
{
    public async Task HandleAsync(OrderPlaced e, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);

        var skus = e.Lines.Select(l => l.Sku).ToList();
        var items = await db.StockItems
            .Include(s => s.Reservations)
            .Where(s => skus.Contains(s.Sku))
            .ToListAsync(ct);

        // ALL-OR-NOTHING within this service: reserve every line or none.
        // A partially reserved order is a state nobody downstream knows how to handle.
        foreach (var line in e.Lines)
        {
            var item = items.FirstOrDefault(s => s.Sku == line.Sku);

            if (item is null || !item.TryReserve(e.OrderId, line.Quantity, out _))
            {
                LogCannotReserve(logger, line.Quantity, line.Sku, e.OrderId);

                // Roll back the reservations we already made in this handler, in memory.
                // (The dispatcher's transaction would also roll them back, but emitting the
                // failure event requires us to complete the transaction, not abort it.)
                foreach (var i in items) { i.Release(e.OrderId); }

                Emit(new StockReservationFailed
                {
                    OrderId = e.OrderId,
                    Sku = line.Sku,
                    Reason = item is null ? "Unknown SKU" : "Insufficient stock",
                    CorrelationId = e.CorrelationId,
                    CausationId = e.MessageId
                }, e.OrderId);
                return;
            }
        }

        var reservationId = items
            .SelectMany(i => i.Reservations)
            .First(r => r.OrderId == e.OrderId && r.IsActive).Id;

        Emit(new StockReserved
        {
            OrderId = e.OrderId,
            ReservationId = reservationId,
            CorrelationId = e.CorrelationId,
            CausationId = e.MessageId     // causation chain: this event because of that one
        }, e.OrderId);

        LogReserved(logger, e.OrderId);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot reserve {Qty} x {Sku} for order {OrderId}.")]
    private static partial void LogCannotReserve(ILogger logger, int qty, string sku, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reserved stock for order {OrderId}.")]
    private static partial void LogReserved(ILogger logger, Guid orderId);

    /// <summary>Stage an outbox row. NOT a Kafka call — the dispatcher publishes it after
    /// this transaction commits. Same dual-write protection as on the Order side.</summary>
    private void Emit(OrderFlow.Contracts.IntegrationEvent @event, Guid partitionKey)
    {
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = @event.MessageId,
            Type = EventTypeRegistry.NameOf(@event.GetType()),
            Content = MessageEnvelope.Wrap(@event).ToJson(),
            PartitionKey = partitionKey.ToString()
        });
    }
}
