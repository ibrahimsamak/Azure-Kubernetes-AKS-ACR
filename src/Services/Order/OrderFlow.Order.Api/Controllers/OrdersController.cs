namespace OrderFlow.Order.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using OrderFlow.Order.Application.Orders.Commands.PlaceOrder;
using OrderFlow.Order.Application.Sagas;

public sealed record PlaceOrderLineRequest(string Sku, int Quantity, decimal UnitPrice);

public sealed record PlaceOrderRequest(
    string CustomerId,
    string Currency,
    IReadOnlyList<PlaceOrderLineRequest> Lines);

[ApiController]
[Route("api/v1/orders")]
public sealed class OrdersController(
    PlaceOrderCommandHandler placeOrder,
    GetOrderSagaStatusQueryHandler sagaStatus) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new PlaceOrderCommand(
            request.CustomerId,
            request.Currency,
            [.. request.Lines.Select(l => new PlaceOrderLine(l.Sku, l.Quantity, l.UnitPrice))]);

        var orderId = await placeOrder.HandleAsync(command, ct);

        return AcceptedAtAction(nameof(GetStatus), new { id = orderId }, new { orderId, status = "Pending" });
    }

    [HttpGet("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken ct)
    {
        var status = await sagaStatus.HandleAsync(id, ct);
        return status is null ? NotFound() : Ok(status);
    }
}
