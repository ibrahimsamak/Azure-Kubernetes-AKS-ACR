namespace OrderFlow.Order.Infrastructure.Persistence.Repositories;

using OrderFlow.Order.Application.Abstractions;

/// <summary>SaveChanges on the shared DbContext: the business rows and the outbox rows the
/// interceptor appended commit together, or not at all.</summary>
public sealed class UnitOfWork(OrderDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
