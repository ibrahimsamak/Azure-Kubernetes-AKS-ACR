namespace OrderFlow.Messaging.Kafka;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";

    public string ConsumerGroupId { get; set; } = "orderflow-service";

    public string[] ManagedTopics { get; set; } = [];

    public int DefaultPartitions { get; set; } = 3;
    public short ReplicationFactor { get; set; } = 1;

    public int MaxHandlerRetries { get; set; } = 3;
}
