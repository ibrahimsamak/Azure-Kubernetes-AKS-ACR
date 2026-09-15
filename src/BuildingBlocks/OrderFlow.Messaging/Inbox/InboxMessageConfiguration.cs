namespace OrderFlow.Messaging.Inbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("InboxMessages");

        builder.HasKey(x => new { x.MessageId, x.Consumer });

        builder.Property(x => x.Consumer).HasMaxLength(100);
        builder.Property(x => x.Type).HasMaxLength(200);

        builder.HasIndex(x => x.ProcessedOnUtc);
    }
}
