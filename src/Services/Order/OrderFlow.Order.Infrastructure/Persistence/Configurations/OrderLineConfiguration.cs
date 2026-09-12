namespace OrderFlow.Order.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Order.Domain.Orders;

public sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

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

        builder.Ignore(l => l.LineTotal);

        builder.HasIndex(l => new { l.OrderId, l.Sku })
            .IsUnique()
            .HasDatabaseName("UX_OrderLines_Order_Sku");
    }
}
