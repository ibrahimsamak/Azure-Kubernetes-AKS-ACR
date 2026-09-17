namespace OrderFlow.Order.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Order.Application.Abstractions;

public sealed class IdempotentRequest
{
    public required string Key { get; init; }
    public required Guid OrderId { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class EfIdempotencyStore(OrderDbContext db) : IIdempotencyStore
{
    public async Task<Guid?> FindOrderIdAsync(string key, CancellationToken ct)
    {
        var existing = await db.IdempotentRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == key, ct);

        return existing?.OrderId;
    }

    public void Record(string key, Guid orderId) =>
        db.IdempotentRequests.Add(new IdempotentRequest { Key = key, OrderId = orderId });
}
