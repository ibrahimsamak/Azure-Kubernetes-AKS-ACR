namespace OrderFlow.Order.Application.Abstractions;

public interface IIdempotencyStore
{
    Task<Guid?> FindOrderIdAsync(string key, CancellationToken ct);

    void Record(string key, Guid orderId);
}
