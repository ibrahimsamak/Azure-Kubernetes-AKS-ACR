namespace OrderFlow.Payment.Api.Domain;

/// <summary>What an order is going to cost, recorded when OrderPlaced arrives so the amount
/// is known by the time StockReserved tells us to charge. StockReserved does not carry the
/// amount — and it should not: Payment owning its own copy is what lets the two events
/// arrive in either order.</summary>
public sealed class PendingCharge
{
    private PendingCharge() { }   // EF

    public PendingCharge(Guid orderId, string customerId, decimal amount, string currency)
    {
        OrderId = orderId;
        CustomerId = customerId;
        Amount = amount;
        Currency = currency;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid OrderId { get; private set; }
    public string CustomerId { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
}
