namespace OrderFlow.Messaging.Kafka;
using Confluent.Kafka;

public enum KafkaAuthMode
{
    /// <summary>PLAINTEXT, no auth — local Kafka container.</summary>
    None,
    /// <summary>SASL_SSL + OAUTHBEARER with an Entra ID token — Azure Event Hubs Kafka endpoint.</summary>
    AzureAd
}

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";
    /// <summary>Local: "localhost:9092". Event Hubs: "&lt;namespace&gt;.servicebus.windows.net:9093".</summary>
    
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


    // ---------------- NEW ----------------
    public KafkaAuthMode AuthMode { get; set; } = KafkaAuthMode.None;
    // <summary>
    // Create topics on startup. True locally for convenience; FALSE in Azure, where topics are
    // event hubs created by infra/provision.sh — apps shouldn't hold rights to create infrastructure.
    // </summary>
    public bool ProvisionTopics { get; set; } = true;

    // <summary>Snappy locally. Gzip on Event Hubs (the compression codec it documents support for).</summary>
    public CompressionType CompressionType { get; set; } = CompressionType.Snappy;
    /// <summary>Kept configurable: if a broker ever rejects idempotent producers, turn it off —
    /// the Inbox already makes consumers safe against the duplicates idempotence prevents.</summary>
    public bool EnableIdempotence { get; set; } = true;
}
