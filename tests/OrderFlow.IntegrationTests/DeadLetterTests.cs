namespace OrderFlow.IntegrationTests;

using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Contracts;
using OrderFlow.Contracts.Orders;
using OrderFlow.IntegrationTests.Fixtures;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Dispatch;
using OrderFlow.Messaging.Kafka;
using OrderFlow.Messaging.Outbox;
using OrderFlow.Order.Infrastructure.Persistence;

[Collection(KafkaCollection.Name)]
public sealed class DeadLetterTests(KafkaFixture fixture)
{
    private const string ConsumerGroup = "dlq-test";

    private const string SharedPartitionKey = "dlq-partition";

    [DockerFact]
    public async Task A_poison_message_is_parked_and_the_partition_keeps_moving()
    {
        await QuarantineExistingOutboxAsync();

        var poisonId = await StageAsync(poison: true);
        var goodId = await StageAsync(poison: false);

        var handled = new ConcurrentQueue<Guid>();
        await using var host = BuildHost(handled);

        await host.StartAsync();
        Message<string, string>? parked;
        try
        {
            using var dlqReader = CreateDlqReader();

            parked = await ReadDlqAsync(dlqReader,
                m => m.Value.Contains(poisonId.ToString(), StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(60));

            await WaitUntilAsync(() => handled.Contains(goodId), TimeSpan.FromSeconds(30));
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.NotNull(parked);
        Assert.DoesNotContain(poisonId, handled);
        Assert.Contains(goodId, handled);

        var headers = ReadHeaders(parked!.Headers);
        Assert.Equal(Topics.Orders, headers[KafkaHeaders.DeathSource]);

        var reason = headers[KafkaHeaders.DeathReason];
        Assert.True(reason.Contains("poison", StringComparison.OrdinalIgnoreCase),
            $"the handler's failure must reach the DLQ header; got: '{reason}'");
        Assert.Equal(SharedPartitionKey, parked.Key);

        Assert.Contains(poisonId.ToString(), parked.Value, StringComparison.OrdinalIgnoreCase);
    }

    [DockerFact]
    public async Task Unparseable_bytes_are_parked_immediately_without_retrying()
    {
        await ProduceRawAsync("this is not a MessageEnvelope");

        const string Garbage = "this is not a MessageEnvelope";

        var handled = new ConcurrentQueue<Guid>();
        await using var host = BuildHost(handled);

        await host.StartAsync();
        Message<string, string>? parked;
        try
        {
            using var dlqReader = CreateDlqReader();
            parked = await ReadDlqAsync(dlqReader, m => m.Value == Garbage, TimeSpan.FromSeconds(60));
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.NotNull(parked);
        var headers = ReadHeaders(parked!.Headers);
        Assert.Equal("malformed-envelope", headers[KafkaHeaders.DeathReason]);
        Assert.Equal(Garbage, parked.Value);
    }

    private async Task<Guid> StageAsync(bool poison)
    {
        var orderId = Guid.CreateVersion7();

        var @event = new OrderPlaced
        {
            OrderId = orderId,
            CustomerId = poison ? "POISON" : "CUST-1",
            TotalAmount = 59.98m,
            Currency = "CAD",
            Lines = [new OrderLineDto("SKU-1", 2, 29.99m)],
            CorrelationId = orderId
        };

        await using var db = fixture.CreateOrderDb();
        new EfOutboxStore<OrderDbContext>(db).Enqueue(@event, SharedPartitionKey);
        await db.SaveChangesAsync();

        return orderId;
    }

    private async Task QuarantineExistingOutboxAsync()
    {
        await using var db = fixture.CreateOrderDb();
        await db.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedOnUtc, DateTime.UtcNow));
    }

    private async Task ProduceRawAsync(string value)
    {
        using var producer = new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = fixture.BootstrapServers }).Build();

        await producer.ProduceAsync(Topics.Orders,
            new Message<string, string> { Key = SharedPartitionKey, Value = value });
    }

    private IConsumer<string, string> CreateDlqReader()
    {
        var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = fixture.BootstrapServers,
            GroupId = $"{ConsumerGroup}-reader-{Guid.CreateVersion7()}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        }).Build();

        consumer.Subscribe(Topics.DeadLetter(Topics.Orders));
        return consumer;
    }

    private static async Task<Message<string, string>?> ReadDlqAsync(
        IConsumer<string, string> consumer, Func<Message<string, string>, bool> match, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var result = await Task.Run(() => consumer.Consume(TimeSpan.FromMilliseconds(500)));
            if (result?.Message is not null && match(result.Message)) { return result.Message; }
        }

        return null;
    }

    private static Dictionary<string, string> ReadHeaders(Headers? headers) =>
        headers?.ToDictionary(h => h.Key, h => Encoding.UTF8.GetString(h.GetValueBytes()))
        ?? [];

    private static async Task WaitUntilAsync(Func<bool> done, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!done() && !cts.IsCancellationRequested)
        {
            await Task.Delay(200, CancellationToken.None);
        }

        Assert.True(done(), $"the partition never moved past the poison message within {timeout.TotalSeconds:N0}s");
    }

    private TestHost BuildHost(ConcurrentQueue<Guid> handled)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Critical));

        services.Configure<KafkaOptions>(o =>
        {
            o.BootstrapServers = fixture.BootstrapServers;
            o.ConsumerGroupId = ConsumerGroup;
            o.ManagedTopics = [Topics.Orders];

            o.MaxHandlerRetries = 2;
        });

        services.AddScoped(_ => fixture.CreateOrderDb());
        services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
        services.AddSingleton<DeadLetterPublisher>();
        services.AddScoped<IIntegrationEventHandler<OrderPlaced>>(_ => new PoisonSensitiveHandler(handled));

        services.AddScoped(sp => new IntegrationEventDispatcher(
            sp,
            sp.GetRequiredService<OrderDbContext>(),
            ConsumerGroup,
            sp.GetRequiredService<ILogger<IntegrationEventDispatcher>>()));

        var provider = services.BuildServiceProvider();

        var provisioner = new KafkaTopicProvisioner(
            provider.GetRequiredService<IOptions<KafkaOptions>>(),
            provider.GetRequiredService<ILogger<KafkaTopicProvisioner>>());

        var publisher = new OutboxDispatcherService<OrderDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IEventPublisher>(),
            provider.GetRequiredService<ILogger<OutboxDispatcherService<OrderDbContext>>>());

        var consumer = new KafkaConsumerHost(
            provider.GetRequiredService<IOptions<KafkaOptions>>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<KafkaConsumerHost>>(),
            [Topics.Orders]);

        return new TestHost(provider, provisioner, publisher, consumer);
    }

    private sealed class PoisonSensitiveHandler(ConcurrentQueue<Guid> handled)
        : IIntegrationEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced @event, CancellationToken ct)
        {
            if (@event.CustomerId == "POISON")
            {
                throw new InvalidOperationException($"poison message {@event.OrderId}");
            }

            handled.Enqueue(@event.OrderId);
            return Task.CompletedTask;
        }
    }

    private sealed record TestHost(
        ServiceProvider Provider,
        KafkaTopicProvisioner Provisioner,
        OutboxDispatcherService<OrderDbContext> Publisher,
        KafkaConsumerHost Consumer) : IAsyncDisposable
    {
        public async Task StartAsync()
        {
            await Provisioner.StartAsync(CancellationToken.None);
            await Consumer.StartAsync(CancellationToken.None);
            await Publisher.StartAsync(CancellationToken.None);
        }

        public async Task StopAsync()
        {
            await Publisher.StopAsync(CancellationToken.None);
            await Consumer.StopAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            Publisher.Dispose();
            Consumer.Dispose();
            await Provider.DisposeAsync();
        }
    }
}
