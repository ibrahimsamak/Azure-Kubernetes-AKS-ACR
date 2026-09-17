namespace OrderFlow.Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    public string ConnectionString { get; set; } = "amqp://guest:guest@localhost:5672";

    public string Exchange { get; set; } = "notifications";

    public string Queue { get; set; } = "notifications.email";

    public string DeadLetterExchange { get; set; } = "notifications.dlx";

    public int MessageTtlMs { get; set; } = 86_400_000;

    public ushort PrefetchCount { get; set; } = 10;
}
