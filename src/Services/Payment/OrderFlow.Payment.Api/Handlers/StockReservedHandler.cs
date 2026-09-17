namespace OrderFlow.Payment.Api.Handlers;

// Inside the namespace so `Payment` resolves to the entity, not the OrderFlow.Payment namespace.
using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts.Inventory;
using OrderFlow.Contracts.Payments;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Payment.Api.Domain;
using OrderFlow.Payment.Api.Gateway;
using OrderFlow.Payment.Api.Persistence;

public sealed partial class StockReservedHandler(
    PaymentDbContext db,
    IOutboxStore outbox,
    IPaymentGateway gateway,
    ILogger<StockReservedHandler> logger) : IIntegrationEventHandler<StockReserved>
{
    public async Task HandleAsync(StockReserved e, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);

        // Belt-and-braces on top of the Inbox: if a payment row already exists for this
        // order, do not call the gateway again. Two independent guards, because the cost
        // of being wrong here is a customer charged twice.
        var existing = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == e.OrderId, ct);
        if (existing is { IsCaptured: true })
        {
            LogAlreadyCaptured(logger, e.OrderId);
            Emit(new PaymentCaptured
            {
                OrderId = e.OrderId,
                PaymentId = existing.Id,
                Amount = existing.Amount,
                Currency = existing.Currency,
                CorrelationId = e.CorrelationId,
                CausationId = e.MessageId
            }, e.OrderId);
            return;
        }

        var order = await db.PendingCharges.FirstOrDefaultAsync(c => c.OrderId == e.OrderId, ct);
        if (order is null)
        {
            // OUT-OF-ORDER ARRIVAL: StockReserved beat OrderPlaced here. Different topics,
            // no cross-topic ordering guarantee. Throwing triggers retry-then-DLQ, which
            // is the right call: by the next retry, OrderPlaced will normally have landed.
            throw new InvalidOperationException($"No pending charge for order {e.OrderId} yet.");
        }

        // The orderId is the gateway idempotency key: a retry with the same key returns the
        // ORIGINAL charge instead of creating a second one.
        var result = await gateway.CaptureAsync(e.OrderId, order.Amount, order.Currency,
            idempotencyKey: e.OrderId.ToString(), ct);

        if (result.Succeeded)
        {
            var payment = Payment.Capture(result.PaymentId!.Value, e.OrderId, order.Amount, order.Currency);
            db.Payments.Add(payment);

            Emit(new PaymentCaptured
            {
                OrderId = e.OrderId,
                PaymentId = payment.Id,
                Amount = payment.Amount,
                Currency = payment.Currency,
                CorrelationId = e.CorrelationId,
                CausationId = e.MessageId
            }, e.OrderId);
        }
        else
        {
            Emit(new PaymentFailed
            {
                OrderId = e.OrderId,
                Reason = result.FailureReason!,
                IsRetryable = result.IsRetryable,
                CorrelationId = e.CorrelationId,
                CausationId = e.MessageId
            }, e.OrderId);
        }
    }

    /// <summary>Stage an outbox row. NOT a Kafka call — the dispatcher publishes it after
    /// this transaction commits. Same dual-write protection as on the Order side.</summary>
    private void Emit(OrderFlow.Contracts.IntegrationEvent @event, Guid partitionKey) =>
        outbox.Enqueue(@event, partitionKey.ToString());

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment already captured for {OrderId}; re-emitting event.")]
    private static partial void LogAlreadyCaptured(ILogger logger, Guid orderId);
}
