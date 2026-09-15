namespace OrderFlow.Messaging.Dispatch;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Contracts;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Inbox;
using OrderFlow.Messaging.Serialization;

public sealed partial class IntegrationEventDispatcher(
    IServiceProvider services,
    DbContext db,           // the service's own DbContext, scoped
    string consumerName,
    ILogger<IntegrationEventDispatcher> logger)
{
    public async Task DispatchAsync(MessageEnvelope envelope, CancellationToken ct)
    {
        var @event = envelope.Unwrap();
        if (@event is null)
        {
            // Unknown type on a shared topic = an event meant for somebody else. Not an error.
            LogUnknownEventType(logger, envelope.Type);
            return;
        }

        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(@event.GetType());
        var handlers = services.GetServices(handlerType).Cast<object>().ToList();
        if (handlers.Count == 0)
        {
            LogNoHandler(logger, envelope.Type, consumerName);
            return;   // we still commit the offset: this service does not care about this event
        }

        // Explicit transaction: the Inbox claim, the handler's business writes, and the
        // handler's outbox rows must all commit or all roll back.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var inbox = new EfInboxStore<DbContext>(db, consumerName);
        inbox.Claim(@event.MessageId, envelope.Type);

        try
        {
            foreach (var handler in handlers)
            {
                var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IntegrationEvent>.HandleAsync))!;
                await (Task)method.Invoke(handler, [@event, ct])!;
            }

            // One SaveChanges: inbox row + business rows + any new outbox rows.
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            LogHandled(logger, envelope.Type, @event.MessageId, @event.CorrelationId);
        }
        catch (DbUpdateException ex) when (InboxDuplicateDetector.IsDuplicate(ex))
        {
            // THE DEDUP PATH. A redelivery (Kafka rebalance, outbox re-publish, or a crash
            // after commit but before offset commit) lands here. Roll back and move on —
            // the work is already durably done from the first delivery.
            await tx.RollbackAsync(ct);
            LogDuplicateIgnored(logger, envelope.Type, @event.MessageId, consumerName);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;   // consumer host retries, then dead-letters
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignoring unknown event type {Type}.")]
    private static partial void LogUnknownEventType(ILogger logger, string type);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No handler for {Type} in {Consumer}; skipping.")]
    private static partial void LogNoHandler(ILogger logger, string type, string consumer);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Type} {MessageId} (correlation {CorrelationId}).")]
    private static partial void LogHandled(ILogger logger, string type, Guid messageId, Guid correlationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Duplicate {Type} {MessageId} ignored by {Consumer}.")]
    private static partial void LogDuplicateIgnored(ILogger logger, string type, Guid messageId, string consumer);
}
