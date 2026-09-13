namespace OrderFlow.Order.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Order.Application.Abstractions;
using OrderFlow.Order.Domain.Orders;

public sealed class OrderRepository(OrderDbContext db) : IOrderRepository
{
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);

    public void Add(Order order) => db.Orders.Add(order);
}
