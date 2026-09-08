namespace OrderFlow.Messaging.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed partial class OutboxCleanupService<TContext>(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxCleanupService<TContext>> logger
) : BackgroundService where TContext : DbContext
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);

    private static readonly TimeSpan BatchPause = TimeSpan.FromMilliseconds(100);

    private const int BatchSize = 1000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();

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
