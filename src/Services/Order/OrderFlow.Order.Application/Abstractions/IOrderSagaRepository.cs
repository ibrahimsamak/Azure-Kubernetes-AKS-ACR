namespace OrderFlow.Order.Application.Abstractions;

using OrderFlow.Order.Domain.Sagas;

public interface IOrderSagaRepository
{
    Task<OrderSaga?> GetAsync(Guid orderId, CancellationToken ct);

    void Add(OrderSaga saga);
}
