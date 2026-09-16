namespace OrderFlow.Inventory.Api.Domain;

public sealed class StockItem
{
    private readonly List<StockReservation> _reservations = [];

    private StockItem() { }

    public StockItem(string sku, int quantityOnHand)
    {
        Sku = sku;
        QuantityOnHand = quantityOnHand;
    }

    public string Sku { get; private set; } = default!;
    public int QuantityOnHand { get; private set; }

    public int QuantityReserved { get; private set; }

    public int Available => QuantityOnHand - QuantityReserved;

    public byte[] Version { get; private set; } = [];

    public IReadOnlyList<StockReservation> Reservations => _reservations.AsReadOnly();

    public bool TryReserve(Guid orderId, int quantity, out Guid reservationId)
    {
        var existing = _reservations.FirstOrDefault(r => r.OrderId == orderId && r.IsActive);
        if (existing is not null)
        {
            reservationId = existing.Id;
            return true;
        }

        if (Available < quantity)
        {
            reservationId = Guid.Empty;
            return false;
        }

        var reservation = new StockReservation(Guid.CreateVersion7(), orderId, quantity);
        _reservations.Add(reservation);
        QuantityReserved += quantity;
        reservationId = reservation.Id;
        return true;
    }

    public void Release(Guid orderId)
    {
        foreach (var reservation in _reservations.Where(r => r.OrderId == orderId && r.IsActive))
        {
            QuantityReserved -= reservation.Quantity;
            reservation.Release();
        }
    }
}
