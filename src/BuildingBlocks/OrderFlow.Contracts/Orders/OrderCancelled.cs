namespace OrderFlow.Contracts.Orders;

public sealed record OrderCancelled : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required string Reason { get; init; }
    public bool PaymentWasCaptured { get; init; }
}
