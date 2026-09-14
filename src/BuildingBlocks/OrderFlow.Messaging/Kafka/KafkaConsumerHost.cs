namespace OrderFlow.Messaging.Kafka;

using System.Diagnostics;
using System.Text;
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
        // Kafka's consume loop is blocking. Run it on a dedicated long-running thread
        // instead of starving the thread pool.
        return Task.Factory.StartNew(() => ConsumeLoop(stoppingToken),
            stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private async Task ConsumeLoop(CancellationToken ct)
    {
        var o = options.Value;
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = o.BootstrapServers,

            // The group is the unit of scale AND the unit of offset bookkeeping.
            GroupId = o.ConsumerGroupId,

            // MANUAL COMMITS. With auto-commit, Kafka commits on a timer — a crash after
            // the commit but before the handler finishes SILENTLY LOSES the message.
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,

            // A brand-new group starts at the beginning of the log, so a service deployed
            // later still sees history. (Use Latest for pure "live" streams.)
            AutoOffsetReset = AutoOffsetReset.Earliest,

            // If we stop polling for longer than this, the broker assumes we died and
            // rebalances our partitions away. Keep handlers FAST; long work belongs elsewhere.
            MaxPollIntervalMs = 300_000,
            SessionTimeoutMs = 45_000,

            // Cooperative rebalancing: only the moving partitions pause, not the whole group.
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
        })
        .SetErrorHandler((_, e) => LogConsumerError(logger, e.Reason))
        // Collections are passed as-is: the logger formats them as "a, b, c" only if the level is enabled.
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

                if (result?.Message is null) { continue; }   // poll timeout, no message

                var headers = ReadHeaders(result.Message.Headers);

                // Continue the producer's trace instead of starting a fresh one.
                var parent = TraceContextPropagation.Extract(headers);
                using var activity = KafkaEventPublisher.ActivitySource.StartActivity($"consume {result.Topic}", ActivityKind.Consumer, parent);
                activity?.SetTag("messaging.system", "kafka");
                activity?.SetTag("messaging.source.name", result.Topic);
                activity?.SetTag("messaging.kafka.partition", result.Partition.Value);
                activity?.SetTag("messaging.kafka.offset", result.Offset.Value);

                await HandleWithRetriesAsync(consumer, result, headers, ct);

                // COMMIT LAST — after the DB transaction committed inside the dispatcher.
                // If we crash before this line, the message is redelivered and the Inbox
                // dedups it. That is at-least-once delivery + effectively-once processing.
                try { consumer.Commit(result); }
                catch (KafkaException ex) { LogCommitFailed(logger, ex); }
            }
        }
        finally
        {
            // Leave the group cleanly so the broker rebalances immediately instead of
            // waiting for the session timeout (a 45s stall for every deploy otherwise).
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
            // ONE DI SCOPE PER MESSAGE = one DbContext per message. Sharing a scoped
            // DbContext across messages leaks tracked entities between them.
            using var scope = scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IntegrationEventDispatcher>();

            try
            {
                var envelope = MessageEnvelope.FromJson(result.Message.Value);
                if (envelope is null)
                {
                    // Unparseable bytes will never become parseable. Do not retry; park it.
                    await DeadLetterAsync(scope, result, headers, "malformed-envelope", ct);
                    return;
                }

                await dispatcher.DispatchAsync(envelope, ct);
                return;
            }
            catch (Exception ex) when (attempt < o.MaxHandlerRetries)
            {
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt));  // 400, 800, 1600ms
                LogHandlerRetry(logger, ex, attempt, o.MaxHandlerRetries, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                // Final failure: park the POISON MESSAGE so the partition keeps moving.
                // Without a DLQ, one bad message blocks its whole partition forever —
                // this is the classic "why did the queue stop?" production incident.
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

    // Source-generated logging (CA1848): no boxing, no params array, and the template is parsed once.
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
