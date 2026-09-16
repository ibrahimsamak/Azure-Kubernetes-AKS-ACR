namespace OrderFlow.Order.Application.Abstractions;

// Inside the namespace so `Order` resolves to the aggregate, not the OrderFlow.Order namespace.
using OrderFlow.Order.Domain.Orders;

/// <summary>The Order aggregate, loaded by id. No SaveChanges here — committing is
/// <see cref="IUnitOfWork"/>'s job, so a handler can change an order AND its saga in one
/// transaction.</summary>
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);

    void Add(Order order);
}
