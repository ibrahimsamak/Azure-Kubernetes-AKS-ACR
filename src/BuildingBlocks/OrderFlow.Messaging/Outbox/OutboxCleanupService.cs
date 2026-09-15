namespace OrderFlow.Messaging.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>Deletes published outbox rows older than the retention window.
/// Without this the table grows forever: every order ever placed leaves rows behind,
/// backups bloat, and index maintenance eventually becomes the incident.</summary>
public sealed partial class OutboxCleanupService<TContext>(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxCleanupService<TContext>> logger
) : BackgroundService where TContext : DbContext
{
    /// <summary>Long enough to debug "was this event ever published?" after the fact.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);

    /// <summary>Pause between batches so cleanup never starves the dispatcher or the business writes.</summary>
    private static readonly TimeSpan BatchPause = TimeSpan.FromMilliseconds(100);

    /// <summary>Kept well under 5000: SQL Server escalates row locks to a TABLE lock at ~5000
    /// locks per statement, which would block the dispatcher and every SaveChanges that
    /// inserts outbox rows.</summary>
    private const int BatchSize = 1000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger replicas so they do not all start deleting at the same moment.
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(10, 60)), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // A failed cleanup is not urgent; the next run picks up where this one stopped.
                LogCleanupFailed(logger, ex);
            }

            await Task.Delay(RunInterval, stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - Retention;
        var total = 0;

        while (!ct.IsCancellationRequested)
        {
            // Fresh scope per batch: no long-lived DbContext, and each DELETE is its own
            // short transaction so locks are released between batches.
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();

            // Safe with multiple replicas: two instances deleting the same batch just means
            // one of them deletes 0 rows.
            var deleted = await db.Database.ExecuteSqlRawAsync($$"""
                DELETE TOP ({{BatchSize}})
                FROM OutboxMessages
                WHERE ProcessedOnUtc IS NOT NULL
                  AND ProcessedOnUtc < {0}
                """, [cutoff], ct);

            total += deleted;
            if (deleted < BatchSize) { break; }

            await Task.Delay(BatchPause, ct);
        }

        if (total > 0)
        {
            LogCleanedUp(logger, total, cutoff);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox cleanup deleted {Count} rows processed before {Cutoff:O}.")]
    private static partial void LogCleanedUp(ILogger logger, int count, DateTime cutoff);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox cleanup run failed; will retry next interval.")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
