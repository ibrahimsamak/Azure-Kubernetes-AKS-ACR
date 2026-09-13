namespace OrderFlow.Order.Infrastructure.Persistence.Repositories;

using OrderFlow.Order.Application.Abstractions;

public sealed class UnitOfWork(OrderDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
