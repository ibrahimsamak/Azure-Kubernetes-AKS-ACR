namespace OrderFlow.Contracts.Payments;

public sealed record PaymentFailed : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string Reason { get; init; }
    public required bool IsRetryable { get; init; }
}
