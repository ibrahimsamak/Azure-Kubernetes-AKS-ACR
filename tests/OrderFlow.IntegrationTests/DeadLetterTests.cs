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

/// <summary>The "why did the queue stop?" incident, as a test.
///
/// A handler that always throws must not wedge its partition. The consumer retries it, parks
/// it on the DLQ, commits the offset and keeps going — and the crucial assertion is that a
/// GOOD message behind the poison one on the SAME partition still gets handled.</summary>
[Collection(KafkaCollection.Name)]
public sealed class DeadLetterTests(KafkaFixture fixture)
{
    private const string ConsumerGroup = "dlq-test";

    /// <summary>Both messages share this key, so Kafka puts them on the same partition and
    /// the poison one is genuinely in front of the good one.</summary>
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
            // Reader built AFTER the host, because the host provisions the DLQ topic and a
            // consumer that subscribes to a topic which does not exist yet will not notice
            // it appear until its next metadata refresh — minutes later, by default.
            using var dlqReader = CreateDlqReader();

            // The DLQ topic is shared with the other test in this class, so match on our own
            // message rather than taking whatever is at the head.
            parked = await ReadDlqAsync(dlqReader,
                m => m.Value.Contains(poisonId.ToString(), StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(60));

            // THE ASSERTION THAT MATTERS. Without the DLQ the consumer would retry the
            // poison message forever and `goodId` would never arrive.
            await WaitUntilAsync(() => handled.Contains(goodId), TimeSpan.FromSeconds(30));
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.NotNull(parked);
        Assert.DoesNotContain(poisonId, handled);         // it never succeeded
        Assert.Contains(goodId, handled);                 // and it did not block the one behind it

        var headers = ReadHeaders(parked!.Headers);
        Assert.Equal(Topics.Orders, headers[KafkaHeaders.DeathSource]);

        var reason = headers[KafkaHeaders.DeathReason];
        Assert.True(reason.Contains("poison", StringComparison.OrdinalIgnoreCase),
            $"the handler's failure must reach the DLQ header; got: '{reason}'");
        Assert.Equal(SharedPartitionKey, parked.Key);     // key preserved, so it can be redriven

        // The payload is forwarded raw, so the parked message is still a valid envelope.
        Assert.Contains(poisonId.ToString(), parked.Value, StringComparison.OrdinalIgnoreCase);
    }

    [DockerFact]
    public async Task Unparseable_bytes_are_parked_immediately_without_retrying()
    {
        // Malformed bytes will never become parseable, so retrying is pure latency.
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

    // ---------------- helpers ----------------

    /// <summary>A poison order is one the handler is rigged to throw on. Everything else about
    /// the message is ordinary — the failure has to come from the handler, not the transport.</summary>
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

    /// <summary>Park anything another test left pending. Done ONCE up front — doing it inside
    /// StageAsync would mark the poison row processed while staging the good one, and the
    /// poison message would never be published at all.</summary>
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
            // Consume blocks, so keep it off the test's thread.
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
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Critical));   // the failures are expected

        services.Configure<KafkaOptions>(o =>
        {
            o.BootstrapServers = fixture.BootstrapServers;
            o.ConsumerGroupId = ConsumerGroup;
            // Just the source topic, as production does — the provisioner adds the DLQ sibling.
            o.ManagedTopics = [Topics.Orders];

            // Two attempts, not the default three: the backoff is exponential and this test
            // should not spend its life waiting to prove a point it makes in one retry.
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
