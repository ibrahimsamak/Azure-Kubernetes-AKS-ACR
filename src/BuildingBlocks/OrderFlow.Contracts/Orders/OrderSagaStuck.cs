namespace OrderFlow.Contracts.Orders;

/// <summary>Compensation itself timed out: something is still held or still charged and the
/// system could not undo it on its own. This is an OPERATIONAL alert, not a business fact —
/// no service is expected to act on it automatically, and none should try. It goes on the
/// wire rather than into a log line because an alert about money in the wrong place must be
/// as durable and replayable as the events that put it there.</summary>
public sealed record OrderSagaStuck : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    /// <summary>The saga state it is wedged in, as a string: a human reads this, and the
    /// enum must stay free to change without breaking the contract.</summary>
    public required string State { get; init; }

    public string? FailureReason { get; init; }
}
