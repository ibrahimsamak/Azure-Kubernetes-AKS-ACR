namespace OrderFlow.Contracts.Payments;

public sealed record PaymentCaptured : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid PaymentId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}
