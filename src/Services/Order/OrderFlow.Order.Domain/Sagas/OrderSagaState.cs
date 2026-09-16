namespace OrderFlow.Order.Domain.Sagas;

public enum OrderSagaState
{
    /// <summary>Order accepted, OrderPlaced sits in the outbox.</summary>
    Pending = 0,
    /// <summary>OrderPlaced published; waiting for Inventory.</summary>
    AwaitingStock = 1,
    /// <summary>Stock reserved; waiting for Payment.</summary>
    AwaitingPayment = 2,
    /// <summary>Payment captured; about to confirm.</summary>
    Paid = 3,
    /// <summary>Terminal success.</summary>
    Confirmed = 4,
    /// <summary>A step failed; compensations in flight.</summary>
    Compensating = 5,
    /// <summary>Terminal failure, everything undone.</summary>
    Cancelled = 6
}
