namespace OrderFlow.Contracts.Orders;

/// <summary>A customer placed an order. Inventory should try to reserve stock.</summary>
public sealed record OrderPlaced : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string Currency { get; init; }
    public required IReadOnlyList<OrderLineDto> Lines { get; init; }
}

/// <summary>Flat primitives only. Do NOT reuse the domain's Money/OrderLine here:
/// a public contract must be free to evolve separately from the internal model.</summary>
public sealed record OrderLineDto(string Sku, int Quantity, decimal UnitPrice);
