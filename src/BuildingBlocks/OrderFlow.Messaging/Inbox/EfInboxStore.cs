namespace OrderFlow.Messaging.Inbox;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Messaging.Abstractions;

public sealed class EfInboxStore<TContext>(TContext db, string consumerName) : IInboxStore
    where TContext : DbContext
{
    public void Claim(Guid messageId, string type) =>
        db.Set<InboxMessage>().Add(new InboxMessage
        {
            MessageId = messageId,
            Consumer = consumerName,
            Type = type
        });

    public Task<bool> AlreadyProcessedAsync(Guid messageId, CancellationToken ct) =>
        db.Set<InboxMessage>().AnyAsync(x => x.MessageId == messageId && x.Consumer == consumerName, ct);
}
