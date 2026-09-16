namespace OrderFlow.Notification.Api.Persistence;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Messaging.Inbox;
using OrderFlow.Messaging.Outbox;

/// <summary>What we told the customer, and when. Kept even though the actual sending happens
/// over RabbitMQ: "did this customer get told?" is a question support will ask.</summary>
public sealed class NotificationRecord
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid OrderId { get; init; }
    public required string CustomerId { get; init; }

    /// <summary>OrderConfirmed, OrderCancelled, ... — the reason the customer was contacted.</summary>
    public required string Kind { get; init; }

    public required string Body { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public const string ConnectionName = "orderflow-notifications";

    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        modelBuilder.Entity<NotificationRecord>(b =>
        {
            b.ToTable("Notifications");
            b.HasKey(x => x.Id);
            b.Property(x => x.CustomerId).HasMaxLength(100).IsRequired();
            b.Property(x => x.Kind).HasMaxLength(50).IsRequired();
            b.Property(x => x.Body).HasMaxLength(1000).IsRequired();
            b.HasIndex(x => x.OrderId);
        });
    }
}
