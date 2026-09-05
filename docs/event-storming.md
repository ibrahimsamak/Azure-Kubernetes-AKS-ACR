# Event storming

Notes from working out the service boundaries. I started from the events in the "place an
order" flow instead of from the database tables.

## Timeline

```
OrderPlaced -> StockReserved -> PaymentCaptured -> OrderConfirmed -> CustomerNotified
    |               |                |                  |                 |
PlaceOrder     ReserveStock    CapturePayment     ConfirmOrder     SendNotification
    |               |                |                  |                 |
  Order         StockItem         Payment             Order          Notification
                                     |
                              payment provider
```

## Questions I asked for each pair of steps

1. Do they change for the same reason? Order logic changes with business rules, payment changes
   when the provider or compliance rules change. So, separate.
2. Do they need to be in the same transaction? Reserving stock and charging the card don't. It's
   fine if the charge happens a bit after the reservation.
3. Would the same team own both? Warehouse and finance usually aren't the same team.

## Services

| Service | Owns | Answers |
|---|---|---|
| Order | orders, order lines, saga state | what did the customer buy? |
| Inventory | stock items, reservations | is there stock, is it held? |
| Payment | payments, refunds | did the money move? |
| Notification | notification log | was the customer told? |

## What goes between them

| From -> to | Type | How |
|---|---|---|
| Order -> Inventory | question | gRPC `CheckAvailability` (500ms deadline). Only a hint, never a reservation |
| Order -> others | event | Kafka `OrderPlaced` |
| Inventory -> Order/Payment | event | `StockReserved` / `StockReservationFailed` |
| Payment -> Order | event | `PaymentCaptured` / `PaymentFailed` |
| Order -> others | compensation | `OrderCancelled` |

## Things I didn't do

- One service per table. That just gives you a distributed monolith where every request is a
  bunch of network calls.
- Order and fulfilment as only two services. Payment and stock fail for different reasons, so I
  wanted them apart.
- A modular monolith. Honestly that would be fine for a real system this size, but the point here
  was to deal with the distributed failure cases.

See [ADR 0001](adr/0001-service-boundaries.md).
