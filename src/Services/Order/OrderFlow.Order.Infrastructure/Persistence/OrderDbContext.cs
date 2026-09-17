namespace OrderFlow.Order.Infrastructure.Persistence;

// Inside the namespace so `Order` resolves to the aggregate, not the OrderFlow.Order namespace.
using Microsoft.EntityFrameworkCore;
using OrderFlow.Messaging.Inbox;
using OrderFlow.Messaging.Outbox;
using OrderFlow.Order.Domain.Orders;
using OrderFlow.Order.Domain.Sagas;
using OrderFlow.Order.Infrastructure.Persistence.Repositories;

/// <summary>The Order service's single database. Business tables and the outbox live here
/// TOGETHER — that co-location is what lets one SaveChanges commit both atomically.</summary>
public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public const string ConnectionName = "orderflow-orders";   // must match AppHost's AddDatabase name

    public DbSet<Order> Orders => Set<Order>();

    /// <summary>Saga state lives in the SAME database as the orders, so advancing the saga
    /// and updating the order is one local transaction — not a second distributed one.</summary>
    public DbSet<OrderSaga> OrderSagas => Set<OrderSaga>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>The dedup claim IntegrationEventDispatcher writes for every message it
    /// handles. Without it in the model, consuming anything throws.</summary>
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <summary>Client-request dedup (Idempotency-Key). Lives here so the key and the order
    /// it created commit in one transaction.</summary>
    public DbSet<IdempotentRequest> IdempotentRequests => Set<IdempotentRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Default schema stays dbo: the dispatcher and cleanup SQL reference OutboxMessages unqualified.
        // Both configurations live in OrderFlow.Messaging, so the assembly scan below — which
        // only looks at THIS assembly — will never find them.
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        modelBuilder.Entity<IdempotentRequest>(b =>
        {
            b.ToTable("IdempotentRequests");
            b.HasKey(x => x.Key);                     // the PK is the dedup guarantee
            b.Property(x => x.Key).HasMaxLength(100);
        });

        // Picks up every IEntityTypeConfiguration in this assembly (OrderConfiguration,
        // OrderSagaConfiguration, ...) so adding an aggregate never means editing this file.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Money. EF's default decimal(18,2) comes with a warning per property; being explicit
        // once here beats silently truncating a unit price somewhere later.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }
}
