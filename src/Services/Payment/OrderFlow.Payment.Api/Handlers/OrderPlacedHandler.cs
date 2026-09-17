namespace OrderFlow.Payment.Api.Handlers;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts.Orders;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Payment.Api.Domain;
using OrderFlow.Payment.Api.Persistence;

public sealed partial class OrderPlacedHandler(
    PaymentDbContext db,
    ILogger<OrderPlacedHandler> logger) : IIntegrationEventHandler<OrderPlaced>
{
    public async Task HandleAsync(OrderPlaced e, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);

        var exists = await db.PendingCharges.AnyAsync(c => c.OrderId == e.OrderId, ct);
        if (exists)
        {
            LogAlreadyRecorded(logger, e.OrderId);
            return;
        }

        db.PendingCharges.Add(new PendingCharge(e.OrderId, e.CustomerId, e.TotalAmount, e.Currency));
        LogRecorded(logger, e.OrderId, e.TotalAmount, e.Currency);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Pending charge for {OrderId} already recorded.")]
    private static partial void LogAlreadyRecorded(ILogger logger, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recorded pending charge {Amount} {Currency} for order {OrderId}.")]
    private static partial void LogRecorded(ILogger logger, Guid orderId, decimal amount, string currency);
}
