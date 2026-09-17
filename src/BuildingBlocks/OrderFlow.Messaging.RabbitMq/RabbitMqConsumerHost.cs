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

        await channel.QueueDeclareAsync(options.Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = options.DeadLetterExchange,
                ["x-message-ttl"] = options.MessageTtlMs
            }, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(options.Queue, options.Exchange, string.Empty, cancellationToken: stoppingToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: options.PrefetchCount, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.Span);
                LogNotificationReceived(logger, json);

                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                LogNotificationFailed(logger, ex);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        await channel.BasicConsumeAsync(options.Queue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ notification: {Json}")]
    private static partial void LogNotificationReceived(ILogger logger, string json);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification failed; nacking to DLX.")]
    private static partial void LogNotificationFailed(ILogger logger, Exception exception);
}
