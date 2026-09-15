namespace OrderFlow.Messaging.DependencyInjection;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Contracts;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Dispatch;
using OrderFlow.Messaging.Kafka;
using OrderFlow.Messaging.Outbox;
using OrderFlow.Messaging.Serialization;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddOrderFlowMessaging<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string consumerName,
        string[] subscribeTopics,
        string[] ownedTopics)
        where TContext : DbContext
    {
        EventTypeRegistry.RegisterAssembly(typeof(IntegrationEvent).Assembly);

        services.Configure<KafkaOptions>(o =>
        {
            o.BootstrapServers = configuration.GetConnectionString("kafka") ?? "localhost:9092";
            o.ConsumerGroupId = consumerName;
            o.ManagedTopics = ownedTopics;
        });

        services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
        services.AddSingleton<DeadLetterPublisher>();

        services.AddScoped(sp => new IntegrationEventDispatcher(
            sp,
            sp.GetRequiredService<TContext>(),
            consumerName,
            sp.GetRequiredService<ILogger<IntegrationEventDispatcher>>()));

        services.AddHostedService<KafkaTopicProvisioner>();
        services.AddHostedService(sp => new OutboxDispatcherService<TContext>(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<ILogger<OutboxDispatcherService<TContext>>>()));

        services.AddHostedService<OutboxCleanupService<TContext>>();

        if (subscribeTopics.Length > 0)
        {
            services.AddHostedService(sp => new KafkaConsumerHost(
                sp.GetRequiredService<IOptions<KafkaOptions>>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<KafkaConsumerHost>>(),
                subscribeTopics));
        }

        return services;
    }
}
