namespace OrderFlow.Messaging.RabbitMq;

using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

public sealed partial class RabbitMqConsumerHost(RabbitMqOptions options, ILogger<RabbitMqConsumerHost> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory { Uri = new Uri(options.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Fanout, durable: true, cancellationToken: stoppingToken);

        // CONTRAST #2: dead-lettering is BUILT IN. In Kafka we wrote DeadLetterPublisher by hand.
        await channel.QueueDeclareAsync(options.Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = options.DeadLetterExchange,
                ["x-message-ttl"] = options.MessageTtlMs
            }, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(options.Queue, options.Exchange, string.Empty, cancellationToken: stoppingToken);

        // CONTRAST #3: prefetch controls in-flight work PER CONSUMER. Kafka's unit of
        // parallelism is the partition, which you must decide when creating the topic.
        // Here you just add another consumer process and the broker shares the queue out.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: options.PrefetchCount, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.Span);
                LogNotificationReceived(logger, json);
                // ... send the email ...

                // CONTRAST #4: ack is PER MESSAGE. Kafka commits an OFFSET, which implicitly
                // acks everything before it — so you cannot ack message 5 and leave 4 pending.
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                LogNotificationFailed(logger, ex);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        // CONTRAST #5: PUSH delivery — the broker pushes to us and tracks per-message state.
        // Kafka is PULL: we poll, and the broker tracks only a single integer per partition.
        // That is precisely why Kafka scales to millions of messages/sec and RabbitMQ gives
        // you richer per-message semantics.
        await channel.BasicConsumeAsync(options.Queue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ notification: {Json}")]
    private static partial void LogNotificationReceived(ILogger logger, string json);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification failed; nacking to DLX.")]
    private static partial void LogNotificationFailed(ILogger logger, Exception exception);
}
