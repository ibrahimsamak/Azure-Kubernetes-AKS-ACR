namespace OrderFlow.Inventory.Api.Persistence;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Inventory.Api.Domain;
using OrderFlow.Messaging.Inbox;
using OrderFlow.Messaging.Outbox;

/// <summary>Stock, the inbox and the outbox in ONE database. Reserving stock and staging the
/// StockReserved row is then a single local transaction — the whole reason the outbox pattern
/// works without a distributed one.</summary>
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
            b.HasKey(x => x.Sku);                       // the SKU already is the natural key
            b.Property(x => x.Sku).HasMaxLength(50);

            // Two orders racing for the last unit: one of them has to lose and retry.
            b.Property(x => x.Version).IsRowVersion();

            // Reservations are only reachable through the item, so TryReserve/Release can
            // keep QuantityReserved and the reservation rows consistent in one place.
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

            // Every handler looks a reservation up by the order it belongs to.
            b.HasIndex(x => x.OrderId);
        });
    }
}
