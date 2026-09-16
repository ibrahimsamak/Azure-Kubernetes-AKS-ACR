namespace OrderFlow.Order.Application.Sagas;

using Microsoft.Extensions.Logging;
using OrderFlow.Contracts.Inventory;
using OrderFlow.Contracts.Payments;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Order.Application.Abstractions;
using OrderFlow.Order.Domain.Sagas;

/// <summary>Inventory reserved the stock: advance to payment.</summary>
public sealed partial class StockReservedHandler(IOrderSagaRepository sagas, ILogger<StockReservedHandler> logger) : IIntegrationEventHandler<StockReserved>
{
    public async Task HandleAsync(StockReserved e, CancellationToken ct)
    {
        var saga = await sagas.GetAsync(e.OrderId, ct);
        if (saga is null)
        {
            // Not an error worth dead-lettering: could be a replayed historic event,
            // or an event for an order this service no longer has. Log and move on.
            LogNoSaga(logger, e.OrderId);
            return;
        }

        saga.OnStockReserved(e.ReservationId);
        // No SaveChanges here: IntegrationEventDispatcher commits saga + inbox + outbox together.
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No saga for order {OrderId}; ignoring StockReserved.")]
    private static partial void LogNoSaga(ILogger logger, Guid orderId);
}

/// <summary>Inventory could not reserve: fail fast, no compensation needed for stock.</summary>
public sealed class StockReservationFailedHandler(IOrderSagaRepository sagas) : IIntegrationEventHandler<StockReservationFailed>
{
    public async Task HandleAsync(StockReservationFailed e, CancellationToken ct)
    {
        var saga = await sagas.GetAsync(e.OrderId, ct);
        saga?.Fail($"Stock unavailable for {e.Sku}: {e.Reason}");
    }
}


/// <summary>Money moved. Mark paid, then immediately confirm.</summary>
public sealed class PaymentCapturedHandler(IOrderSagaRepository sagas, IOrderRepository orders) : IIntegrationEventHandler<PaymentCaptured>
{
    public async Task HandleAsync(PaymentCaptured e, CancellationToken ct)
    {
        var saga = await sagas.GetAsync(e.OrderId, ct);
        if (saga is null) { return; }

        saga.OnPaymentCaptured(e.PaymentId);
        saga.Confirm();     // no-op unless we actually reached Paid

        // Keep the ORDER aggregate in step with the saga: the saga tracks the process,
        // the order tracks the business entity. Do not merge them — the order outlives
        // the saga and has its own lifecycle (returns, amendments).
        var order = await orders.GetByIdAsync(e.OrderId, ct);
        order?.MarkPlaced();
    }
}


/// <summary>Payment failed. Retryable failures wait for Payment's own retry; permanent
/// failures start compensation immediately.</summary>
public sealed partial class PaymentFailedHandler(IOrderSagaRepository sagas, ILogger<PaymentFailedHandler> logger)
    : IIntegrationEventHandler<PaymentFailed>
{
    public async Task HandleAsync(PaymentFailed e, CancellationToken ct)
    {
        var saga = await sagas.GetAsync(e.OrderId, ct);
        if (saga is null) { return; }

        if (e.IsRetryable)
        {
            // Gateway timeout / 503: Payment retries on its own. Stay put; the saga
            // deadline is the backstop if the retries never succeed.
            LogRetryableFailure(logger, e.OrderId, e.Reason);
            return;
        }

        saga.Fail($"Payment declined: {e.Reason}");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retryable payment failure for {OrderId}: {Reason}")]
    private static partial void LogRetryableFailure(ILogger logger, Guid orderId, string reason);
}

/// <summary>Compensation ACKs.</summary>
public sealed class StockReleasedHandler(IOrderSagaRepository sagas, IOrderRepository orders) : IIntegrationEventHandler<StockReleased>
{
    public async Task HandleAsync(StockReleased e, CancellationToken ct)
    {
        var saga = await sagas.GetAsync(e.OrderId, ct);
        if (saga is null) { return; }

        saga.OnStockReleased();
        if (saga.State == OrderSagaState.Cancelled)
        {
            var order = await orders.GetByIdAsync(e.OrderId, ct);
            order?.Cancel(saga.FailureReason ?? "Saga compensated.", saga.PaymentWasCaptured);
        }
    }
}

public sealed class PaymentRefundedHandler(IOrderSagaRepository sagas) : IIntegrationEventHandler<PaymentRefunded>
{
    public async Task HandleAsync(PaymentRefunded e, CancellationToken ct)
    {
        var saga = await sagas.GetAsync(e.OrderId, ct);
        saga?.OnPaymentRefunded();
    }
}