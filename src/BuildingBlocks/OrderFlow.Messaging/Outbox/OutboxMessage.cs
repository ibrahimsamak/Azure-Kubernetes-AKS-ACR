namespace OrderFlow.Messaging.Outbox;

/// <summary>One row = one integration event waiting to be published.
/// Lives in the SAME database as the business data — that co-location is the entire trick.</summary>
public sealed class OutboxMessage
{
    /// <summary>Also used as the integration event's MessageId. Sequential GUID so the
    /// clustered index does not fragment under insert load.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Wire type name from EventTypeRegistry, e.g. "OrderPlaced".</summary>
    public required string Type { get; init; }

    /// <summary>The serialized MessageEnvelope — exactly what will go on the wire.</summary>
    public required string Content { get; init; }

    /// <summary>Kafka partition key. Stored because the dispatcher must not have to
    /// deserialize the payload to know how to route it.</summary>
    public required string PartitionKey { get; init; }

    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;

    /// <summary>NULL = not yet published. The dispatcher's index lives on this column.</summary>
    public DateTime? ProcessedOnUtc { get; set; }

    public int AttemptCount { get; set; }
    public string? LastError { get; set; }

    /// <summary>Exponential backoff: do not retry before this time. Stops one broken row
    /// from being hammered every 500ms and drowning the log.</summary>
    public DateTime? NextAttemptUtc { get; set; }

    /// <summary>W3C traceparent of the transaction that CREATED this row ("00-traceid-spanid-01").
    /// The dispatcher publishes later, from a background loop with no ambient trace; it starts its
    /// span as a child of this, so request -> outbox -> Kafka -> consumer stays ONE trace.
    /// Null for rows written outside any trace (and for rows older than this column).</summary>
    public string? TraceParent { get; init; }
}
