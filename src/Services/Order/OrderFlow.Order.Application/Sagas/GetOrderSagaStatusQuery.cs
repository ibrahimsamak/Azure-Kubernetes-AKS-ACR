namespace OrderFlow.Order.Application.Sagas;

using OrderFlow.Order.Application.Abstractions;

/// <summary>What the client polls while the saga runs.</summary>
public sealed record OrderSagaStatus(
    Guid OrderId,
    string State,
    string? FailureReason,
    DateTime StartedAtUtc,
    DateTime? DeadlineUtc);

public sealed class GetOrderSagaStatusQueryHandler(IOrderSagaRepository sagas)
{
    public async Task<OrderSagaStatus?> HandleAsync(Guid orderId, CancellationToken ct)
    {
        var saga = await sagas.GetAsync(orderId, ct);
        return saga is null
            ? null
            : new OrderSagaStatus(saga.Id, saga.State.ToString(), saga.FailureReason, saga.StartedAtUtc, saga.DeadlineUtc);
    }
}
