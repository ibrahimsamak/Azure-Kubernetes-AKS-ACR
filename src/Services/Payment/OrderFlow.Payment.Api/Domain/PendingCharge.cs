namespace OrderFlow.Payment.Api.Domain;

public sealed class PendingCharge
{
    private PendingCharge() { }

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
