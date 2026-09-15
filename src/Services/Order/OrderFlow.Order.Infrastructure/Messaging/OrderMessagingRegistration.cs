using Microsoft.Extensions.Hosting;
using OrderFlow.Contracts;
using OrderFlow.Messaging.DependencyInjection;
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
        builder.Services.AddOrderFlowMessaging<OrderDbContext>(builder.Configuration, consumerName: ConsumerGroupId, subscribeTopics: [], ownedTopics: [Topics.Orders]);

        return builder;
    }
}
