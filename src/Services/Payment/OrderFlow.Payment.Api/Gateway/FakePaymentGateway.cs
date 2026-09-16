namespace OrderFlow.Payment.Api.Gateway;

public interface IPaymentGateway
{
    Task<PaymentResult> CaptureAsync(Guid orderId, decimal amount, string currency, string idempotencyKey, CancellationToken ct);
    Task<PaymentResult> RefundAsync(Guid paymentId, CancellationToken ct);
}

public sealed record PaymentResult(bool Succeeded, Guid? PaymentId, string? FailureReason, bool IsRetryable);

/// <summary>Deterministic stand-in for a PSP. Every real PSP integration has the same
/// three shapes of outcome, so building against them now means Week 3's swap is trivial.</summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public async Task<PaymentResult> CaptureAsync(Guid orderId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct)
    {
        await Task.Delay(Random.Shared.Next(50, 200), ct);   // realistic PSP latency

        // NOTE the idempotencyKey parameter: every real PSP takes one, and it is how you
        // avoid double-charging when YOUR retry logic fires. Mention this in interviews —
        // it shows you have actually integrated a payment provider.
        return amount switch
        {
            13.13m => new PaymentResult(false, null, "Card declined (insufficient funds).", IsRetryable: false),
            66.66m => new PaymentResult(false, null, "Gateway timeout.", IsRetryable: true),
            _ => new PaymentResult(true, Guid.CreateVersion7(), null, false)
        };
    }

    public Task<PaymentResult> RefundAsync(Guid paymentId, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, paymentId, null, false));
}
