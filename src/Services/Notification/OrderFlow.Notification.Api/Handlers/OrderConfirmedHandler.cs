namespace OrderFlow.Notification.Api.Handlers;

using OrderFlow.Contracts.Orders;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.RabbitMq;
using OrderFlow.Notification.Api.Persistence;

public sealed partial class OrderConfirmedHandler(
    NotificationDbContext db,
    RabbitMqEventPublisher rabbit,
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

        // Kafka carried the FACT ("the order was confirmed") — a durable, replayable,
        // ordered record. RabbitMQ now carries the TASK ("send an email, an SMS and a push")
        // — work to be distributed, acked per message, and dead-lettered if a channel fails.
        // Same information, different job, different tool.
        await rabbit.PublishAsync(new
        {
            e.OrderId,
            e.CustomerId,
            Kind = "OrderConfirmed",
            Channels = ConfirmationChannels
        }, ct);

        LogQueued(logger, e.OrderId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued confirmation notification for order {OrderId}.")]
    private static partial void LogQueued(ILogger logger, Guid orderId);
}
