namespace OrderFlow.Order.Domain.Sagas;

using OrderFlow.Order.Domain.Common;
using OrderFlow.Order.Domain.Sagas.Events;

public sealed class OrderSaga : Entity<Guid>
{
    private OrderSaga() { }

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

    public DateTime? DeadlineUtc { get; private set; }

    public Guid? ReservationId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public bool StockWasReserved { get; private set; }
    public bool PaymentWasCaptured { get; private set; }
    public string? FailureReason { get; private set; }

    public byte[] Version { get; private set; } = [];

    public static OrderSaga Start(Guid orderId, string customerId, decimal amount, string currency) => new(orderId, customerId, amount, currency);

    public void MarkOrderPlaced()
    {
        if (State != OrderSagaState.Pending)
        {
            return;
        }
        Transition(OrderSagaState.AwaitingStock, TimeSpan.FromSeconds(90));
    }

    public void OnStockReserved(Guid reservationId)
    {
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
            PaymentWasCaptured = true;
            PaymentId = paymentId;
            return;
        }
        if (State != OrderSagaState.AwaitingPayment) { return; }

        PaymentId = paymentId;
        PaymentWasCaptured = true;
        Transition(OrderSagaState.Paid, TimeSpan.FromSeconds(10));
    }

    public void Confirm()
    {
        if (State != OrderSagaState.Paid) { return; }
        State = OrderSagaState.Confirmed;
        DeadlineUtc = null;
        Raise(new OrderSagaCompletedDomainEvent(Id, CustomerId));
    }

    public void Fail(string reason)
    {
        if (State is OrderSagaState.Cancelled or OrderSagaState.Confirmed) { return; }
        if (State == OrderSagaState.Compensating) { return; }

        FailureReason = reason;
        State = OrderSagaState.Compensating;
        DeadlineUtc = DateTime.UtcNow.AddSeconds(60);

        Raise(new OrderSagaFailedDomainEvent(Id, CustomerId, reason, PaymentWasCaptured));
    }

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

    private void TryCompleteCompensation()
    {
        if (StockWasReserved || PaymentWasCaptured) { return; }
        State = OrderSagaState.Cancelled;
        DeadlineUtc = null;
        Raise(new OrderSagaCancelledDomainEvent(Id, CustomerId, FailureReason ?? "unknown"));
    }

    public void OnTimeout()
    {
        switch (State)
        {
            case OrderSagaState.AwaitingStock:
                Fail("Timed out waiting for inventory.");
                break;
            case OrderSagaState.AwaitingPayment:
                Fail("Timed out waiting for payment.");
                break;
            case OrderSagaState.Compensating:
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
