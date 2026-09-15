namespace OrderFlow.Order.Infrastructure.Persistence.Configurations;

// Usings sit INSIDE the namespace on purpose: from here, a bare `Order` would otherwise
// resolve to the namespace OrderFlow.Order instead of the aggregate.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Order.Domain.Orders;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Orders", t =>
            t.HasCheckConstraint("CK_Orders_Total_NonNegative", "[Total] >= 0"));

        builder.HasKey(o => o.Id);
        // Ids are v7 GUIDs generated in the domain, so the id (and the outbox CorrelationId)
        // exists before SaveChanges. Never let the database assign it.
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.CustomerId).HasMaxLength(100).IsRequired();
        builder.Property(o => o.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(o => o.Total);   // precision from OrderDbContext.ConfigureConventions
        builder.Property(o => o.CancellationReason).HasMaxLength(500);
        builder.Property(o => o.CreatedAtUtc).IsRequired();

        // Stored as text: "SELECT ... WHERE Status = 'Pending'" is readable during an incident,
        // and reordering the enum can never silently re-label existing rows.
        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Optimistic concurrency. A saga handler (MarkPlaced) and a customer cancel can race
        // on the same order; without this the last writer silently wins.
        // Shadow property: the domain model does not need to know it exists.
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey(l => l.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Lines is a read-only view; EF must write through the _lines field.
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Domain events are in-memory only; the interceptor turns them into outbox rows.
        builder.Ignore(o => o.DomainEvents);

        // "My orders" page: filter by customer, newest first.
        builder.HasIndex(o => new { o.CustomerId, o.CreatedAtUtc })
            .HasDatabaseName("IX_Orders_Customer_Created");
    }
}
