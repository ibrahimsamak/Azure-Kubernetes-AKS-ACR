namespace OrderFlow.Order.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Order.Application.Abstractions;

/// <summary>One row per client request key. The PRIMARY KEY is the guarantee: two
/// simultaneous double-clicks both pass the read below, and the database decides which one
/// created the order.</summary>
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
