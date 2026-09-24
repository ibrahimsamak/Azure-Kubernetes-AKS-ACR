namespace OrderFlow.Notification.Api.RabbitMq;

using OrderFlow.Messaging.RabbitMq;
using OrderFlow.Notification.Api.Tasks;

public sealed class RabbitMqNotificationPublisher(RabbitMqEventPublisher rabbit) : INotificationTaskPublisher
{
    public Task PublishAsync(NotificationRequested notification, CancellationToken ct) =>
        rabbit.PublishAsync(notification, ct);
}
