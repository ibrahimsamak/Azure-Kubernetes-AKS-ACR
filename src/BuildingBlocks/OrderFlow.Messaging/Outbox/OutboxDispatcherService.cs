namespace OrderFlow.Messaging.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Serialization;


public sealed partial class OutboxDispatcherService<TContext>(
        IServiceScopeFactory scopeFactory,
        IEventPublisher publisher,
        ILogger<OutboxDispatcherService<TContext>> logger
) : BackgroundService where TContext : DbContext
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(Random.Shared.Next(0, 500), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await DispatchBatchAsync(stoppingToken);
                // Backlog? Loop straight away. Idle? Sleep, do not spin the DB.
                if (dispatched == 0) { await Task.Delay(PollInterval, stoppingToken); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // The dispatcher must NEVER die: if it does, events stop flowing silently.
                LogIterationFailed(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var now = DateTime.UtcNow;

        // UPDLOCK + READPAST is the competing-consumers pattern in SQL Server:
        //   UPDLOCK  — take an update lock so another replica cannot grab the same rows
        //   READPAST — skip rows another replica already locked instead of blocking
        // (Postgres equivalent: SELECT ... FOR UPDATE SKIP LOCKED.)
        // Without this, two replicas publish the same message twice — still safe thanks to
        // the Inbox, but wasteful and confusing in the logs.

        var messages = await db.Set<OutboxMessage>()
           .FromSqlRaw($$"""
                SELECT TOP ({{BatchSize}}) *
                FROM OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE ProcessedOnUtc IS NULL
                  AND (NextAttemptUtc IS NULL OR NextAttemptUtc <= {0})
                ORDER BY OccurredOnUtc
                """, now)
           .ToListAsync(ct);

        if (messages.Count == 0)
        {
            return 0;
        }

        foreach (var message in messages) {
            try
            {
                var envelope = MessageEnvelope.FromJson(message.Content)
                        ?? throw new InvalidOperationException("Outbox row content is not a valid envelope.");

                var @event = envelope.Unwrap()
                        ?? throw new InvalidOperationException($"Unknown event type '{envelope.Type}'.");

                // Awaits the broker ack (Acks.All). Only then do we mark it processed.
                await publisher.PublishAsync(@event, message.PartitionKey, ct);
                message.ProcessedOnUtc = DateTime.UtcNow;
                message.LastError = null;
            }
            catch (Exception ex)
            {
                message.AttemptCount++;
                message.LastError = ex.Message;
                var delaySeconds = Math.Min(300, Math.Pow(2, Math.Min(message.AttemptCount, 8)));
                message.NextAttemptUtc = DateTime.UtcNow.AddSeconds(delaySeconds);

                LogPublishFailed(logger, ex, message.Id, message.AttemptCount);

                // NOTE: we never drop the row. A permanently failing outbox row is an
                // operational alert (Week 4), not something to delete quietly.
            }
        }

        await db.SaveChangesAsync(ct);
        return messages.Count;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox dispatcher iteration failed; continuing.")]
    private static partial void LogIterationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to publish outbox message {MessageId} (attempt {Attempt}).")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, Guid messageId, int attempt);
}