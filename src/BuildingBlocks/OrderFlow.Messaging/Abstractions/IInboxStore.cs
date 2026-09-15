namespace OrderFlow.Messaging.Abstractions;

/// <summary>Consumer-side idempotency: records that a message was processed by a consumer.</summary>
public interface IInboxStore
{
    /// <summary>Stages the claim row. Caller saves it together with the business change.</summary>
    void Claim(Guid messageId, string type);

    /// <summary>Cheap pre-check; the unique key remains the correctness guarantee.</summary>
    Task<bool> AlreadyProcessedAsync(Guid messageId, CancellationToken ct);
}
