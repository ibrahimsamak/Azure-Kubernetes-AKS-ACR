namespace OrderFlow.Messaging.Kafka;
using Confluent.Kafka;

public enum KafkaAuthMode
{
    None,
    AzureAd
}

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";
    
    public string BootstrapServers { get; set; } = "localhost:9092";

    public string ConsumerGroupId { get; set; } = "orderflow-service";

    public string[] ManagedTopics { get; set; } = [];

    public int DefaultPartitions { get; set; } = 3;
    public short ReplicationFactor { get; set; } = 1;

    public int MaxHandlerRetries { get; set; } = 3;

    public KafkaAuthMode AuthMode { get; set; } = KafkaAuthMode.None;
    public bool ProvisionTopics { get; set; } = true;

    public CompressionType CompressionType { get; set; } = CompressionType.Snappy;
    public bool EnableIdempotence { get; set; } = true;
}
