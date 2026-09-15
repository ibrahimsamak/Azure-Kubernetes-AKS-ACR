using OrderFlow.Order.Domain.Common;
using OrderFlow.Order.Domain.Orders.Events;

namespace OrderFlow.Order.Domain.Orders;

/// <summary>Aggregate root. Lines are only reachable through the order, so every invariant
/// (at least one line, no duplicate SKU, status transitions) is enforced in one place.</summary>
public sealed class Order : Entity<Guid>
{
    private readonly List<OrderLine> _lines = [];

    public string CustomerId { get; private set; } = null!;
    public string Currency { get; private set; } = null!;

    /// <summary>Stored, not only computed, so listing orders never has to load their lines.</summary>
    public decimal Total { get; private set; }

    public OrderStatus Status { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    private Order() { }   // EF

    public static Order Create(string customerId, string currency, IEnumerable<(string Sku, int Quantity, decimal UnitPrice)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (string.IsNullOrWhiteSpace(customerId)) { throw new DomainException("Customer is required."); }
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new DomainException("Currency must be a 3-letter ISO 4217 code.");
        }

        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId.Trim(),
            Currency = currency.Trim().ToUpperInvariant(),
            Status = OrderStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        foreach (var (sku, quantity, unitPrice) in lines)
        {
            if (order._lines.Exists(l => string.Equals(l.Sku, sku?.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new DomainException($"SKU '{sku}' appears more than once; combine the quantities.");
            }
            order._lines.Add(new OrderLine(order.Id, sku!, quantity, unitPrice));
        }

        if (order._lines.Count == 0) { throw new DomainException("An order needs at least one line."); }

        order.Total = order._lines.Sum(l => l.LineTotal);

        order.Raise(new OrderPlacedDomainEvent(
            order.Id,
            order.CustomerId,
            order.Total,
            order.Currency,
            [.. order._lines.Select(l => new OrderPlacedLine(l.Sku, l.Quantity, l.UnitPrice))]));

        return order;
    }

    /// <summary>Saga succeeded: stock reserved and payment captured.</summary>
    public void MarkPlaced()
    {
        // Idempotent: Kafka is at-least-once, so the saga may deliver the same step twice.
        if (Status == OrderStatus.Placed) { return; }
        if (Status != OrderStatus.Pending)
        {
            throw new DomainException($"Order {Id} cannot be placed from status {Status}.");
        }

        Status = OrderStatus.Placed;
        UpdatedAtUtc = DateTime.UtcNow;
        Raise(new OrderConfirmedDomainEvent(Id, CustomerId));
    }

    public void Cancel(string reason, bool paymentWasCaptured)
    {
        if (Status == OrderStatus.Cancelled) { return; }
        if (Status != OrderStatus.Pending)
        {
            throw new DomainException($"Order {Id} cannot be cancelled from status {Status}.");
        }
        if (string.IsNullOrWhiteSpace(reason)) { throw new DomainException("A cancellation reason is required."); }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        UpdatedAtUtc = DateTime.UtcNow;
        Raise(new OrderCancelledDomainEvent(Id, CustomerId, reason, paymentWasCaptured));
    }
}
