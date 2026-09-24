namespace OrderFlow.Notification.Api.Handlers;

using OrderFlow.Contracts.Orders;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Notification.Api.Persistence;
using OrderFlow.Notification.Api.Tasks;

public sealed partial class OrderCancelledHandler(
    NotificationDbContext db,
    INotificationTaskPublisher publisher,
    ILogger<OrderCancelledHandler> logger) : IIntegrationEventHandler<OrderCancelled>
{
    private static readonly string[] CancellationChannels = ["email"];

    public async Task HandleAsync(OrderCancelled e, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);

        // Say whether the money is coming back. "Your order was cancelled" without that
        // sentence is the message that generates the support ticket.
        var body = e.PaymentWasCaptured
            ? $"Your order {e.OrderId} was cancelled ({e.Reason}). Your refund is on its way."
            : $"Your order {e.OrderId} was cancelled ({e.Reason}). You have not been charged.";

        db.Notifications.Add(new NotificationRecord
        {
            OrderId = e.OrderId,
            CustomerId = e.CustomerId,
            Kind = "OrderCancelled",
            Body = body
        });

        // Same idempotency rule as OrderConfirmed: a Kafka redelivery is dropped by Service Bus
        // duplicate detection (same MessageId), or ignored by the idempotent RabbitMQ workers.
        await publisher.PublishAsync(
            new NotificationRequested(e.OrderId, e.CustomerId, "OrderCancelled", CancellationChannels), ct);

        LogQueued(logger, e.OrderId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued cancellation notification for order {OrderId}.")]
    private static partial void LogQueued(ILogger logger, Guid orderId);
}
