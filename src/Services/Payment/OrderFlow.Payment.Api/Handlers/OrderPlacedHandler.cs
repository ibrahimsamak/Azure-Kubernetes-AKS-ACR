namespace OrderFlow.Payment.Api.Handlers;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts.Orders;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Payment.Api.Domain;
using OrderFlow.Payment.Api.Persistence;

/// <summary>Records what the order will cost. Payment charges on StockReserved, but that
/// event carries no amount — and should not, because then the amount would depend on
/// Inventory getting it right. Keeping our own copy is what lets OrderPlaced and
/// StockReserved arrive in either order.</summary>
public sealed partial class OrderPlacedHandler(
    PaymentDbContext db,
    ILogger<OrderPlacedHandler> logger) : IIntegrationEventHandler<OrderPlaced>
{
    public async Task HandleAsync(OrderPlaced e, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);

        // The Inbox already dedups redeliveries, but OrderPlaced can also be REPLAYED from
        // the topic during a rebuild. Checking first keeps that harmless.
        var exists = await db.PendingCharges.AnyAsync(c => c.OrderId == e.OrderId, ct);
        if (exists)
        {
            LogAlreadyRecorded(logger, e.OrderId);
            return;
        }

        db.PendingCharges.Add(new PendingCharge(e.OrderId, e.CustomerId, e.TotalAmount, e.Currency));
        LogRecorded(logger, e.OrderId, e.TotalAmount, e.Currency);

        // No SaveChanges: IntegrationEventDispatcher commits this with the inbox row.
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Pending charge for {OrderId} already recorded.")]
    private static partial void LogAlreadyRecorded(ILogger logger, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recorded pending charge {Amount} {Currency} for order {OrderId}.")]
    private static partial void LogRecorded(ILogger logger, Guid orderId, decimal amount, string currency);
}
