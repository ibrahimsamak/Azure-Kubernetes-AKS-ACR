namespace OrderFlow.Messaging.Inbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("InboxMessages");

        // COMPOSITE PRIMARY KEY = the unique constraint = the idempotency guarantee.
        // A second insert of the same (MessageId, Consumer) throws a duplicate-key error,
        // and we translate that error into "already handled, skip".
        builder.HasKey(x => new { x.MessageId, x.Consumer });

        builder.Property(x => x.Consumer).HasMaxLength(100);
        builder.Property(x => x.Type).HasMaxLength(200);

        // For the retention job: delete rows older than N days.
        builder.HasIndex(x => x.ProcessedOnUtc);
    }
}
