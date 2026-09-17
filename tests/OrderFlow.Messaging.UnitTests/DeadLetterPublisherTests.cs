namespace OrderFlow.Messaging.UnitTests;

using Microsoft.Extensions.Logging.Abstractions;
using OrderFlow.Contracts;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Dispatch;
using OrderFlow.Messaging.Kafka;

public sealed class DeadLetterPublisherTests
{
    private const string SourceTopic = "orderflow.orders.v1";

    [Fact]
    public async Task A_parked_message_goes_to_the_dlq_topic_never_back_onto_the_source()
    {
        var publisher = new CapturingPublisher();

        await SendAsync(publisher, reason: "handler exploded");

        // Re-publishing onto the source topic would loop forever: consume, fail, republish.
        Assert.Equal(Topics.DeadLetter(SourceTopic), publisher.Topic);
        Assert.NotEqual(SourceTopic, publisher.Topic);
    }

    [Fact]
    public async Task The_original_key_and_payload_are_preserved_byte_for_byte()
    {
        var publisher = new CapturingPublisher();

        await SendAsync(publisher, reason: "handler exploded");

        // Same key = same partition when replayed, so a redrive keeps its ordering.
        Assert.Equal("order-123", publisher.Key);

        // The payload must survive untouched or the DLQ cannot be redriven at all.
        Assert.Equal("{\"the\":\"original envelope\"}", publisher.Value);
    }

    [Fact]
    public async Task The_original_headers_survive_and_the_diagnostic_ones_are_added()
    {
        var publisher = new CapturingPublisher();

        await SendAsync(publisher, reason: "handler exploded");

        // Whoever investigates needs the trace id that got it here.
        Assert.Equal("00-abc-def-01", publisher.Headers[KafkaHeaders.TraceParent]);
        Assert.Equal("OrderPlaced", publisher.Headers[KafkaHeaders.EventType]);

        Assert.Equal("handler exploded", publisher.Headers[KafkaHeaders.DeathReason]);
        Assert.Equal(SourceTopic, publisher.Headers[KafkaHeaders.DeathSource]);
        Assert.True(publisher.Headers.ContainsKey("dlq-timestamp"));
    }

    [Fact]
    public async Task A_huge_failure_reason_is_truncated_rather_than_rejected()
    {
        var publisher = new CapturingPublisher();

        // A stack trace can run to tens of kilobytes; Kafka rejects oversized headers, and
        // losing the message because the explanation was long would be the worst outcome.
        await SendAsync(publisher, reason: new string('x', 5_000));

        Assert.Equal(900, publisher.Headers[KafkaHeaders.DeathReason].Length);
    }

    private static Task SendAsync(IEventPublisher publisher, string reason)
    {
        var sut = new DeadLetterPublisher(publisher, NullLogger<DeadLetterPublisher>.Instance);

        return sut.SendAsync(
            SourceTopic,
            key: "order-123",
            value: "{\"the\":\"original envelope\"}",
            originalHeaders: new Dictionary<string, string>
            {
                [KafkaHeaders.TraceParent] = "00-abc-def-01",
                [KafkaHeaders.EventType] = "OrderPlaced"
            },
            reason: reason,
            ct: CancellationToken.None);
    }

    private sealed class CapturingPublisher : IEventPublisher
    {
        public string Topic { get; private set; } = string.Empty;
        public string Key { get; private set; } = string.Empty;
        public string Value { get; private set; } = string.Empty;
        public IReadOnlyDictionary<string, string> Headers { get; private set; } =
            new Dictionary<string, string>();

        public Task PublishAsync(IntegrationEvent @event, string partitionKey, CancellationToken ct = default) =>
            throw new InvalidOperationException("the DLQ must forward raw bytes, not re-serialize");

        public Task PublishRawAsync(string topic, string key, string value,
            IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
        {
            Topic = topic;
            Key = key;
            Value = value;
            Headers = headers;
            return Task.CompletedTask;
        }
    }
}
