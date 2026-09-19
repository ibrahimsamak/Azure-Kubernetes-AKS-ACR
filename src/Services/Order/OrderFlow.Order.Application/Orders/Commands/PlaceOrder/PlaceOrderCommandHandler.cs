namespace OrderFlow.Order.Application.Orders.Commands.PlaceOrder;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderFlow.Messaging.Inbox;
using OrderFlow.Order.Application.Abstractions;
using OrderFlow.Order.Domain.Common;
using OrderFlow.Order.Domain.Orders;
using OrderFlow.Order.Domain.Sagas;

public sealed partial class PlaceOrderCommandHandler(
    IOrderRepository orders,
    IOrderSagaRepository sagas,
    IUnitOfWork unitOfWork,
    IInventoryQueryClient inventory,
    IIdempotencyStore idempotency,
    ILogger<PlaceOrderCommandHandler> logger)
{
    public async Task<Guid> HandleAsync(PlaceOrderCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();

        if (key is not null)
        {
            var already = await idempotency.FindOrderIdAsync(key, ct);
            if (already is not null)
            {
                LogReplayed(logger, key, already.Value);
                return already.Value;
            }
        }

        var availability = await inventory.CheckAvailabilityAsync([.. request.Lines.Select(l => (l.Sku, l.Quantity))], ct);

        if (availability is { Known: true, AllAvailable: false })
        {
            throw new DomainException($"SKU {availability.FirstUnavailableSku} is not available.");
        }

        var order = Order.Create(
            request.CustomerId,
            request.Currency,
            request.Lines.Select(l => (l.Sku, l.Quantity, l.UnitPrice)));

        var saga = OrderSaga.Start(order.Id, order.CustomerId, order.Total, order.Currency);
        saga.MarkOrderPlaced();

        orders.Add(order);
        sagas.Add(saga);
        if (key is not null) { idempotency.Record(key, order.Id); }

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (key is not null && InboxDuplicateDetector.IsDuplicate(ex))
        {
            var winner = await idempotency.FindOrderIdAsync(key, ct)
                ?? throw new InvalidOperationException(
                    $"Idempotency key '{key}' collided but no order was found for it.", ex);

            LogRaceLost(logger, key, winner);
            return winner;
        }

        LogOrderPlaced(logger, order.Id);
        return order.Id;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} placed; saga started.")]
    private static partial void LogOrderPlaced(ILogger logger, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency key {Key} already served order {OrderId}; not creating another.")]
    private static partial void LogReplayed(ILogger logger, string key, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency key {Key} lost a concurrent race; returning order {OrderId}.")]
    private static partial void LogRaceLost(ILogger logger, string key, Guid orderId);
}
