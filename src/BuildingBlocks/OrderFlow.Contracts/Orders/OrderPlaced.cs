namespace OrderFlow.Contracts.Orders;

public sealed record OrderPlaced : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string Currency { get; init; }
    public required IReadOnlyList<OrderLineDto> Lines { get; init; }
}

public sealed record OrderLineDto(string Sku, int Quantity, decimal UnitPrice);
