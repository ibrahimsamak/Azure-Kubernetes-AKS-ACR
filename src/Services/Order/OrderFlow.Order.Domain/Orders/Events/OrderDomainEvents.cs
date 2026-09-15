using OrderFlow.Order.Domain.Common;

namespace OrderFlow.Order.Domain.Orders.Events;

public sealed record OrderPlacedLine(string Sku, int Quantity, decimal UnitPrice);

public sealed record OrderPlacedDomainEvent(
    Guid OrderId,
    string CustomerId,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderPlacedLine> Lines) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}

public sealed record OrderConfirmedDomainEvent(Guid OrderId, string CustomerId) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}

public sealed record OrderCancelledDomainEvent(
    Guid OrderId,
    string CustomerId,
    string Reason,
    bool PaymentWasCaptured) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}
