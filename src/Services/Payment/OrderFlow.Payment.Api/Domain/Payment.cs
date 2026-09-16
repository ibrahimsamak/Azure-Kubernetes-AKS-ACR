namespace OrderFlow.Payment.Api.Domain;

/// <summary>A captured charge. One row per order at most — that uniqueness is the second
/// guard (after the Inbox) against charging a customer twice.</summary>
public sealed class Payment
{
    private Payment() { }   // EF

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public bool IsCaptured { get; private set; }
    public DateTime CapturedAtUtc { get; private set; }

    /// <summary>The id comes from the gateway, not from us: it is the PSP's charge that
    /// has to be refundable later, so its identifier is the one worth storing.</summary>
    public static Payment Capture(Guid paymentId, Guid orderId, decimal amount, string currency) => new()
    {
        Id = paymentId,
        OrderId = orderId,
        Amount = amount,
        Currency = currency,
        IsCaptured = true,
        CapturedAtUtc = DateTime.UtcNow
    };

    /// <summary>COMPENSATION. Idempotent: refunding twice must not un-capture twice.</summary>
    public void MarkRefunded() => IsCaptured = false;
}
