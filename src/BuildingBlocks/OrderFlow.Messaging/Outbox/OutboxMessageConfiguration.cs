namespace OrderFlow.Messaging.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Content).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.PartitionKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(2000);
        builder.Property(x => x.TraceParent).HasMaxLength(55);

        builder.HasIndex(x => new { x.ProcessedOnUtc, x.NextAttemptUtc })
         .HasFilter("[ProcessedOnUtc] IS NULL")
         .HasDatabaseName("IX_Outbox_Pending");

        builder.HasIndex(x => x.ProcessedOnUtc)
         .HasFilter("[ProcessedOnUtc] IS NOT NULL")
         .HasDatabaseName("IX_Outbox_Processed");
    }
}
