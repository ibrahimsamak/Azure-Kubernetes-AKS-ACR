namespace OrderFlow.Order.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using OrderFlow.Order.Api.Security;
using OrderFlow.Order.Application.Orders.Commands.PlaceOrder;
using OrderFlow.Order.Application.Sagas;

/// <summary>The line the client sends. Kept separate from the command so the wire format
/// can change without touching the application layer.</summary>
public sealed record PlaceOrderLineRequest(string Sku, int Quantity, decimal UnitPrice);

/// <summary>No CustomerId: WHO is ordering comes from the validated token, never from the body.
/// (Old clients that still send "customerId" are fine — unknown JSON properties are ignored.)</summary>
public sealed record PlaceOrderRequest(string Currency, IReadOnlyList<PlaceOrderLineRequest> Lines);

[ApiController]
[Route("api/v1/orders")]
[Authorize(Policy = OrderPolicies.ReadOrders)]
public sealed class OrdersController(
    PlaceOrderCommandHandler placeOrder,
    GetOrderSagaStatusQueryHandler sagaStatus) : ControllerBase
{
    /// <summary>Accepts an order. The work completes asynchronously via the saga.</summary>
    [HttpPost]
    [Authorize(Policy = OrderPolicies.PlaceOrder)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Place(
        PlaceOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The token's oid: stable, unique per user in the tenant, and impossible for the caller to
        // choose. A customerId in the body would let any signed-in user order as anyone else.
        if (User.GetObjectId() is not { } customerId) { return Forbid(); }

        // Idempotency-Key stops a double-click creating two orders. Note it solves a
        // DIFFERENT problem from the message Inbox: that one dedups messages the broker
        // delivered twice, this one dedups client REQUESTS.
        var command = new PlaceOrderCommand(
            customerId,
            request.Currency,
            [.. request.Lines.Select(l => new PlaceOrderLine(l.Sku, l.Quantity, l.UnitPrice))],
            idempotencyKey);

        var orderId = await placeOrder.HandleAsync(command, ct);

        return AcceptedAtAction(nameof(GetStatus), new { id = orderId }, new { orderId, status = "Pending" });
    }

    /// <summary>Poll target for the client while the saga runs.</summary>
    [HttpGet("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken ct)
    {
        var status = await sagaStatus.HandleAsync(id, ct);
        if (status is null) { return NotFound(); }

        // Ownership. Support may read any order; a customer only their own. Someone else's order is
        // a 404, not a 403 — a 403 would confirm that the id exists.
        var mayReadAny = User.IsInRole(OrderPolicies.SupportRole);
        if (!mayReadAny && status.CustomerId != User.GetObjectId()) { return NotFound(); }

        return Ok(status);
    }
}
