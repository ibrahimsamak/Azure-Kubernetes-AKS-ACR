
namespace OrderFlow.Order.Domain.Sagas;

using OrderFlow.Order.Domain.Common;
using OrderFlow.Order.Domain.Sagas.Events;

/// <summary>Tracks the distributed transaction behind one order. Keyed by OrderId so the
/// saga and the order it drives are always found by the same id.</summary>
public sealed class OrderSaga : Entity<Guid>
{
    private OrderSaga() { }   // EF

    private OrderSaga(Guid orderId, string customerId, decimal amount, string currency)
    {
        Id = orderId;
        CustomerId = customerId;
        Amount = amount;
        Currency = currency;
        State = OrderSagaState.Pending;
        StartedAtUtc = DateTime.UtcNow;
        DeadlineUtc = DateTime.UtcNow.AddSeconds(30);
    }

    public string CustomerId { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public OrderSagaState State { get; private set; }
    public DateTime StartedAtUtc { get; private set; }

    /// <summary>When the CURRENT step must have answered by. Null in terminal states.
    /// The timeout scanner queries exactly this column.</summary>
    public DateTime? DeadlineUtc { get; private set; }

    // Compensation bookkeeping: what actually happened, so we know what to undo.
    public Guid? ReservationId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public bool StockWasReserved { get; private set; }
    public bool PaymentWasCaptured { get; private set; }
    public string? FailureReason { get; private set; }

    /// <summary>Optimistic concurrency. Two Kafka partitions could deliver StockReserved and
    /// a timeout tick at the same moment; rowversion makes one of them lose and retry.</summary>
    public byte[] Version { get; private set; } = [];

    public static OrderSaga Start(Guid orderId, string customerId, decimal amount, string currency) => new(orderId, customerId, amount, currency);


    // ---------------- Forward transitions ----------------

    /// <summary>Called once the OrderPlaced outbox row has been written. Inventory answers
    /// first, so the next thing we wait for is stock — not payment.</summary>
    public void MarkOrderPlaced()
    {
        if (State != OrderSagaState.Pending)
        {
            return;
        }
        Transition(OrderSagaState.AwaitingStock, TimeSpan.FromSeconds(90));
    }

    // <summary>Inventory says the stock is held. Next step: take the money.</summary>
    public void OnStockReserved(Guid reservationId)
    {
        // GUARD + IDEMPOTENCE IN ONE PLACE.
        // - a duplicate delivery in AwaitingPayment: no-op, already moved on
        // - arriving while Compensating (a race with a timeout): we must NOT move forward;
        //   instead we now know there IS stock to release. Record it and stay compensating.

        if (State == OrderSagaState.Compensating)
        {
            StockWasReserved = true;
            ReservationId = reservationId;
            return;
        }
        if (State != OrderSagaState.AwaitingStock) { return; }

        ReservationId = reservationId;
        StockWasReserved = true;
        Transition(OrderSagaState.AwaitingPayment, TimeSpan.FromSeconds(30));
    }

    public void OnPaymentCaptured(Guid paymentId)
    {
        if (State == OrderSagaState.Compensating)
        {
            // Payment landed after we gave up. We owe the customer a refund.
            PaymentWasCaptured = true;
            PaymentId = paymentId;
            return;
        }
        if (State != OrderSagaState.AwaitingPayment) { return; }

        PaymentId = paymentId;
        PaymentWasCaptured = true;
        Transition(OrderSagaState.Paid, TimeSpan.FromSeconds(10));
    }


    /// <summary>Terminal success. Raises the event that tells the world (and Notification).</summary>
    public void Confirm()
    {
        if (State != OrderSagaState.Paid) { return; }
        State = OrderSagaState.Confirmed;
        DeadlineUtc = null;
        Raise(new OrderSagaCompletedDomainEvent(Id, CustomerId));
    }

    // ---------------- Failure and compensation ----------------
    // <summary>Entry point for EVERY failure: stock unavailable, payment declined, timeout.
    // One door into compensation means one place to reason about correctness.</summary>

    public void Fail(string reason)
    {
        if (State is OrderSagaState.Cancelled or OrderSagaState.Confirmed) { return; }  // terminal
        if (State == OrderSagaState.Compensating) { return; } // already undoing

        FailureReason = reason;
        State = OrderSagaState.Compensating;
        DeadlineUtc = DateTime.UtcNow.AddSeconds(60);   // compensations get their own deadline

        // ONE event triggers ALL compensations: Inventory releases, Payment refunds,
        // Notification apologises. Consumers decide what applies to them.
        Raise(new OrderSagaFailedDomainEvent(Id, CustomerId, reason, PaymentWasCaptured));
    }

    /// <summary>Inventory confirmed the release. If nothing else is outstanding, we are done.</summary>
    public void OnStockReleased()
    {
        if (State != OrderSagaState.Compensating) { return; }
        StockWasReserved = false;
        TryCompleteCompensation();
    }

    public void OnPaymentRefunded()
    {
        if (State != OrderSagaState.Compensating) { return; }
        PaymentWasCaptured = false;
        TryCompleteCompensation();
    }

    /// <summary>Only terminal once every compensation has ACKed. Flipping to Cancelled
    /// early would hide stock still held or money still taken.</summary>
    private void TryCompleteCompensation()
    {
        if (StockWasReserved || PaymentWasCaptured) { return; }
        State = OrderSagaState.Cancelled;
        DeadlineUtc = null;
        Raise(new OrderSagaCancelledDomainEvent(Id, CustomerId, FailureReason ?? "unknown"));
    }

    /// <summary>Called by the timeout scanner when DeadlineUtc passed.</summary>
    public void OnTimeout()
    {
        switch (State)
        {
            case OrderSagaState.AwaitingStock:
                Fail("Timed out waiting for inventory.");
                break;
            case OrderSagaState.AwaitingPayment:
                // DANGEROUS CASE: payment may have succeeded and the event is merely late.
                // We compensate, and the late PaymentCaptured handler above records
                // PaymentWasCaptured so a refund is still issued. Never assume silence = failure.
                Fail("Timed out waiting for payment.");
                break;
            case OrderSagaState.Paid:
                // Payment landed but confirmation did not follow. Nothing is owed to anyone
                // here — the work succeeded — so finish the job rather than compensate.
                // Without this branch the deadline stays in the past and the scanner
                // re-reads the same saga every 5 seconds forever.
                Confirm();
                break;

            case OrderSagaState.Compensating:
                // Compensation itself is stuck: escalate to a human, do NOT silently cancel.
                Raise(new OrderSagaStuckDomainEvent(Id, State.ToString(), FailureReason));
                DeadlineUtc = DateTime.UtcNow.AddMinutes(5);
                break;
        }
    }

    private void Transition(OrderSagaState next, TimeSpan deadline)
    {
        State = next;
        DeadlineUtc = DateTime.UtcNow.Add(deadline);
    }

}