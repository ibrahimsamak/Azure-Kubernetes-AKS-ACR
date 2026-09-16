namespace OrderFlow.Inventory.Api.Domain;

public sealed class StockItem
{
    private readonly List<StockReservation> _reservations = [];

    private StockItem() { }   // EF

    public StockItem(string sku, int quantityOnHand)
    {
        Sku = sku;
        QuantityOnHand = quantityOnHand;
    }

    public string Sku { get; private set; } = default!;
    public int QuantityOnHand { get; private set; }

    /// <summary>Held for in-flight sagas; not yet shipped, not available to others.</summary>
    public int QuantityReserved { get; private set; }

    public int Available => QuantityOnHand - QuantityReserved;

    public byte[] Version { get; private set; } = [];   // rowversion: two orders racing for the last unit

    public IReadOnlyList<StockReservation> Reservations => _reservations.AsReadOnly();

    /// <returns>false if there is not enough stock — the caller emits StockReservationFailed.</returns>
    public bool TryReserve(Guid orderId, int quantity, out Guid reservationId)
    {
        // IDEMPOTENCE: this exact order already holds a reservation. Return the SAME id,
        // so a duplicate delivery produces a byte-identical StockReserved event.
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

    /// <summary>COMPENSATION. Idempotent: releasing twice must not credit stock twice —
    /// that would be a phantom-inventory bug, which is worse than losing an order.</summary>
    public void Release(Guid orderId)
    {
        foreach (var reservation in _reservations.Where(r => r.OrderId == orderId && r.IsActive))
        {
            QuantityReserved -= reservation.Quantity;
            reservation.Release();
        }
    }
}

