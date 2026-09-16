namespace OrderFlow.Payment.Api.Persistence;

// Inside the namespace so `Payment` resolves to the entity, not the OrderFlow.Payment namespace.
using Microsoft.EntityFrameworkCore;
using OrderFlow.Messaging.Inbox;
using OrderFlow.Messaging.Outbox;
using OrderFlow.Payment.Api.Domain;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public const string ConnectionName = "orderflow-payments";

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PendingCharge> PendingCharges => Set<PendingCharge>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        modelBuilder.Entity<Payment>(b =>
        {
            b.ToTable("Payments");
            b.HasKey(x => x.Id);
            b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            b.Property(x => x.Amount).HasPrecision(18, 2);

            // The database enforces "at most one capture per order" so a bug in the handler
            // cannot turn into a double charge.
            b.HasIndex(x => x.OrderId).IsUnique();
        });

        modelBuilder.Entity<PendingCharge>(b =>
        {
            b.ToTable("PendingCharges");
            b.HasKey(x => x.OrderId);
            b.Property(x => x.CustomerId).HasMaxLength(100).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            b.Property(x => x.Amount).HasPrecision(18, 2);
        });
    }
}
