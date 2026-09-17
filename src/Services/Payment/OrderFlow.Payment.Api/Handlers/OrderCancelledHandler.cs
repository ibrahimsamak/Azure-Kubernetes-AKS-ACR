namespace OrderFlow.Payment.Api.Handlers;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts.Orders;
using OrderFlow.Contracts.Payments;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Payment.Api.Gateway;
using OrderFlow.Payment.Api.Persistence;

public sealed partial class OrderCancelledHandler(
    PaymentDbContext db,
    IOutboxStore outbox,
    IPaymentGateway gateway,
    ILogger<OrderCancelledHandler> logger) : IIntegrationEventHandler<OrderCancelled>
{
    public async Task HandleAsync(OrderCancelled e, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);

        var pending = await db.PendingCharges.FirstOrDefaultAsync(c => c.OrderId == e.OrderId, ct);
        if (pending is not null) { db.PendingCharges.Remove(pending); }

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == e.OrderId, ct);
        if (payment is null || !payment.IsCaptured)
        {
            LogNothingToRefund(logger, e.OrderId);
            Emit(new PaymentRefunded
            {
                OrderId = e.OrderId,
                PaymentId = payment?.Id ?? Guid.Empty,
                CorrelationId = e.CorrelationId,
                CausationId = e.MessageId
            }, e.OrderId);
            return;
        }

        var result = await gateway.RefundAsync(payment.Id, ct);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Refund failed for payment {payment.Id}: {result.FailureReason}");
        }

        payment.MarkRefunded();
        LogRefunded(logger, payment.Id, e.OrderId);

        Emit(new PaymentRefunded
        {
            OrderId = e.OrderId,
            PaymentId = payment.Id,
            CorrelationId = e.CorrelationId,
            CausationId = e.MessageId
        }, e.OrderId);
    }

    private void Emit(OrderFlow.Contracts.IntegrationEvent @event, Guid partitionKey) =>
        outbox.Enqueue(@event, partitionKey.ToString());

    [LoggerMessage(Level = LogLevel.Information, Message = "Nothing to refund for order {OrderId}; acknowledging.")]
    private static partial void LogNothingToRefund(ILogger logger, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refunded payment {PaymentId} for cancelled order {OrderId}.")]
    private static partial void LogRefunded(ILogger logger, Guid paymentId, Guid orderId);
}
