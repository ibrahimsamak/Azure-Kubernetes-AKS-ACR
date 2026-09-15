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
    /// <param name="services">The service collection to register messaging into.</param>
    /// <param name="configuration">Configuration holding the Kafka connection string.</param>
    /// <param name="consumerName">Service name: the Kafka group id AND the Inbox discriminator.</param>
    /// <param name="subscribeTopics">Topics this service consumes.</param>
    /// <param name="ownedTopics">Topics this service produces to (auto-created on startup in dev).</param>
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
            // Aspire injects the broker address as ConnectionStrings:kafka.
            o.BootstrapServers = configuration.GetConnectionString("kafka") ?? "localhost:9092";
            o.ConsumerGroupId = consumerName;
            o.ManagedTopics = ownedTopics;
        });

        // Producer: singleton (thread-safe, batching, expensive to construct).
        services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
        services.AddSingleton<DeadLetterPublisher>();

        // Dispatcher: scoped, because it needs the scoped DbContext for the transaction.
        services.AddScoped(sp => new IntegrationEventDispatcher(
            sp,
            sp.GetRequiredService<TContext>(),
            consumerName,
            sp.GetRequiredService<ILogger<IntegrationEventDispatcher>>()));

        // Outbox dispatcher + topic provisioning + the consume loop.
        services.AddHostedService<KafkaTopicProvisioner>();
        services.AddHostedService(sp => new OutboxDispatcherService<TContext>(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<ILogger<OutboxDispatcherService<TContext>>>()));

        // Retention: deletes published outbox rows older than 7 days.
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
