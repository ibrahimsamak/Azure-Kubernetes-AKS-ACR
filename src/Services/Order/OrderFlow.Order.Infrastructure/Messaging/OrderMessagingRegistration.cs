using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderFlow.Contracts;
using OrderFlow.Contracts.Inventory;
using OrderFlow.Contracts.Payments;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.DependencyInjection;
using OrderFlow.Order.Application.Sagas;
using OrderFlow.Order.Infrastructure.Persistence;

namespace OrderFlow.Order.Infrastructure.Messaging;

public static class OrderMessagingRegistration
{
    /// <summary>One group per SERVICE, shared by all replicas, so they split partitions
    /// instead of each processing every message.</summary>
    public const string ConsumerGroupId = "orderflow-order";
    public static IHostApplicationBuilder AddOrderMessaging(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The saga drives the process, so Order both produces (Orders) and consumes the
        // answers it is waiting for (Inventory, Payments).
        builder.Services.AddOrderFlowMessaging<OrderDbContext>(
            builder.Configuration,
            consumerName: ConsumerGroupId,
            subscribeTopics: [Topics.Inventory, Topics.Payments],
            ownedTopics: [Topics.Orders]);

        // Resolved by IntegrationEventDispatcher via IIntegrationEventHandler<TEvent>.
        builder.Services.AddScoped<IIntegrationEventHandler<StockReserved>, StockReservedHandler>();
        builder.Services.AddScoped<IIntegrationEventHandler<StockReservationFailed>, StockReservationFailedHandler>();
        builder.Services.AddScoped<IIntegrationEventHandler<StockReleased>, StockReleasedHandler>();
        builder.Services.AddScoped<IIntegrationEventHandler<PaymentCaptured>, PaymentCapturedHandler>();
        builder.Services.AddScoped<IIntegrationEventHandler<PaymentFailed>, PaymentFailedHandler>();
        builder.Services.AddScoped<IIntegrationEventHandler<PaymentRefunded>, PaymentRefundedHandler>();

        return builder;
    }
}
