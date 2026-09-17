namespace OrderFlow.Order.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>Deletes idempotency keys older than the retention window. Without it the table
/// grows forever — one row per order ever placed — and the PK index that makes the dedup
/// fast eventually becomes the reason it is slow.</summary>
public sealed partial class IdempotencyCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<IdempotencyCleanupService> logger) : BackgroundService
{
    /// <summary>A client retrying a request it made a day ago is not retrying, it is placing
    /// a new order. 24h is far beyond any sane HTTP retry window.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan BatchPause = TimeSpan.FromMilliseconds(100);

    /// <summary>Kept well under 5000, where SQL Server escalates row locks to a TABLE lock
    /// and would block order placement itself.</summary>
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
                // Not urgent: the next run picks up where this one stopped.
                LogCleanupFailed(logger, ex);
            }

            try
            {
                await Task.Delay(RunInterval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - Retention;
        var total = 0;

        while (!ct.IsCancellationRequested)
        {
            // Fresh scope per batch, so each DELETE is its own short transaction and locks
            // are released in between.
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

            // Safe with multiple replicas: two instances deleting the same batch just means
            // one of them deletes 0 rows.
            var deleted = await db.IdempotentRequests
                .Where(r => r.CreatedAtUtc < cutoff)
                .Take(BatchSize)
                .ExecuteDeleteAsync(ct);

            total += deleted;
            if (deleted < BatchSize) { break; }

            await Task.Delay(BatchPause, ct);
        }

        if (total > 0)
        {
            LogCleanedUp(logger, total, cutoff);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency cleanup deleted {Count} keys created before {Cutoff:O}.")]
    private static partial void LogCleanedUp(ILogger logger, int count, DateTime cutoff);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency cleanup run failed; will retry next interval.")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
