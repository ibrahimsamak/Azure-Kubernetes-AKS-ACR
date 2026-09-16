namespace OrderFlow.Order.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Messaging.Inbox;
using OrderFlow.Messaging.Outbox;
using OrderFlow.Order.Domain.Orders;
using OrderFlow.Order.Domain.Sagas;
using OrderFlow.Order.Infrastructure.Persistence.Repositories;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public const string ConnectionName = "orderflow-orders";

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderSaga> OrderSagas => Set<OrderSaga>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<IdempotentRequest> IdempotentRequests => Set<IdempotentRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        modelBuilder.Entity<IdempotentRequest>(b =>
        {
            b.ToTable("IdempotentRequests");
            b.HasKey(x => x.Key);
            b.Property(x => x.Key).HasMaxLength(100);
        });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }
}
