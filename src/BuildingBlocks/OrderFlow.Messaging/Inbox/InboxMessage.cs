namespace OrderFlow.Messaging.Inbox;

public sealed class InboxMessage
{
    /// <summary>The producer's MessageId — stable across re-publishes.</summary>
    public required Guid MessageId { get; init; }

    /// <summary>Which consumer processed it (service name). Part of the unique key.</summary>
    public required string Consumer { get; init; }

    public required string Type { get; init; }
    public DateTime ProcessedOnUtc { get; init; } = DateTime.UtcNow;
}
