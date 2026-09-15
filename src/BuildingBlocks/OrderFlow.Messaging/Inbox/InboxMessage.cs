namespace OrderFlow.Messaging.Inbox;

public sealed class InboxMessage
{
    public required Guid MessageId { get; init; }

    public required string Consumer { get; init; }

    public required string Type { get; init; }
    public DateTime ProcessedOnUtc { get; init; } = DateTime.UtcNow;
}
