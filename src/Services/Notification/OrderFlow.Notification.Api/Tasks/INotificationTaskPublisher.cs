namespace OrderFlow.Notification.Api.Tasks;

/// <summary>The TASK in "Kafka carries the fact, a queue carries the task". Matches the Function's
/// NotificationRequested record field for field — it's a contract, keep it small.</summary>
public sealed record NotificationRequested(Guid OrderId, string CustomerId, string Kind, string[] Channels);

public interface INotificationTaskPublisher
{
    Task PublishAsync(NotificationRequested notification, CancellationToken ct);
}
