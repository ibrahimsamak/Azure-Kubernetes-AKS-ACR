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

        // --- 0. Has this exact request already been served? -----------------------------
        // The cheap read catches the ordinary case (a retry seconds later). It cannot catch
        // two simultaneous clicks — the unique key at step 4 does that.
        if (key is not null)
        {
            var already = await idempotency.FindOrderIdAsync(key, ct);
            if (already is not null)
            {
                LogReplayed(logger, key, already.Value);
                return already.Value;
            }
        }

        // --- 1. ADVISORY sync pre-check (gRPC) ------------------------------------------
        // Fail obviously-impossible orders in 5ms instead of 400ms via the saga. It is a
        // HINT, never a reservation: stock can vanish between this call and the reservation.
        // If Inventory is down we accept the order anyway and let the saga decide — the
        // availability of the ORDER path must not depend on Inventory being up.
        var availability = await inventory.CheckAvailabilityAsync([.. request.Lines.Select(l => (l.Sku, l.Quantity))], ct);

        if (availability is { Known: true, AllAvailable: false })
        {
            throw new DomainException($"SKU {availability.FirstUnavailableSku} is not available.");
        }

        // --- 2. Build the aggregate. Create() raises OrderPlacedDomainEvent. -------------
        var order = Order.Create(
            request.CustomerId,
            request.Currency,
            request.Lines.Select(l => (l.Sku, l.Quantity, l.UnitPrice)));

        // --- 3. Start the saga in the SAME transaction ----------------------------------
        var saga = OrderSaga.Start(order.Id, order.CustomerId, order.Total, order.Currency);
        saga.MarkOrderPlaced();

        orders.Add(order);
        sagas.Add(saga);
        if (key is not null) { idempotency.Record(key, order.Id); }

        // --- 4. ONE transaction ---------------------------------------------------------
        // Order + OrderLines + OrderSaga + OutboxMessage(OrderPlaced) + the idempotency key.
        // The interceptor added the outbox row; the dispatcher will publish it within ~500ms.
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (key is not null && InboxDuplicateDetector.IsDuplicate(ex))
        {
            // A concurrent request with the same key won the race. Everything we staged —
            // order, lines, saga, outbox row — rolls back with this transaction, so there is
            // exactly one order. Hand back the winner's id.
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
