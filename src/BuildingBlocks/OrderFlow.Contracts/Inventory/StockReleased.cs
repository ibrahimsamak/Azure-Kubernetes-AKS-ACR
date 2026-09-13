namespace OrderFlow.Contracts.Inventory;
using OrderFlow.Contracts;

public sealed record StockReleased : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid ReservationId { get; init; }
}