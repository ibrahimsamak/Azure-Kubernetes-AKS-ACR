namespace OrderFlow.Inventory.Api.Persistence;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Inventory.Api.Domain;
using OrderFlow.Messaging.Inbox;
using OrderFlow.Messaging.Outbox;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public const string ConnectionName = "orderflow-inventory";

    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        modelBuilder.Entity<StockItem>(b =>
        {
            b.ToTable("StockItems");
            b.HasKey(x => x.Sku);
            b.Property(x => x.Sku).HasMaxLength(50);

            b.Property(x => x.Version).IsRowVersion();

            b.HasMany(x => x.Reservations)
                .WithOne()
                .HasForeignKey("Sku")
                .OnDelete(DeleteBehavior.Cascade);

            b.Navigation(x => x.Reservations).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<StockReservation>(b =>
        {
            b.ToTable("StockReservations");
            b.HasKey(x => x.Id);

            b.HasIndex(x => x.OrderId);
        });
    }
}
