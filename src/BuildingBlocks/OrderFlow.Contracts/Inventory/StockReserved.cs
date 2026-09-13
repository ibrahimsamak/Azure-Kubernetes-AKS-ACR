namespace OrderFlow.Contracts.Inventory;

public sealed record StockReserved : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid ReservationId { get; init; }
}