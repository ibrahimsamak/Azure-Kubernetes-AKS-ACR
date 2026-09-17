namespace OrderFlow.Messaging.RabbitMq;

/// <summary>Topology and tuning for the notification fanout. The publisher and the consumer
/// have to agree on every name here — declaring an exchange under one name and binding a
/// queue to another fails silently, with messages simply going nowhere.</summary>
public sealed class RabbitMqOptions
{
    /// <summary>Aspire injects this as ConnectionStrings:rabbitmq.</summary>
    public string ConnectionString { get; set; } = "amqp://guest:guest@localhost:5672";

    /// <summary>Fanout: every bound queue gets a copy, and the routing key is ignored.</summary>
    public string Exchange { get; set; } = "notifications";

    /// <summary>One queue per delivery channel. Add another queue on the same exchange and
    /// that channel starts receiving, with no change to the publisher.</summary>
    public string Queue { get; set; } = "notifications.email";

    /// <summary>Dead-lettering is built into the broker here — on the Kafka side we had to
    /// write DeadLetterPublisher by hand.</summary>
    public string DeadLetterExchange { get; set; } = "notifications.dlx";

    /// <summary>24h. A notification nobody delivered in a day is not worth sending.</summary>
    public int MessageTtlMs { get; set; } = 86_400_000;

    /// <summary>In-flight messages per consumer. Kafka's unit of parallelism is the
    /// partition, fixed when the topic is created; here you just add another consumer
    /// process and the broker shares the queue out.</summary>
    public ushort PrefetchCount { get; set; } = 10;
}
