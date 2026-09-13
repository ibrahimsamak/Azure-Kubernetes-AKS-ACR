# Event storming — how we got to four services

> The artefact to show when an interviewer asks *"how did you decide on four services?"*
> Boundaries came from the timeline of events, not from the tables.

## The timeline

```
[Orange = domain event]  [Blue = command]  [Yellow = aggregate]  [Pink = external system]

  OrderPlaced → StockReserved → PaymentCaptured → OrderConfirmed → CustomerNotified
       ↑              ↑               ↑                 ↑                ↑
   PlaceOrder    ReserveStock   CapturePayment    ConfirmOrder     SendNotification
       ↑              ↑               ↑                 ↑                ↑
     Order        StockItem        Payment            Order         Notification
                                      ↑
                                 [PSP - Stripe]
```

## The three boundary questions, asked for every adjacent pair

1. **Do they change for the same reason?**
   Order changes for commerce rules; Payment changes when the PSP or PCI rules change.
   → different services.
2. **Do they need the same transaction?**
   Reserving stock and charging a card do not — the business is fine with
   "reserved, then charged 200ms later". → different services.
3. **Would one team own both?**
   Warehouse vs Finance. → different services.

## The resulting clusters

| Service      | Owns (data)                    | Answers                         | Changes because |
|--------------|--------------------------------|---------------------------------|-----------------|
| Order        | Order, OrderLine, saga state   | "what did the customer buy?"    | commerce rules  |
| Inventory    | StockItem, Reservation         | "is there stock, is it held?"   | warehouse rules |
| Payment      | Payment, refund records        | "did the money move?"           | PSP/compliance  |
| Notification | Notification log, preferences  | "was the customer told?"        | channel/vendor  |

## Seams, and what crosses them

| Seam | Direction | Mechanism | Why |
|---|---|---|---|
| Order → Inventory | question | gRPC `CheckAvailability`, 500ms deadline | caller cannot finish its own work without the answer — and it is **advisory**, never a reservation |
| Order → Inventory | fact | Kafka `OrderPlaced` | "this happened, react if you care" |
| Inventory → Order/Payment | fact | Kafka `StockReserved` / `StockReservationFailed` | no caller is blocked |
| Payment → Order | fact | Kafka `PaymentCaptured` / `PaymentFailed` | drives the saga transition |
| Order → all | compensation | Kafka `OrderCancelled` | one event, three compensations |

## What we deliberately did *not* do

- **One service per table** (OrderService, OrderLineService, CustomerService) — a distributed
  monolith: every use case becomes five network calls and a distributed transaction.
- **Order + Fulfilment as two services** — payment and warehouse fail for completely different
  reasons and live under different compliance regimes.
- **Modular monolith** — a legitimate and often better real-world answer; rejected here only
  because the learning goal is distributed-systems failure handling.

See [ADR-0004](adr/0004-service-boundaries-from-event-storming.md) for the decision record.

> **Attach the board.** Export the Miro/sticky-note photo next to this file as
> `event-storming-board.png` and link it here — the picture is what people remember.
