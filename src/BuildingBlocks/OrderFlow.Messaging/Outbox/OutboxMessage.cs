namespace OrderFlow.Messaging.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Type { get; init; }

    public required string Content { get; init; }

    public required string PartitionKey { get; init; }

    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;

    public DateTime? ProcessedOnUtc { get; set; }

    public int AttemptCount { get; set; }
    public string? LastError { get; set; }

    public DateTime? NextAttemptUtc { get; set; }

    public string? TraceParent { get; init; }
}
