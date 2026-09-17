namespace OrderFlow.Payment.Api.Gateway;

/// <summary>The outcome of a gateway call. <c>IsRetryable</c> is the important field:
/// declines are permanent, gateway timeouts are not. Putting that distinction in the RESULT
/// keeps the caller from string-matching a failure message.</summary>
public sealed record PaymentResult(bool Succeeded, Guid? PaymentId, string? FailureReason, bool IsRetryable);

public interface IPaymentGateway
{
    /// <summary>Takes the money. The <c>idempotencyKey</c> is not optional in spirit: every
    /// real PSP accepts one, and it is how you avoid double-charging when YOUR retry fires.</summary>
    Task<PaymentResult> CaptureAsync(Guid orderId, decimal amount, string currency, string idempotencyKey, CancellationToken ct);

    Task<PaymentResult> RefundAsync(Guid paymentId, CancellationToken ct);
}
