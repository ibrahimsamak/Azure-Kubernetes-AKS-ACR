namespace OrderFlow.Order.Application.Orders.Commands.PlaceOrder;

using Microsoft.Extensions.Logging;
using OrderFlow.Order.Application.Abstractions;
using OrderFlow.Order.Domain.Orders;
using OrderFlow.Order.Domain.Sagas;

public sealed partial class PlaceOrderCommandHandler(
    IOrderRepository orders,
    IOrderSagaRepository sagas,
    IUnitOfWork unitOfWork,
    ILogger<PlaceOrderCommandHandler> logger)
{
    public async Task<Guid> HandleAsync(PlaceOrderCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var order = Order.Create(
            request.CustomerId,
            request.Currency,
            request.Lines.Select(l => (l.Sku, l.Quantity, l.UnitPrice)));

        var saga = OrderSaga.Start(order.Id, order.CustomerId, order.Total, order.Currency);
        saga.MarkOrderPlaced();

        orders.Add(order);
        sagas.Add(saga);

        await unitOfWork.SaveChangesAsync(ct);

        LogOrderPlaced(logger, order.Id);
        return order.Id;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} placed; saga started.")]
    private static partial void LogOrderPlaced(ILogger logger, Guid orderId);
}
