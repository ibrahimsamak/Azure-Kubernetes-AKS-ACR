namespace OrderFlow.Order.UnitTests.Sagas;

using OrderFlow.Order.Domain.Sagas;
using OrderFlow.Order.Domain.Sagas.Events;

public sealed class OrderSagaTests
{
    private static OrderSaga Started()
    {
        var saga = OrderSaga.Start(Guid.CreateVersion7(), "CUST-1", 59.98m, "CAD");
        saga.MarkOrderPlaced();
        return saga;
    }

    private static OrderSaga AwaitingPayment()
    {
        var saga = Started();
        saga.OnStockReserved(Guid.CreateVersion7());
        return saga;
    }

    [Fact]
    public void A_new_saga_waits_for_stock_first()
    {
        var saga = Started();

        // Inventory answers before Payment. If this went straight to AwaitingPayment the
        // StockReserved guard would reject the very first step and every order would die
        // at the payment timeout.
        Assert.Equal(OrderSagaState.AwaitingStock, saga.State);
        Assert.NotNull(saga.DeadlineUtc);
    }

    [Fact]
    public void Marking_placed_twice_is_a_no_op()
    {
        var saga = Started();
        var deadline = saga.DeadlineUtc;

        saga.MarkOrderPlaced();

        Assert.Equal(OrderSagaState.AwaitingStock, saga.State);
        Assert.Equal(deadline, saga.DeadlineUtc);
    }

    [Fact]
    public void The_happy_path_reaches_Confirmed_and_announces_it()
    {
        var saga = AwaitingPayment();
        Assert.Equal(OrderSagaState.AwaitingPayment, saga.State);

        saga.OnPaymentCaptured(Guid.CreateVersion7());
        Assert.Equal(OrderSagaState.Paid, saga.State);

        saga.Confirm();

        Assert.Equal(OrderSagaState.Confirmed, saga.State);
        Assert.Null(saga.DeadlineUtc);                       // terminal: nothing left to wait for
        Assert.Contains(saga.DomainEvents, e => e is OrderSagaCompletedDomainEvent);
    }

    [Fact]
    public void A_duplicate_StockReserved_does_not_move_the_saga_on()
    {
        var saga = AwaitingPayment();
        var reservationId = saga.ReservationId;

        saga.OnStockReserved(Guid.CreateVersion7());          // at-least-once redelivery

        Assert.Equal(OrderSagaState.AwaitingPayment, saga.State);
        Assert.Equal(reservationId, saga.ReservationId);
    }

    [Fact]
    public void Failing_starts_compensation_once_and_only_once()
    {
        var saga = AwaitingPayment();

        saga.Fail("Payment declined.");
        saga.Fail("Something else.");                         // second failure must not re-raise

        Assert.Equal(OrderSagaState.Compensating, saga.State);
        Assert.Equal("Payment declined.", saga.FailureReason);
        Assert.Single(saga.DomainEvents.OfType<OrderSagaFailedDomainEvent>());
    }

    [Fact]
    public void Stock_reserved_during_compensation_is_recorded_not_advanced()
    {
        var saga = Started();
        saga.Fail("Timed out waiting for inventory.");

        // The reservation was in flight when we gave up. We must not move forward — but we
        // now know there IS stock to release.
        saga.OnStockReserved(Guid.CreateVersion7());

        Assert.Equal(OrderSagaState.Compensating, saga.State);
        Assert.True(saga.StockWasReserved);
    }

    [Fact]
    public void Payment_captured_during_compensation_still_owes_a_refund()
    {
        var saga = AwaitingPayment();
        saga.Fail("Timed out waiting for payment.");

        // The money moved after we gave up. Silence was never proof of failure.
        saga.OnPaymentCaptured(Guid.CreateVersion7());

        Assert.Equal(OrderSagaState.Compensating, saga.State);
        Assert.True(saga.PaymentWasCaptured);

        // Releasing the stock alone must NOT finish the saga while money is still taken.
        saga.OnStockReleased();
        Assert.Equal(OrderSagaState.Compensating, saga.State);

        saga.OnPaymentRefunded();
        Assert.Equal(OrderSagaState.Cancelled, saga.State);
    }

    [Fact]
    public void Cancellation_is_only_terminal_once_everything_has_ACKed()
    {
        var saga = AwaitingPayment();          // stock is held, no payment yet
        saga.Fail("Payment declined.");

        Assert.Equal(OrderSagaState.Compensating, saga.State);

        saga.OnStockReleased();

        Assert.Equal(OrderSagaState.Cancelled, saga.State);
        Assert.Null(saga.DeadlineUtc);
        Assert.Contains(saga.DomainEvents, e => e is OrderSagaCancelledDomainEvent);
    }

    [Fact]
    public void A_timeout_while_awaiting_payment_compensates_rather_than_assuming_failure()
    {
        var saga = AwaitingPayment();

        saga.OnTimeout();

        Assert.Equal(OrderSagaState.Compensating, saga.State);
        Assert.Contains(saga.DomainEvents, e => e is OrderSagaFailedDomainEvent);
    }

    [Fact]
    public void A_timeout_in_Paid_finishes_the_job_rather_than_spinning_forever()
    {
        var saga = AwaitingPayment();
        saga.OnPaymentCaptured(Guid.CreateVersion7());
        Assert.Equal(OrderSagaState.Paid, saga.State);

        saga.OnTimeout();

        // The money moved; there is nothing to compensate. Leaving the deadline in the past
        // would have the scanner re-read this saga every 5 seconds for ever.
        Assert.Equal(OrderSagaState.Confirmed, saga.State);
        Assert.Null(saga.DeadlineUtc);
    }

    [Fact]
    public void A_stuck_compensation_escalates_instead_of_silently_cancelling()
    {
        var saga = AwaitingPayment();
        saga.Fail("Payment declined.");
        saga.ClearDomainEvents();

        saga.OnTimeout();                      // compensation itself ran out of time

        // Cancelling here would claim the stock was released when it was not.
        Assert.Equal(OrderSagaState.Compensating, saga.State);
        Assert.Contains(saga.DomainEvents, e => e is OrderSagaStuckDomainEvent);
        Assert.NotNull(saga.DeadlineUtc);      // re-armed, so a human gets paged again
    }

    [Fact]
    public void Confirmed_and_Cancelled_are_terminal()
    {
        var confirmed = AwaitingPayment();
        confirmed.OnPaymentCaptured(Guid.CreateVersion7());
        confirmed.Confirm();
        confirmed.Fail("too late");
        Assert.Equal(OrderSagaState.Confirmed, confirmed.State);

        var cancelled = AwaitingPayment();
        cancelled.Fail("Payment declined.");
        cancelled.OnStockReleased();
        cancelled.Fail("too late");
        Assert.Equal(OrderSagaState.Cancelled, cancelled.State);
    }

    [Fact]
    public void Confirm_does_nothing_unless_the_money_actually_moved()
    {
        var saga = AwaitingPayment();

        saga.Confirm();                        // never reached Paid

        Assert.Equal(OrderSagaState.AwaitingPayment, saga.State);
        Assert.Empty(saga.DomainEvents.OfType<OrderSagaCompletedDomainEvent>());
    }
}
