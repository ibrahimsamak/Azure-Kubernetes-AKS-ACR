namespace OrderFlow.Inventory.Api.Domain;

public sealed class StockReservation
{
    private StockReservation() { }   // EF
    public StockReservation(Guid id, Guid orderId, int quantity)
        => (Id, OrderId, Quantity, IsActive, CreatedAtUtc) = (id, orderId, quantity, true, DateTime.UtcNow);

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public int Quantity { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void Release() => IsActive = false;   // soft-delete: the audit trail matters
}
