namespace OrderFlow.Messaging.Dispatch;

using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Contracts;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Inbox;
using OrderFlow.Messaging.Serialization;

public sealed partial class IntegrationEventDispatcher(
    IServiceProvider services,
    DbContext db,
    string consumerName,
    ILogger<IntegrationEventDispatcher> logger)
{
    public async Task DispatchAsync(MessageEnvelope envelope, CancellationToken ct)
    {
        var @event = envelope.Unwrap();
        if (@event is null)
        {
            LogUnknownEventType(logger, envelope.Type);
            return;
        }

        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(@event.GetType());
        var handlers = services.GetServices(handlerType).Cast<object>().ToList();
        if (handlers.Count == 0)
        {
            LogNoHandler(logger, envelope.Type, consumerName);
            return;
        }

        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var inbox = new EfInboxStore<DbContext>(db, consumerName);
            inbox.Claim(@event.MessageId, envelope.Type);

            try
            {
                foreach (var handler in handlers)
                {
                    var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IntegrationEvent>.HandleAsync))!;

                    try
                    {
                        await (Task)method.Invoke(handler, [@event, ct])!;
                    }
                    catch (TargetInvocationException ex) when (ex.InnerException is not null)
                    {
                        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    }
                }

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                LogHandled(logger, envelope.Type, @event.MessageId, @event.CorrelationId);
            }
            catch (DbUpdateException ex) when (InboxDuplicateDetector.IsDuplicate(ex))
            {
                await tx.RollbackAsync(ct);
                LogDuplicateIgnored(logger, envelope.Type, @event.MessageId, consumerName);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });
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
