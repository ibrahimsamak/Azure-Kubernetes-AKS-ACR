namespace OrderFlow.Contracts;

/// <summary>
/// Base for everything that crosses a service boundary.
/// Integration events are PUBLIC contracts: they are versioned, backwards-compatible,
/// and must NOT leak internal domain types (no Money value object, no OrderId struct —
/// just primitives another team could deserialize from Java or Python).
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>Unique per logical message. Assigned when the outbox row is created.
    /// Stable across re-publishes — that is what makes consumer dedup possible.</summary>
    public Guid MessageId { get; init; } = Guid.CreateVersion7();

    /// <summary>When the FACT happened (not when it was published).</summary>
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Ties every message in one business process together. For us: the orderId.
    /// This is what lets you grep a whole saga out of the logs.</summary>
    public Guid CorrelationId { get; init; }

    /// <summary>The MessageId of the message that caused this one. Build a causation chain
    /// and you can draw the actual runtime graph of your system.</summary>
    public Guid? CausationId { get; init; }
}
