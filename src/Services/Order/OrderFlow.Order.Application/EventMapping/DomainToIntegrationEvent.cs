using OrderFlow.Contracts;
using OrderFlow.Contracts.Orders;
using OrderFlow.Order.Domain.Common;
using OrderFlow.Order.Domain.Orders.Events;
using OrderFlow.Order.Domain.Sagas.Events;

namespace OrderFlow.Order.Application.EventMapping;

public static class DomainToIntegrationEvent
{
    public static IntegrationEvent? Map(IDomainEvent domainEvent) => domainEvent switch
    {
        OrderPlacedDomainEvent e => new OrderPlaced
        {
            OrderId = e.OrderId,
            CustomerId = e.CustomerId,
            TotalAmount = e.Total,
            Currency = e.Currency,
            Lines = e.Lines.Select(l => new OrderLineDto(l.Sku, l.Quantity, l.UnitPrice)).ToList(),
            CorrelationId = e.OrderId      // orderId correlates the whole saga
        },
        OrderConfirmedDomainEvent e => new OrderConfirmed
        {
            OrderId = e.OrderId,
            CustomerId = e.CustomerId,
            CorrelationId = e.OrderId
        },

        OrderCancelledDomainEvent e => new OrderCancelled
        {
            OrderId = e.OrderId,
            CustomerId = e.CustomerId,
            Reason = e.Reason,
            PaymentWasCaptured = e.PaymentWasCaptured,
            CorrelationId = e.OrderId
        },

        // Not a business fact, but it must leave the service: nothing else can tell an
        // operator that a compensation gave up half-way.
        OrderSagaStuckDomainEvent e => new OrderSagaStuck
        {
            OrderId = e.OrderId,
            State = e.State,
            FailureReason = e.FailureReason,
            CorrelationId = e.OrderId
        },

        // e.g. OrderSagaCompleted/Failed/Cancelled — internal bookkeeping that the Order
        // aggregate already announces publicly. Nobody outside cares about the saga itself.
        _ => null
    };
}