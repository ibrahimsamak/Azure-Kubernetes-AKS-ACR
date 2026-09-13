namespace OrderFlow.Contracts.Payments;

public sealed record PaymentFailed : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string Reason { get; init; }
    /// <summary>Declines are permanent; gateway timeouts are not. The saga treats them
    /// differently: retry vs compensate. Put the decision in the CONTRACT, not in a
    /// string-matching if-statement in the consumer.</summary>
    public required bool IsRetryable { get; init; }
}
