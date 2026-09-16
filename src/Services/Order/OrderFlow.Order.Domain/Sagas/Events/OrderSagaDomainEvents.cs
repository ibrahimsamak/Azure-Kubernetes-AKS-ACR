using OrderFlow.Order.Domain.Common;

namespace OrderFlow.Order.Domain.Sagas.Events;

public sealed record OrderSagaCompletedDomainEvent(Guid OrderId, string CustomerId) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}

public sealed record OrderSagaFailedDomainEvent(
    Guid OrderId,
    string CustomerId,
    string Reason,
    bool PaymentWasCaptured) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}

public sealed record OrderSagaCancelledDomainEvent(Guid OrderId, string CustomerId, string Reason) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}

public sealed record OrderSagaStuckDomainEvent(Guid OrderId, string State, string? FailureReason) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}
