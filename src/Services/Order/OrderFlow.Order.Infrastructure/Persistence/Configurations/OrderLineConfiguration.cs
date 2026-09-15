namespace OrderFlow.Order.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Order.Domain.Orders;

public sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The domain already enforces these; the constraints catch anything that bypasses it
        // (a migration script, a manual fix in production, a future bug).
        builder.ToTable("OrderLines", t =>
        {
            t.HasCheckConstraint("CK_OrderLines_Quantity_Positive", "[Quantity] > 0");
            t.HasCheckConstraint("CK_OrderLines_UnitPrice_NonNegative", "[UnitPrice] >= 0");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.Sku).HasMaxLength(64).IsRequired();
        builder.Property(l => l.Quantity).IsRequired();
        builder.Property(l => l.UnitPrice).IsRequired();

        // Computed in memory; storing it would create a second source of truth.
        builder.Ignore(l => l.LineTotal);

        // One line per SKU per order (the domain rule), and it doubles as the index for the
        // OrderId foreign key, so EF does not create a separate one.
        builder.HasIndex(l => new { l.OrderId, l.Sku })
            .IsUnique()
            .HasDatabaseName("UX_OrderLines_Order_Sku");
    }
}
