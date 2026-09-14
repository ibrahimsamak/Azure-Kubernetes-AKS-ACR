namespace OrderFlow.Messaging.Kafka;

public static class KafkaHeaders
{
    public const string MessageId = "message-id";
    public const string CorrelationId = "correlation-id";
    public const string EventType = "event-type";
    public const string TraceParent = "traceparent";   // W3C Trace Context
    public const string DeathReason = "dlq-reason";
    public const string DeathSource = "dlq-source-topic";
    public const string RetryCount = "retry-count";
}
