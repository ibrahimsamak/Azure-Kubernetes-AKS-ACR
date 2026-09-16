namespace OrderFlow.Order.Application.Abstractions;

using OrderFlow.Order.Domain.Sagas;

/// <summary>The saga is keyed by OrderId, so every handler can find it from the OrderId
/// that rides on the integration event.</summary>
public interface IOrderSagaRepository
{
    Task<OrderSaga?> GetAsync(Guid orderId, CancellationToken ct);

    void Add(OrderSaga saga);
}
