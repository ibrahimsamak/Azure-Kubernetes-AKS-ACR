using OrderFlow.Contracts;
using OrderFlow.Contracts.Orders;
using OrderFlow.Order.Domain.Common;
using OrderFlow.Order.Domain.Orders.Events;

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

        // e.g. OrderLineAddedDomainEvent — internal bookkeeping. Nobody outside cares.
        _ => null
    };
}