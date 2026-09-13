namespace OrderFlow.Contracts.Orders;

public sealed record OrderCancelled : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required string Reason { get; init; }
    /// <summary>True if payment had already been captured, so Payment knows to refund.
    /// The saga knows this; the consumers should not have to guess.</summary>
    public bool PaymentWasCaptured { get; init; }
}
