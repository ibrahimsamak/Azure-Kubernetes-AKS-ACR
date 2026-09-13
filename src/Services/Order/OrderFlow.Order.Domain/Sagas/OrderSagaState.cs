namespace OrderFlow.Order.Domain.Sagas;

public enum OrderSagaState
{
    Pending = 0,
    AwaitingStock = 1,
    AwaitingPayment = 2,
    Paid = 3,
    Confirmed = 4,
    Compensating = 5,
    Cancelled = 6
}
