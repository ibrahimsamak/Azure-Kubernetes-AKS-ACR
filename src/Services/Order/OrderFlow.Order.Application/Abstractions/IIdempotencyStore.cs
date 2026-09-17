namespace OrderFlow.Order.Application.Abstractions;

/// <summary>Dedups client REQUESTS — a double-clicked submit button, or a mobile client
/// retrying a request whose response it never saw.
///
/// This solves a DIFFERENT problem from the message Inbox. The Inbox dedups a message that
/// the broker delivered twice; this dedups two genuinely separate HTTP calls that mean the
/// same intent. Both are needed, and neither substitutes for the other.</summary>
public interface IIdempotencyStore
{
    /// <summary>The order a previous request with this key already created, if any.</summary>
    Task<Guid?> FindOrderIdAsync(string key, CancellationToken ct);

    /// <summary>Stages the claim. The caller saves it in the SAME transaction as the order,
    /// so the key can never exist without its order, or the order without its key.</summary>
    void Record(string key, Guid orderId);
}
