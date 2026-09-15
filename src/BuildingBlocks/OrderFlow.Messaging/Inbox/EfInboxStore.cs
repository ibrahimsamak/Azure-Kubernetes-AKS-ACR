namespace OrderFlow.Messaging.Inbox;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Messaging.Abstractions;

public sealed class EfInboxStore<TContext>(TContext db, string consumerName) : IInboxStore
    where TContext : DbContext
{
    /// <summary>Stages the claim row. Caller saves it together with the business change.</summary>
    public void Claim(Guid messageId, string type) =>
        db.Set<InboxMessage>().Add(new InboxMessage
        {
            MessageId = messageId,
            Consumer = consumerName,
            Type = type
        });

    /// <summary>Optional fast path: a cheap SELECT before doing expensive work. It is a
    /// PERFORMANCE optimisation only — the unique index is the correctness guarantee.
    /// A check-then-act read can always lose a race; a constraint cannot.</summary>
    public Task<bool> AlreadyProcessedAsync(Guid messageId, CancellationToken ct) =>
        db.Set<InboxMessage>().AnyAsync(x => x.MessageId == messageId && x.Consumer == consumerName, ct);
}
