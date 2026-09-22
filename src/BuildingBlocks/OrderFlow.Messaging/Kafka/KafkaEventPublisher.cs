using Azure.Core;
using System.Diagnostics;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Contracts;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Serialization;

namespace OrderFlow.Messaging.Kafka;

public sealed partial class KafkaEventPublisher : IEventPublisher, IDisposable
{
    public static readonly ActivitySource ActivitySource = new("OrderFlow.Messeging");
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaEventPublisher> _logger;
    private int _disposed;

    public KafkaEventPublisher(IOptions<KafkaOptions> options, ILogger<KafkaEventPublisher> logger, TokenCredential? credential = null)
    {
        _logger = logger;
        var o = options.Value;

    var config = new ProducerConfig
    {
        Acks = Acks.All,
        EnableIdempotence = o.EnableIdempotence,
        MessageSendMaxRetries = 5,
        RetryBackoffMs = 200,
        MessageTimeoutMs = 30_000,
        LingerMs = 5,
        CompressionType = o.CompressionType
    };
    AzureKafkaAuth.Apply(config, o);

    var builder = new ProducerBuilder<string, string>(config)
        .SetLogHandler((_, m) => LogProducerClientMessage(_logger, m.Message));

    if (o.AuthMode == KafkaAuthMode.AzureAd)
    {
        ArgumentNullException.ThrowIfNull(credential);
        builder.SetOAuthBearerTokenRefreshHandler((client, _) => AzureKafkaAuth.RefreshToken(client, credential, o));
    }
    
    _producer = builder.Build();
    }

    public Task PublishAsync(IntegrationEvent @event, string partitionKey, CancellationToken ct = default)
    {
        var topic = Topics.For(@event.GetType());
        var envelope = MessageEnvelope.Wrap(@event);

        var headers = new Dictionary<string, string>
        {
            [KafkaHeaders.MessageId] = @event.MessageId.ToString(),
            [KafkaHeaders.CorrelationId] = @event.CorrelationId.ToString(),
            [KafkaHeaders.EventType] = envelope.Type,
        };

        using var activity = ActivitySource.StartActivity($"publish {topic}", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", topic);
        activity?.SetTag("messaging.message.id", @event.MessageId);
        TraceContextPropagation.Inject(Activity.Current, headers);

        return PublishRawAsync(topic, partitionKey, envelope.ToJson(), headers, ct);
    }

    public async Task PublishRawAsync(string topic, string key, string value,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        var message = new Message<string, string>
        {
            Key = key,
            Value = value,
            Headers = []
        };
        foreach (var (k, v) in headers)
        {
            message.Headers.Add(k, System.Text.Encoding.UTF8.GetBytes(v));
        }

        var result = await _producer.ProduceAsync(topic, message, ct);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            LogProduced(_logger, result.Topic, result.Partition.Value, result.Offset.Value, key);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) { return; }

        _producer.Flush(TimeSpan.FromSeconds(10));
        _producer.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Kafka producer: {Message}")]
    private static partial void LogProducerClientMessage(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Produced to {Topic}[{Partition}]@{Offset} key={Key}")]
    private static partial void LogProduced(ILogger logger, string topic, int partition, long offset, string key);
}
