namespace OrderFlow.Notification.Api.ServiceBus;

using Azure.Messaging.ServiceBus;

public sealed record NotificationRequested(Guid OrderId, string CustomerId, string Kind, string[] Channels);


public sealed class ServiceBusNotificationPublisher(ServiceBusSender sender)
{
    public Task PublishAsync(NotificationRequested notification, CancellationToken ct)
    {
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