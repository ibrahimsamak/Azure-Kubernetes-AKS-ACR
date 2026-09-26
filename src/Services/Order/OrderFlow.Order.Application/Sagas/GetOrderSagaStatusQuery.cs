namespace OrderFlow.Order.Application.Sagas;

using OrderFlow.Order.Application.Abstractions;

/// <summary>What the client polls while the saga runs. CustomerId is here so the API can check
/// ownership; it's the caller's own id (or the caller is support), so returning it leaks nothing.</summary>
public sealed record OrderSagaStatus(
    Guid OrderId,
    string CustomerId,
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
            : new OrderSagaStatus(saga.Id, saga.CustomerId, saga.State.ToString(), saga.FailureReason,
                                  saga.StartedAtUtc, saga.DeadlineUtc);
    }
}
