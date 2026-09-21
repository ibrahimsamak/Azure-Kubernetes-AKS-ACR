namespace OrderFlow.Notification.Api.Handlers;

using OrderFlow.Contracts.Orders;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Notification.Api.Persistence;
using OrderFlow.Notification.Api.ServiceBus;


public sealed partial class OrderConfirmedHandler(
    NotificationDbContext db,
    ServiceBusNotificationPublisher publisher,
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

        // Kafka carried the FACT; Service Bus carries the TASK — same split as Week 2's RabbitMQ.
        // This send happens BEFORE the dispatcher commits the DB transaction. If we crash after
        // sending but before committing, Kafka redelivers, we send again, and Service Bus
        // duplicate detection (same MessageId) drops the second copy.

        await publisher.PublishAsync(
            new NotificationRequested(e.OrderId, e.CustomerId, "OrderConfirmed", ConfirmationChannels), ct
        );   
        // await rabbit.PublishAsync(new
        // {
        //     e.OrderId,
        //     e.CustomerId,
        //     Kind = "OrderConfirmed",
        //     Channels = ConfirmationChannels
        // }, ct);

        LogQueued(logger, e.OrderId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued confirmation notification for order {OrderId}.")]
    private static partial void LogQueued(ILogger logger, Guid orderId);
}
