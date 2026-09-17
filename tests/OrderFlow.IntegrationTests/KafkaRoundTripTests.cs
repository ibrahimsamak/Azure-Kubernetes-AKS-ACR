namespace OrderFlow.IntegrationTests;

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Contracts.Orders;
using OrderFlow.IntegrationTests.Fixtures;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Dispatch;
using OrderFlow.Messaging.Kafka;
using OrderFlow.Messaging.Outbox;
using OrderFlow.Order.Infrastructure.Persistence;

/// <summary>The one test that crosses the broker. Everything else stubs the publisher, which
/// is right for asserting saga behaviour but proves nothing about serialization, topic
/// routing, partition keys, offset commits or the consumer wiring — the parts that only
/// break against a real Kafka.</summary>
[Collection(KafkaCollection.Name)]
public sealed class KafkaRoundTripTests(KafkaFixture fixture)
{
    [DockerFact]
    public async Task An_outbox_row_reaches_a_consumer_and_is_claimed_in_the_inbox()
    {
        var orderId = Guid.CreateVersion7();
        var messageId = await StageOrderPlacedAsync(orderId);

        var received = new ConcurrentQueue<OrderPlaced>();
        await using var host = BuildHost(received);

        // The full path: outbox row → dispatcher → Kafka → consumer host → event dispatcher
        // → handler → inbox claim. No step is faked.
        await host.StartAsync();
        try
        {
            await WaitUntilAsync(() => !received.IsEmpty, TimeSpan.FromSeconds(60));
        }
        finally
        {
            await host.StopAsync();
        }

        var delivered = Assert.Single(received);
        Assert.Equal(orderId, delivered.OrderId);
        Assert.Equal(messageId, delivered.MessageId);           // identity survives the wire
        Assert.Equal("CUST-1", delivered.CustomerId);
        Assert.Equal(59.98m, delivered.TotalAmount);            // decimals survive it too
        Assert.Equal("SKU-1", Assert.Single(delivered.Lines).Sku);

        await using var db = fixture.CreateOrderDb();

        // Published, so the dispatcher must have marked it — otherwise it would be
        // re-published forever.
        var outboxRow = await db.OutboxMessages.SingleAsync(m => m.Id == messageId);
        Assert.NotNull(outboxRow.ProcessedOnUtc);

        // And the consumer side must have claimed it, or a redelivery would run twice.
        var claimed = await db.InboxMessages
            .AnyAsync(i => i.MessageId == messageId && i.Consumer == ConsumerGroup);
        Assert.True(claimed, "the inbox claim must commit with the handler's work");
    }

    private const string ConsumerGroup = "kafka-roundtrip-test";

    private async Task<Guid> StageOrderPlacedAsync(Guid orderId)
    {
        var @event = new OrderPlaced
        {
            OrderId = orderId,
            CustomerId = "CUST-1",
            TotalAmount = 59.98m,
            Currency = "CAD",
            Lines = [new OrderLineDto("SKU-1", 2, 29.99m)],
            CorrelationId = orderId
        };

        await using var db = fixture.CreateOrderDb();

        // Only ours should be pending, so the assertions cannot be satisfied by a leftover.
        await db.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedOnUtc, DateTime.UtcNow));

        new EfOutboxStore<OrderDbContext>(db).Enqueue(@event, orderId.ToString());
        await db.SaveChangesAsync();

        return @event.MessageId;
    }

    private TestHost BuildHost(ConcurrentQueue<OrderPlaced> received)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        services.Configure<KafkaOptions>(o =>
        {
            o.BootstrapServers = fixture.BootstrapServers;
            o.ConsumerGroupId = ConsumerGroup;
            o.ManagedTopics = [Contracts.Topics.Orders];
        });

        services.AddScoped(_ => fixture.CreateOrderDb());
        services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
        services.AddSingleton<DeadLetterPublisher>();

        // The handler under test just records what arrived; the point here is the transport,
        // not the business logic (which the SQL-only suite already covers).
        services.AddScoped<IIntegrationEventHandler<OrderPlaced>>(_ => new RecordingHandler(received));

        services.AddScoped(sp => new IntegrationEventDispatcher(
            sp,
            sp.GetRequiredService<OrderDbContext>(),
            ConsumerGroup,
            sp.GetRequiredService<ILogger<IntegrationEventDispatcher>>()));

        var provider = services.BuildServiceProvider();

        var publisher = new OutboxDispatcherService<OrderDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IEventPublisher>(),
            provider.GetRequiredService<ILogger<OutboxDispatcherService<OrderDbContext>>>());

        var consumer = new KafkaConsumerHost(
            provider.GetRequiredService<IOptions<KafkaOptions>>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<KafkaConsumerHost>>(),
            [Contracts.Topics.Orders]);

        var provisioner = new KafkaTopicProvisioner(
            provider.GetRequiredService<IOptions<KafkaOptions>>(),
            provider.GetRequiredService<ILogger<KafkaTopicProvisioner>>());

        return new TestHost(provider, provisioner, publisher, consumer);
    }

    private static async Task WaitUntilAsync(Func<bool> done, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!done() && !cts.IsCancellationRequested)
        {
            await Task.Delay(200, CancellationToken.None);
        }

        Assert.True(done(), $"nothing arrived through Kafka within {timeout.TotalSeconds:N0}s");
    }

    private sealed class RecordingHandler(ConcurrentQueue<OrderPlaced> received)
        : IIntegrationEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced @event, CancellationToken ct)
        {
            received.Enqueue(@event);
            return Task.CompletedTask;
        }
    }

    /// <summary>Starts the provisioner first — the topic has to exist before either side
    /// touches it — then the producer and consumer.</summary>
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
