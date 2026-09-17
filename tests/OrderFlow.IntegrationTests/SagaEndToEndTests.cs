namespace OrderFlow.IntegrationTests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Dispatch;
using OrderFlow.Messaging.Serialization;
using OrderFlow.Contracts.Inventory;
using OrderFlow.Contracts.Payments;
using OrderFlow.IntegrationTests.Fixtures;
using OrderFlow.Order.Application.Abstractions;
using OrderFlow.Order.Application.Orders.Commands.PlaceOrder;
using OrderFlow.Order.Application.Sagas;
using OrderFlow.Order.Domain.Orders;
using OrderFlow.Order.Domain.Sagas;
using OrderFlow.Order.Infrastructure.Persistence;
using OrderFlow.Order.Infrastructure.Persistence.Repositories;

/// <summary>Drives the Order side of the saga across real transactions, one delivery at a
/// time. Kafka is left out on purpose: what is worth asserting here is that each step
/// commits the saga, the order and the outbox together, not that a broker moves bytes.</summary>
[Collection(DistributedCollection.Name)]
public sealed class SagaEndToEndTests(DistributedFixture fixture)
{
    [DockerFact]
    public async Task A_placed_order_starts_a_saga_and_an_OrderPlaced_outbox_row_in_one_transaction()
    {
        var orderId = await PlaceOrderAsync();

        await using var db = fixture.CreateOrderDb();

        var saga = await db.OrderSagas.SingleAsync(s => s.Id == orderId);
        Assert.Equal(OrderSagaState.AwaitingStock, saga.State);

        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Pending, order.Status);

        // The interceptor must have staged the event in the SAME commit. If this row were
        // missing, the order would exist and nobody would ever hear about it.
        var outbox = await db.OutboxMessages.Where(m => m.PartitionKey == orderId.ToString()).ToListAsync();
        Assert.Contains(outbox, m => m.Type == "OrderPlaced");
    }

    [DockerFact]
    public async Task The_happy_path_walks_the_saga_to_Confirmed_and_places_the_order()
    {
        var orderId = await PlaceOrderAsync();

        await HandleAsync(async (sagas, orders) =>
        {
            await new StockReservedHandler(sagas, NullLogger<StockReservedHandler>.Instance)
                .HandleAsync(new StockReserved { OrderId = orderId, ReservationId = Guid.CreateVersion7() }, default);
        });

        await AssertStateAsync(orderId, OrderSagaState.AwaitingPayment);

        await HandleAsync(async (sagas, orders) =>
        {
            await new PaymentCapturedHandler(sagas, orders).HandleAsync(new PaymentCaptured
            {
                OrderId = orderId,
                PaymentId = Guid.CreateVersion7(),
                Amount = 59.98m,
                Currency = "CAD"
            }, default);
        });

        await using var db = fixture.CreateOrderDb();
        var saga = await db.OrderSagas.SingleAsync(s => s.Id == orderId);
        var order = await db.Orders.SingleAsync(o => o.Id == orderId);

        Assert.Equal(OrderSagaState.Confirmed, saga.State);
        Assert.Null(saga.DeadlineUtc);
        Assert.Equal(OrderStatus.Placed, order.Status);

        // The order aggregate raises its own event, so the world hears "confirmed" too.
        var outbox = await db.OutboxMessages.Where(m => m.PartitionKey == orderId.ToString()).ToListAsync();
        Assert.Contains(outbox, m => m.Type == "OrderConfirmed");
    }

    [DockerFact]
    public async Task A_redelivered_StockReserved_does_not_advance_the_saga_twice()
    {
        var orderId = await PlaceOrderAsync();
        var reservationId = Guid.CreateVersion7();

        for (var delivery = 0; delivery < 2; delivery++)
        {
            await HandleAsync(async (sagas, orders) =>
            {
                await new StockReservedHandler(sagas, NullLogger<StockReservedHandler>.Instance)
                    .HandleAsync(new StockReserved { OrderId = orderId, ReservationId = reservationId }, default);
            });
        }

        await using var db = fixture.CreateOrderDb();
        var saga = await db.OrderSagas.SingleAsync(s => s.Id == orderId);

        Assert.Equal(OrderSagaState.AwaitingPayment, saga.State);
        Assert.Equal(reservationId, saga.ReservationId);
    }

    [DockerFact]
    public async Task A_stock_failure_compensates_and_cancels_the_order_once_the_release_is_ACKed()
    {
        var orderId = await PlaceOrderAsync();

        await HandleAsync(async (sagas, orders) =>
        {
            await new StockReservationFailedHandler(sagas, orders).HandleAsync(new StockReservationFailed
            {
                OrderId = orderId,
                Sku = "SKU-OOS",
                Reason = "Insufficient stock"
            }, default);
        });

        await AssertStateAsync(orderId, OrderSagaState.Compensating);

        // THE THING THAT MAKES COMPENSATION HAPPEN. Failing the saga has to put a public
        // event on the wire, or Inventory is never told to release and Payment is never told
        // to refund — and the StockReleased fed in below would never arrive in production.
        await using (var check = fixture.CreateOrderDb())
        {
            var staged = await check.OutboxMessages
                .Where(m => m.PartitionKey == orderId.ToString())
                .Select(m => m.Type)
                .ToListAsync();

            Assert.Contains("OrderCancelled", staged);
        }

        await HandleAsync(async (sagas, orders) =>
        {
            await new StockReleasedHandler(sagas, orders).HandleAsync(new StockReleased
            {
                OrderId = orderId,
                ReservationId = Guid.Empty
            }, default);
        });

        await using var db = fixture.CreateOrderDb();
        var saga = await db.OrderSagas.SingleAsync(s => s.Id == orderId);
        var order = await db.Orders.SingleAsync(o => o.Id == orderId);

        Assert.Equal(OrderSagaState.Cancelled, saga.State);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Contains("SKU-OOS", saga.FailureReason);
    }

    [DockerFact]
    public async Task A_retryable_payment_failure_leaves_the_saga_where_it_is()
    {
        var orderId = await PlaceOrderAsync();

        await HandleAsync(async (sagas, orders) =>
        {
            await new StockReservedHandler(sagas, NullLogger<StockReservedHandler>.Instance)
                .HandleAsync(new StockReserved { OrderId = orderId, ReservationId = Guid.CreateVersion7() }, default);
        });

        await HandleAsync(async (sagas, orders) =>
        {
            await new PaymentFailedHandler(sagas, orders, NullLogger<PaymentFailedHandler>.Instance)
                .HandleAsync(new PaymentFailed
                {
                    OrderId = orderId,
                    Reason = "Gateway timeout.",
                    IsRetryable = true
                }, default);
        });

        // Payment retries on its own; giving up here would cancel an order that is still
        // perfectly capable of succeeding. The saga deadline is the backstop.
        await AssertStateAsync(orderId, OrderSagaState.AwaitingPayment);
    }

    [DockerFact]
    public async Task A_message_dispatches_even_though_the_context_retries_on_transient_faults()
    {
        var orderId = await PlaceOrderAsync();

        var @event = new StockReserved
        {
            OrderId = orderId,
            ReservationId = Guid.CreateVersion7(),
            CorrelationId = orderId
        };

        await using var db = fixture.CreateRetryingOrderDb();

        var services = new ServiceCollection();
        services.AddScoped<IIntegrationEventHandler<StockReserved>>(_ =>
            new StockReservedHandler(new OrderSagaRepository(db), NullLogger<StockReservedHandler>.Instance));

        await using var provider = services.BuildServiceProvider();

        var dispatcher = new IntegrationEventDispatcher(
            provider, db, "order-service", NullLogger<IntegrationEventDispatcher>.Instance);

        // The dispatcher opens its own transaction to commit the inbox claim with the
        // business change. EF refuses a user-initiated transaction under a retrying
        // execution strategy unless the whole unit is wrapped in that strategy.
        await dispatcher.DispatchAsync(MessageEnvelope.Wrap(@event), CancellationToken.None);

        await using var check = fixture.CreateOrderDb();
        var saga = await check.OrderSagas.SingleAsync(s => s.Id == orderId);
        Assert.Equal(OrderSagaState.AwaitingPayment, saga.State);

        var claimed = await check.InboxMessages
            .AnyAsync(i => i.MessageId == @event.MessageId && i.Consumer == "order-service");
        Assert.True(claimed, "the inbox claim must commit with the saga change");
    }

    [DockerFact]
    public async Task The_same_Idempotency_Key_returns_the_first_order_instead_of_creating_a_second()
    {
        var key = $"key-{Guid.CreateVersion7()}";

        var first = await PlaceOrderAsync(key);
        var second = await PlaceOrderAsync(key);   // the double-click

        Assert.Equal(first, second);

        await using var db = fixture.CreateOrderDb();
        var ordersForKey = await db.Orders.CountAsync(o => o.Id == first);
        Assert.Equal(1, ordersForKey);

        // And exactly one saga, or the second order would be driving a parallel process.
        Assert.Equal(1, await db.OrderSagas.CountAsync(s => s.Id == first));
    }

    [DockerFact]
    public async Task Two_simultaneous_requests_with_one_key_produce_exactly_one_order()
    {
        var key = $"key-{Guid.CreateVersion7()}";

        // Separate contexts = separate transactions, which is what two web requests are.
        // Both will pass the cheap read; the primary key decides the winner.
        await using var dbA = fixture.CreateOrderDb();
        await using var dbB = fixture.CreateOrderDb();

        var results = await Task.WhenAll(
            PlaceOrderAsync(dbA, key),
            PlaceOrderAsync(dbB, key));

        Assert.Equal(results[0], results[1]);

        await using var check = fixture.CreateOrderDb();
        var keyRows = await check.IdempotentRequests.CountAsync(r => r.Key == key);
        Assert.Equal(1, keyRows);

        // The loser's order, lines, saga and outbox row all rolled back with its transaction.
        Assert.Equal(1, await check.OrderSagas.CountAsync(s => s.Id == results[0]));
    }

    // ---------------- helpers ----------------

    private async Task<Guid> PlaceOrderAsync(string? idempotencyKey = null)
    {
        await using var db = fixture.CreateOrderDb();
        return await PlaceOrderAsync(db, idempotencyKey);
    }

    private static async Task<Guid> PlaceOrderAsync(OrderDbContext db, string? idempotencyKey)
    {
        var handler = new PlaceOrderCommandHandler(
            new OrderRepository(db),
            new OrderSagaRepository(db),
            new UnitOfWork(db),
            new AlwaysAvailableInventory(),
            new EfIdempotencyStore(db),
            NullLogger<PlaceOrderCommandHandler>.Instance);

        return await handler.HandleAsync(new PlaceOrderCommand("CUST-1", "CAD",
            [new PlaceOrderLine("SKU-1", 2, 29.99m)], idempotencyKey), default);
    }

    /// <summary>One delivery = one transaction. The handlers deliberately do not save; in
    /// production IntegrationEventDispatcher commits them with the inbox row, and here the
    /// test plays that part.</summary>
    private async Task HandleAsync(Func<IOrderSagaRepository, IOrderRepository, Task> handle)
    {
        await using var db = fixture.CreateOrderDb();
        await handle(new OrderSagaRepository(db), new OrderRepository(db));
        await db.SaveChangesAsync();
    }

    private async Task AssertStateAsync(Guid orderId, OrderSagaState expected)
    {
        await using var db = fixture.CreateOrderDb();
        var saga = await db.OrderSagas.SingleAsync(s => s.Id == orderId);
        Assert.Equal(expected, saga.State);
    }

    private sealed class AlwaysAvailableInventory : IInventoryQueryClient
    {
        public Task<AvailabilityResult> CheckAvailabilityAsync(
            IReadOnlyList<(string Sku, int Quantity)> lines, CancellationToken ct) =>
            Task.FromResult(new AvailabilityResult(Known: true, AllAvailable: true, FirstUnavailableSku: null));
    }
}
