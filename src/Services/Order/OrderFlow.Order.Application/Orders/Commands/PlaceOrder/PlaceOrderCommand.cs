namespace OrderFlow.Order.Application.Orders.Commands.PlaceOrder;

/// <summary>One requested line. Primitives only — the aggregate turns these into OrderLines
/// and enforces the invariants (positive quantity, no duplicate SKU).</summary>
public sealed record PlaceOrderLine(string Sku, int Quantity, decimal UnitPrice);

public sealed record PlaceOrderCommand(
    string CustomerId,
    string Currency,
    IReadOnlyList<PlaceOrderLine> Lines);
