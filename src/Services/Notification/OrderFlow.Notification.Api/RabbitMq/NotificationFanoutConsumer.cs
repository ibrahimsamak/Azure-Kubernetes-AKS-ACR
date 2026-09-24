namespace OrderFlow.Notification.Api.RabbitMq;

using OrderFlow.Messaging.RabbitMq;

public static class NotificationFanoutConsumer
{
    public const string ConnectionName = "rabbitmq";

    public static IServiceCollection AddNotificationFanout(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new RabbitMqOptions();
        configuration.GetSection("RabbitMq").Bind(options);

        var connectionString = configuration.GetConnectionString(ConnectionName);
        if (!string.IsNullOrWhiteSpace(connectionString)) { options.ConnectionString = connectionString; }

        services.AddSingleton(options);

        services.AddSingleton(_ => RabbitMqEventPublisher.CreateAsync(options).GetAwaiter().GetResult());

        services.AddHostedService(sp => new RabbitMqConsumerHost(
            options,
            sp.GetRequiredService<ILogger<RabbitMqConsumerHost>>()));

        return services;
    }
}
