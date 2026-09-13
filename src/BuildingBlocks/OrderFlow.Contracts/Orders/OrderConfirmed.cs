namespace OrderFlow.Contracts.Orders;

public sealed record OrderConfirmed : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string CustomerId { get; init; }
}
