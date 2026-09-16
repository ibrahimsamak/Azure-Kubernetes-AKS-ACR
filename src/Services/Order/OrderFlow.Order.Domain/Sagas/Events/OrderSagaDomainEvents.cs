using OrderFlow.Order.Domain.Common;

namespace OrderFlow.Order.Domain.Sagas.Events;

/// <summary>The saga reached its terminal success state: stock held and money taken.</summary>
public sealed record OrderSagaCompletedDomainEvent(Guid OrderId, string CustomerId) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>A step failed. One event starts EVERY compensation; consumers decide what applies
/// to them (Inventory releases, Payment refunds, Notification apologises).</summary>
public sealed record OrderSagaFailedDomainEvent(
    Guid OrderId,
    string CustomerId,
    string Reason,
    bool PaymentWasCaptured) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>Every compensation ACKed: nothing is still held or taken.</summary>
public sealed record OrderSagaCancelledDomainEvent(Guid OrderId, string CustomerId, string Reason) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>Compensation itself timed out. Nobody can fix this automatically — it exists to
/// page a human, which is why it carries the state rather than a customer-facing reason.</summary>
public sealed record OrderSagaStuckDomainEvent(Guid OrderId, string State, string? FailureReason) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}
