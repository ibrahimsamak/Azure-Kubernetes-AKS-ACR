namespace OrderFlow.Payment.Api.Gateway;

public sealed class FakePaymentGateway : IPaymentGateway
{
    public async Task<PaymentResult> CaptureAsync(Guid orderId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct)
    {
        await Task.Delay(Random.Shared.Next(50, 200), ct);

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
