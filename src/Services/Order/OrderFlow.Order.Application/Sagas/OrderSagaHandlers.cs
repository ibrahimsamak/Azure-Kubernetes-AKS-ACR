namespace OrderFlow.Order.Application.Sagas;

using Microsoft.Extensions.Logging;
using OrderFlow.Contracts.Inventory;
using OrderFlow.Contracts.Payments;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Order.Application.Abstractions;
using OrderFlow.Order.Domain.Sagas;

public sealed partial class StockReservedHandler(IOrderSagaRepository sagas, ILogger<StockReservedHandler> logger) 
    : IIntegrationEventHandler<StockReserved>
{
    public async Task HandleAsync(StockReserved e, CancellationToken ct)
    {
        var saga = await sagas.GetAsync(e.OrderId, ct);
        if (saga is null)
        {
            LogNoSaga(logger, e.OrderId);
            return;
        }

        saga.OnStockReserved(e.ReservationId);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No saga for order {OrderId}; ignoring StockReserved.")]
    private static partial void LogNoSaga(ILogger logger, Guid orderId);
}

public sealed class StockReservationFailedHandler(IOrderSagaRepository sagas, IOrderRepository orders)
    : IIntegrationEventHandler<StockReservationFailed>
{
    public async Task HandleAsync(StockReservationFailed e, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);

        var saga = await sagas.GetAsync(e.OrderId, ct);
        if (saga is null) { return; }

        saga.Fail($"Stock unavailable for {e.Sku}: {e.Reason}");
        await SagaCompensation.AnnounceAsync(saga, orders, ct);
    }
}

internal static class SagaCompensation
{
    public static async Task AnnounceAsync(OrderSaga saga, IOrderRepository orders, CancellationToken ct)
    {
        if (saga.State != OrderSagaState.Compensating) { return; }

        var order = await orders.GetByIdAsync(saga.Id, ct);
        order?.Cancel(saga.FailureReason ?? "Saga failed.", saga.PaymentWasCaptured);
    }
}

public sealed class PaymentCapturedHandler(IOrderSagaRepository sagas, IOrderRepository orders) : IIntegrationEventHandler<PaymentCaptured>
{
    public async Task HandleAsync(PaymentCaptured e, CancellationToken ct)
    {
        var saga = await sagas.GetAsync(e.OrderId, ct);
        if (saga is null) { return; }

        saga.OnPaymentCaptured(e.PaymentId);
        saga.Confirm();

        var order = await orders.GetByIdAsync(e.OrderId, ct);
        order?.MarkPlaced();
    }
}

public sealed partial class PaymentFailedHandler(
    IOrderSagaRepository sagas,
    IOrderRepository orders,
    ILogger<PaymentFailedHandler> logger)
    : IIntegrationEventHandler<PaymentFailed>
{
    public async Task HandleAsync(PaymentFailed e, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);

        var saga = await sagas.GetAsync(e.OrderId, ct);
        if (saga is null) { return; }

        if (e.IsRetryable)
        {
            LogRetryableFailure(logger, e.OrderId, e.Reason);
            return;
        }

        saga.Fail($"Payment declined: {e.Reason}");
        await SagaCompensation.AnnounceAsync(saga, orders, ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retryable payment failure for {OrderId}: {Reason}")]
    private static partial void LogRetryableFailure(ILogger logger, Guid orderId, string reason);
}

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
