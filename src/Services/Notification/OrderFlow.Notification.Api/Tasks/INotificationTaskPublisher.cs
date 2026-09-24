namespace OrderFlow.Notification.Api.Tasks;

public sealed record NotificationRequested(Guid OrderId, string CustomerId, string Kind, string[] Channels);

public interface INotificationTaskPublisher
{
    Task PublishAsync(NotificationRequested notification, CancellationToken ct);
}
