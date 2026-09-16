namespace OrderFlow.Order.Infrastructure.Persistence.Repositories;

// Inside the namespace so `Order` resolves to the aggregate, not the OrderFlow.Order namespace.
using Microsoft.EntityFrameworkCore;
using OrderFlow.Order.Application.Abstractions;
using OrderFlow.Order.Domain.Orders;

public sealed class OrderRepository(OrderDbContext db) : IOrderRepository
{
    /// <summary>Lines come along: every invariant lives on the aggregate, so it must be
    /// whole before anyone calls a method on it.</summary>
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);

    public void Add(Order order) => db.Orders.Add(order);
}
