namespace OrderFlow.IntegrationTests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Contracts.Orders;
using OrderFlow.IntegrationTests.Fixtures;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Outbox;
using OrderFlow.Messaging.Serialization;
using OrderFlow.Order.Infrastructure.Persistence;

[Collection(DistributedCollection.Name)]
public sealed class OutboxDispatcherTests(DistributedFixture fixture)
{
    [DockerFact]
    public async Task Pending_rows_are_published_and_marked_processed()
    {
        var orderId = Guid.CreateVersion7();
        var messageId = await StageOutboxRowAsync(orderId);

        var publisher = new RecordingEventPublisher();
        await RunDispatcherUntilAsync(publisher, () => !publisher.Published.IsEmpty);

        var published = Assert.Single(publisher.Published);
        var placed = Assert.IsType<OrderPlaced>(published.Event);
        Assert.Equal(orderId, placed.OrderId);

        // The partition key is what keeps one order's events ordered relative to each other.
        Assert.Equal(orderId.ToString(), published.PartitionKey);

        await using var db = fixture.CreateOrderDb();
        var row = await db.OutboxMessages.SingleAsync(m => m.Id == messageId);
        Assert.NotNull(row.ProcessedOnUtc);
        Assert.Null(row.LastError);
    }

    [DockerFact]
    public async Task A_processed_row_is_never_published_twice()
    {
        var messageId = await StageOutboxRowAsync(Guid.CreateVersion7());

        await using (var db = fixture.CreateOrderDb())
        {
            var row = await db.OutboxMessages.SingleAsync(m => m.Id == messageId);
            row.ProcessedOnUtc = DateTime.UtcNow;      // already sent on a previous run
            await db.SaveChangesAsync();
        }

        var publisher = new RecordingEventPublisher();
        await RunDispatcherForAsync(publisher, TimeSpan.FromSeconds(3));

        Assert.DoesNotContain(publisher.Published, p => p.Event is OrderPlaced e && e.MessageId == messageId);
    }

    [DockerFact]
    public async Task A_failed_publish_is_retried_with_backoff_and_the_row_is_kept()
    {
        var messageId = await StageOutboxRowAsync(Guid.CreateVersion7());

        // Fail the first attempt only. The row must survive, record the error, and be
        // scheduled for later — a permanently failing row is an alert, never a delete.
        var publisher = new RecordingEventPublisher { FailUntilAttempt = 1 };
        await RunDispatcherUntilAsync(publisher, () => publisher.Attempts > 0);

        await using var db = fixture.CreateOrderDb();
        var row = await db.OutboxMessages.SingleAsync(m => m.Id == messageId);

        Assert.Null(row.ProcessedOnUtc);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal("Broker unavailable.", row.LastError);
        Assert.NotNull(row.NextAttemptUtc);
        Assert.True(row.NextAttemptUtc > DateTime.UtcNow, "backoff must push the next attempt into the future");
    }

    private async Task<Guid> StageOutboxRowAsync(Guid orderId)
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

        // The saga tests share this database and leave their own pending rows behind. The
        // dispatcher takes a BATCH, so without this the "fail the first attempt" case would
        // spend its failure on somebody else's row and ours would sail through.
        await db.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedOnUtc, DateTime.UtcNow));

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = @event.MessageId,
            Type = EventTypeRegistry.NameOf(@event.GetType()),
            Content = MessageEnvelope.Wrap(@event).ToJson(),
            PartitionKey = orderId.ToString()
        });
        await db.SaveChangesAsync();

        return @event.MessageId;
    }

    private async Task RunDispatcherUntilAsync(IEventPublisher publisher, Func<bool> done)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var host = BuildDispatcher(publisher);

        await host.Service.StartAsync(cts.Token);
        try
        {
            while (!done() && !cts.IsCancellationRequested)
            {
                await Task.Delay(100, cts.Token);
            }
        }
        finally
        {
            await host.Service.StopAsync(CancellationToken.None);
        }

        Assert.True(done(), "the dispatcher did not reach the expected state in time");
    }

    private async Task RunDispatcherForAsync(IEventPublisher publisher, TimeSpan duration)
    {
        await using var host = BuildDispatcher(publisher);

        await host.Service.StartAsync(CancellationToken.None);
        await Task.Delay(duration);
        await host.Service.StopAsync(CancellationToken.None);
    }

    private DispatcherHost BuildDispatcher(IEventPublisher publisher)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddScoped(_ => fixture.CreateOrderDb());

        var provider = services.BuildServiceProvider();

        var service = new OutboxDispatcherService<OrderDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            provider.GetRequiredService<ILogger<OutboxDispatcherService<OrderDbContext>>>());

        return new DispatcherHost(provider, service);
    }

    private sealed record DispatcherHost(ServiceProvider Provider, OutboxDispatcherService<OrderDbContext> Service)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Service.Dispose();
            await Provider.DisposeAsync();
        }
    }
}
