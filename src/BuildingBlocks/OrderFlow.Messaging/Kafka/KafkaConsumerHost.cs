namespace OrderFlow.Messaging.Kafka;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Messaging.Dispatch;
using OrderFlow.Messaging.Serialization;

public sealed partial class KafkaConsumerHost(
    IOptions<KafkaOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<KafkaConsumerHost> logger,
    string[] topics) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Factory.StartNew(() => ConsumeLoop(stoppingToken),
            stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private async Task ConsumeLoop(CancellationToken ct)
    {
        var o = options.Value;
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = o.BootstrapServers,

            GroupId = o.ConsumerGroupId,

            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,

            AutoOffsetReset = AutoOffsetReset.Earliest,

            MaxPollIntervalMs = 300_000,
            SessionTimeoutMs = 45_000,

            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
        })
        .SetErrorHandler((_, e) => LogConsumerError(logger, e.Reason))
        .SetPartitionsAssignedHandler((_, parts) => LogPartitionsAssigned(logger, parts))
        .SetPartitionsRevokedHandler((_, parts) => LogPartitionsRevoked(logger, parts))
        .Build();

        consumer.Subscribe(topics);
        LogSubscribed(logger, o.ConsumerGroupId, topics);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                }
                catch (ConsumeException ex)
                {
                    LogConsumeFailed(logger, ex);
                    await Task.Delay(1000, ct);
                    continue;
                }

                if (result?.Message is null) { continue; }

                var headers = ReadHeaders(result.Message.Headers);

                var parent = TraceContextPropagation.Extract(headers);
                using var activity = KafkaEventPublisher.ActivitySource.StartActivity($"consume {result.Topic}", ActivityKind.Consumer, parent);
                activity?.SetTag("messaging.system", "kafka");
                activity?.SetTag("messaging.source.name", result.Topic);
                activity?.SetTag("messaging.kafka.partition", result.Partition.Value);
                activity?.SetTag("messaging.kafka.offset", result.Offset.Value);

                await HandleWithRetriesAsync(consumer, result, headers, ct);

                try { consumer.Commit(result); }
                catch (KafkaException ex) { LogCommitFailed(logger, ex); }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task HandleWithRetriesAsync(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> result,
        Dictionary<string, string> headers,
        CancellationToken ct)
    {
        var o = options.Value;

        for (var attempt = 1; attempt <= o.MaxHandlerRetries; attempt++)
        {
            using var scope = scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IntegrationEventDispatcher>();

            try
            {
                MessageEnvelope? envelope;
                try
                {
                    envelope = MessageEnvelope.FromJson(result.Message.Value);
                }
                catch (JsonException)
                {
                    envelope = null;
                }

                if (envelope is null)
                {
                    await DeadLetterAsync(scope, result, headers, "malformed-envelope", ct);
                    return;
                }

                await dispatcher.DispatchAsync(envelope, ct);
                return;
            }
            catch (Exception ex) when (attempt < o.MaxHandlerRetries)
            {
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt));
                LogHandlerRetry(logger, ex, attempt, o.MaxHandlerRetries, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                LogHandlerFailedPermanently(logger, ex, result.Offset.Value);
                await DeadLetterAsync(scope, result, headers, ex.Message, ct);
                return;
            }
        }
    }

    private static async Task DeadLetterAsync(IServiceScope scope, ConsumeResult<string, string> result,
        Dictionary<string, string> headers, string reason, CancellationToken ct)
    {
        var dlq = scope.ServiceProvider.GetRequiredService<DeadLetterPublisher>();
        await dlq.SendAsync(result.Topic, result.Message.Key, result.Message.Value, headers, reason, ct);
    }

    private static Dictionary<string, string> ReadHeaders(Headers? headers) =>
        headers?.ToDictionary(h => h.Key, h => Encoding.UTF8.GetString(h.GetValueBytes()))
        ?? new Dictionary<string, string>();

    [LoggerMessage(Level = LogLevel.Error, Message = "Kafka consumer error: {Reason}")]
    private static partial void LogConsumerError(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Assigned partitions: {Partitions}")]
    private static partial void LogPartitionsAssigned(ILogger logger, IEnumerable<TopicPartition> partitions);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Revoked partitions: {Partitions}")]
    private static partial void LogPartitionsRevoked(ILogger logger, IEnumerable<TopicPartitionOffset> partitions);

    [LoggerMessage(Level = LogLevel.Information, Message = "Consumer group {Group} subscribed to {Topics}")]
    private static partial void LogSubscribed(ILogger logger, string group, IEnumerable<string> topics);

    [LoggerMessage(Level = LogLevel.Error, Message = "Consume failed; backing off.")]
    private static partial void LogConsumeFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Offset commit failed; message will be redelivered.")]
    private static partial void LogCommitFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handler failed (attempt {Attempt}/{Max}); retrying in {Delay}ms.")]
    private static partial void LogHandlerRetry(ILogger logger, Exception exception, int attempt, int max, double delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handler failed permanently; dead-lettering offset {Offset}.")]
    private static partial void LogHandlerFailedPermanently(ILogger logger, Exception exception, long offset);
}
