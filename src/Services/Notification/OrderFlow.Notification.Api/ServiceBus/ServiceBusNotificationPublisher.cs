namespace OrderFlow.Notification.Api.ServiceBus;

using Azure.Messaging.ServiceBus;
using OrderFlow.Notification.Api.Tasks;

public sealed class ServiceBusNotificationPublisher(ServiceBusSender sender) : INotificationTaskPublisher
{
    public Task PublishAsync(NotificationRequested notification, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(notification))
        {
            // Same order + same kind => same MessageId => Service Bus discards the duplicate
            // (topic created with duplicate detection, 10-minute window).
            MessageId = $"{notification.OrderId}:{notification.Kind}",
            ContentType = "application/json",
            Subject = notification.Kind,
            CorrelationId = notification.OrderId.ToString()
        };

        return sender.SendMessageAsync(message, ct);
    }
}
