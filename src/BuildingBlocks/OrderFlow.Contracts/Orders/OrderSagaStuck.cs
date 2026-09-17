namespace OrderFlow.Contracts.Orders;

public sealed record OrderSagaStuck : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string State { get; init; }

    public string? FailureReason { get; init; }
}
