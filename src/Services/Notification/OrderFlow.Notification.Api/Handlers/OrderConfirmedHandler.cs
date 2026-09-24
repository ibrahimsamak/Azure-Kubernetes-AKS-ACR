namespace OrderFlow.Notification.Api.Handlers;

using OrderFlow.Contracts.Orders;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Notification.Api.Persistence;
using OrderFlow.Notification.Api.Tasks;

public sealed partial class OrderConfirmedHandler(
    NotificationDbContext db,
    INotificationTaskPublisher publisher,
    ILogger<OrderConfirmedHandler> logger) : IIntegrationEventHandler<OrderConfirmed>
{
    private static readonly string[] ConfirmationChannels = ["email", "sms"];

    public async Task HandleAsync(OrderConfirmed e, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);

        db.Notifications.Add(new NotificationRecord
        {
            OrderId = e.OrderId,
            CustomerId = e.CustomerId,
            Kind = "OrderConfirmed",
            Body = $"Your order {e.OrderId} is confirmed."
        });

        // Kafka carried the FACT; the queue carries the TASK. The send happens BEFORE the
        // dispatcher commits the DB transaction. A crash in between means Kafka redelivers and we
        // send again: Service Bus drops the copy (same MessageId); the RabbitMQ workers are
        // idempotent per (order, kind).
        await publisher.PublishAsync(
            new NotificationRequested(e.OrderId, e.CustomerId, "OrderConfirmed", ConfirmationChannels), ct);

        LogQueued(logger, e.OrderId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued confirmation notification for order {OrderId}.")]
    private static partial void LogQueued(ILogger logger, Guid orderId);
}
