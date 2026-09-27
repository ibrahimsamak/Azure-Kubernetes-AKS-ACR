namespace OrderFlow.Order.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using OrderFlow.Order.Api.Security;
using OrderFlow.Order.Application.Orders.Commands.PlaceOrder;
using OrderFlow.Order.Application.Sagas;

public sealed record PlaceOrderLineRequest(string Sku, int Quantity, decimal UnitPrice);

public sealed record PlaceOrderRequest(string Currency, IReadOnlyList<PlaceOrderLineRequest> Lines);

[ApiController]
[Route("api/v1/orders")]
[Authorize(Policy = OrderPolicies.ReadOrders)]
public sealed class OrdersController(
    PlaceOrderCommandHandler placeOrder,
    GetOrderSagaStatusQueryHandler sagaStatus) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = OrderPolicies.PlaceOrder)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Place(
        PlaceOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (User.GetObjectId() is not { } customerId) { return Forbid(); }

        var command = new PlaceOrderCommand(
            customerId,
            request.Currency,
            [.. request.Lines.Select(l => new PlaceOrderLine(l.Sku, l.Quantity, l.UnitPrice))],
            idempotencyKey);

        var orderId = await placeOrder.HandleAsync(command, ct);

        return AcceptedAtAction(nameof(GetStatus), new { id = orderId }, new { orderId, status = "Pending" });
    }

    [HttpGet("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken ct)
    {
        var status = await sagaStatus.HandleAsync(id, ct);
        if (status is null) { return NotFound(); }

        var mayReadAny = User.IsInRole(OrderPolicies.SupportRole);
        if (!mayReadAny && status.CustomerId != User.GetObjectId()) { return NotFound(); }

        return Ok(status);
    }
}
