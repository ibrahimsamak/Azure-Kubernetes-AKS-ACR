namespace OrderFlow.Contracts.Inventory;
using OrderFlow.Contracts;

public sealed record StockReservationFailed : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string Sku { get; init; }
    public required string Reason { get; init; }
}