namespace OrderFlow.Payment.Api.Handlers;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts.Inventory;
using OrderFlow.Contracts.Payments;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Outbox;
using OrderFlow.Messaging.Serialization;
using OrderFlow.Payment.Api.Domain;
using OrderFlow.Payment.Api.Gateway;
using OrderFlow.Payment.Api.Persistence;

public sealed partial class StockReservedHandler(
    PaymentDbContext db,
    IPaymentGateway gateway,
    ILogger<StockReservedHandler> logger) : IIntegrationEventHandler<StockReserved>
{
    public async Task HandleAsync(StockReserved e, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);

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
            throw new InvalidOperationException($"No pending charge for order {e.OrderId} yet.");
        }

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment already captured for {OrderId}; re-emitting event.")]
    private static partial void LogAlreadyCaptured(ILogger logger, Guid orderId);
}
