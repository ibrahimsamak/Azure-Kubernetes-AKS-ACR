using OrderFlow.Order.Domain.Common;

namespace OrderFlow.Order.Domain.Orders;

public sealed class OrderLine
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string Sku { get; private set; } = null!;
    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal LineTotal => Quantity * UnitPrice;

    private OrderLine() { }

    internal OrderLine(Guid orderId, string sku, int quantity, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(sku)) { throw new DomainException("SKU is required."); }
        if (quantity <= 0) { throw new DomainException($"Quantity for '{sku}' must be positive."); }
        if (unitPrice < 0) { throw new DomainException($"Unit price for '{sku}' cannot be negative."); }

        Id = Guid.CreateVersion7();
        OrderId = orderId;
        Sku = sku.Trim();
        Quantity = quantity;
        UnitPrice = unitPrice;
    }
}
