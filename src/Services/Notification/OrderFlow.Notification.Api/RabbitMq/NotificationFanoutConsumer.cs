namespace OrderFlow.Notification.Api.RabbitMq;

using OrderFlow.Messaging.RabbitMq;

public static class NotificationFanoutConsumer
{
    public const string ConnectionName = "rabbitmq";

    /// <summary>The publisher opens a connection, so it is built once and shared; the
    /// consumer host is the other half, draining notifications.email off the fanout.</summary>
    public static IServiceCollection AddNotificationFanout(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new RabbitMqOptions();
        configuration.GetSection("RabbitMq").Bind(options);

        // Aspire injects the broker address as ConnectionStrings:rabbitmq; anything bound
        // from configuration above wins over the default.
        var connectionString = configuration.GetConnectionString(ConnectionName);
        if (!string.IsNullOrWhiteSpace(connectionString)) { options.ConnectionString = connectionString; }

        services.AddSingleton(options);

        // CreateAsync is async, so the singleton is resolved through a factory rather than
        // blocking the container on a connection handshake.
        services.AddSingleton(_ => RabbitMqEventPublisher.CreateAsync(options).GetAwaiter().GetResult());

        services.AddHostedService(sp => new RabbitMqConsumerHost(
            options,
            sp.GetRequiredService<ILogger<RabbitMqConsumerHost>>()));

        return services;
    }
}
