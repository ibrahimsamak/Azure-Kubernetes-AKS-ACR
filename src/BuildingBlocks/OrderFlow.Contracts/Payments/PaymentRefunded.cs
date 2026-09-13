namespace OrderFlow.Contracts.Payments;

public sealed record PaymentRefunded : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid PaymentId { get; init; }
}
