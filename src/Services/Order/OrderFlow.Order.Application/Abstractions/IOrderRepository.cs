namespace OrderFlow.Order.Application.Abstractions;

using OrderFlow.Order.Domain.Orders;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);

    void Add(Order order);
}
