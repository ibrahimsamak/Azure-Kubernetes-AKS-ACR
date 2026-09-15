namespace OrderFlow.Payment.Api.Domain;

public sealed class Payment
{
    private Payment() { }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public bool IsCaptured { get; private set; }
    public DateTime CapturedAtUtc { get; private set; }

    public static Payment Capture(Guid paymentId, Guid orderId, decimal amount, string currency) => new()
    {
        Id = paymentId,
        OrderId = orderId,
        Amount = amount,
        Currency = currency,
        IsCaptured = true,
        CapturedAtUtc = DateTime.UtcNow
    };

    public void MarkRefunded() => IsCaptured = false;
}
