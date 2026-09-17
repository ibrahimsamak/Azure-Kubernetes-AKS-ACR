namespace OrderFlow.Payment.Api.Gateway;

public sealed record PaymentResult(bool Succeeded, Guid? PaymentId, string? FailureReason, bool IsRetryable);

public interface IPaymentGateway
{
    Task<PaymentResult> CaptureAsync(Guid orderId, decimal amount, string currency, string idempotencyKey, CancellationToken ct);

    Task<PaymentResult> RefundAsync(Guid paymentId, CancellationToken ct);
}
