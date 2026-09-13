namespace OrderFlow.Order.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Order.Application.Abstractions;
using OrderFlow.Order.Domain.Sagas;

public sealed class OrderSagaRepository(OrderDbContext db) : IOrderSagaRepository
{
    public Task<OrderSaga?> GetAsync(Guid orderId, CancellationToken ct) =>
        db.OrderSagas.FirstOrDefaultAsync(s => s.Id == orderId, ct);

    public void Add(OrderSaga saga) => db.OrderSagas.Add(saga);
}
