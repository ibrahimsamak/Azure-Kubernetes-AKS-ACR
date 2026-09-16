namespace OrderFlow.Order.Application.Orders.Commands.PlaceOrder;

public sealed record PlaceOrderLine(string Sku, int Quantity, decimal UnitPrice);

public sealed record PlaceOrderCommand(
    string CustomerId,
    string Currency,
    IReadOnlyList<PlaceOrderLine> Lines,
    string? IdempotencyKey = null);
