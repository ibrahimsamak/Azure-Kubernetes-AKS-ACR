namespace OrderFlow.Order.Application.Orders.Commands.PlaceOrder;

/// <summary>One requested line. Primitives only — the aggregate turns these into OrderLines
/// and enforces the invariants (positive quantity, no duplicate SKU).</summary>
public sealed record PlaceOrderLine(string Sku, int Quantity, decimal UnitPrice);

/// <summary><paramref name="IdempotencyKey"/> comes from the Idempotency-Key header and is
/// optional. When present, replaying the same request returns the original order instead of
/// creating a second one.</summary>
public sealed record PlaceOrderCommand(
    string CustomerId,
    string Currency,
    IReadOnlyList<PlaceOrderLine> Lines,
    string? IdempotencyKey = null);
