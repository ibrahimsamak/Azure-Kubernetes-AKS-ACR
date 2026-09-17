namespace OrderFlow.Messaging.RabbitMq;

using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

public sealed class RabbitMqEventPublisher : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly string _exchange;

    private RabbitMqEventPublisher(IConnection c, IChannel ch, string exchange)
    {
        _connection = c;
        _channel = ch;
        _exchange = exchange;
    }

    public static async Task<RabbitMqEventPublisher> CreateAsync(RabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var factory = new ConnectionFactory { Uri = new Uri(options.ConnectionString) };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        // CONTRAST #1: topology is declared by the CONSUMER-facing side, and routing
        // happens in the broker. In Kafka, routing is the producer's choice of topic+key
        // and the consumer's choice of subscription — the broker does no routing at all.
        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Fanout, durable: true);
        return new RabbitMqEventPublisher(connection, channel, options.Exchange);
    }

    public async Task PublishAsync<T>(T message, CancellationToken ct = default)
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        await _channel.BasicPublishAsync(
          exchange: _exchange,
          routingKey: string.Empty,        // fanout ignores the routing key
          mandatory: false,
          basicProperties: new BasicProperties { Persistent = true, MessageId = Guid.CreateVersion7().ToString() },
          body: body,
          cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.CloseAsync();
        await _connection.CloseAsync();
    }
}
