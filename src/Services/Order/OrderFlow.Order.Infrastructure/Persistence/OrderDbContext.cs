namespace OrderFlow.Order.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Messaging.Outbox;
using OrderFlow.Order.Domain.Orders;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public const string ConnectionName = "orderflow-orders";

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }
}
