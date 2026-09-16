namespace OrderFlow.Order.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Order.Domain.Sagas;

public sealed class OrderSagaConfiguration : IEntityTypeConfiguration<OrderSaga>
{
    public void Configure(EntityTypeBuilder<OrderSaga> builder)
    {
        builder.ToTable("OrderSagas");
        builder.HasKey(x => x.Id);                       // == OrderId

        builder.Property(x => x.State)
         .HasConversion<string>()                  // readable in the DB, safe to reorder the enum
         .HasMaxLength(30);

        builder.Property(x => x.CustomerId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.FailureReason).HasMaxLength(500);
        builder.Property(x => x.Version).IsRowVersion();  // optimistic concurrency

        // The timeout scanner's query: WHERE DeadlineUtc <= now AND State NOT IN (terminal).
        builder.HasIndex(x => x.DeadlineUtc).HasFilter("[DeadlineUtc] IS NOT NULL");
    }
}
