namespace OrderFlow.Messaging.Kafka;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    /// <summary>Injected by Aspire as ConnectionStrings__kafka, e.g. "localhost:9092".</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Consumer group = the service name. One group per SERVICE, never per instance:
    /// instances of the same service must SHARE partitions, not each get a full copy.</summary>
    public string ConsumerGroupId { get; set; } = "orderflow-service";

    /// <summary>Topics this service creates on startup (dev only; prod uses IaC).</summary>
    public string[] ManagedTopics { get; set; } = [];

    public int DefaultPartitions { get; set; } = 3;      // 3 locally; size to peak consumers in prod
    public short ReplicationFactor { get; set; } = 1;    // 1 locally; >=3 in prod for durability

    /// <summary>How many times a handler is retried in-process before the message is dead-lettered.</summary>
    public int MaxHandlerRetries { get; set; } = 3;
}
