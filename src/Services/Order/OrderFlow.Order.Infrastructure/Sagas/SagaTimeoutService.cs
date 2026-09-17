namespace OrderFlow.Order.Infrastructure.Sagas;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderFlow.Order.Domain.Sagas;
using OrderFlow.Order.Infrastructure.Persistence;

public sealed partial class SagaTimeoutService(
    IServiceScopeFactory scopeFactory,
    ILogger<SagaTimeoutService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

                var now = DateTime.UtcNow;
                var expired = await db.Set<OrderSaga>()
                    .Where(s => s.DeadlineUtc != null && s.DeadlineUtc <= now)
                    .OrderBy(s => s.DeadlineUtc)
                    .Take(50)
                    .ToListAsync(stoppingToken);

                foreach (var saga in expired)
                {
                    LogTimedOut(logger, saga.Id, saga.State.ToString());
                    saga.OnTimeout();

                    // Same reason as in the failure handlers: the saga's own failure event
                    // never reaches the wire, so the ORDER has to be cancelled for anyone to
                    // hear about it and start compensating.
                    if (saga.State == OrderSagaState.Compensating)
                    {
                        var order = await db.Orders
                            .Include(o => o.Lines)
                            .FirstOrDefaultAsync(o => o.Id == saga.Id, stoppingToken);

                        order?.Cancel(saga.FailureReason ?? "Saga timed out.", saga.PaymentWasCaptured);
                    }
                }

                if (expired.Count > 0)
                {
                    try
                    {
                        await db.SaveChangesAsync(stoppingToken);
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        // A real message advanced the saga while we were deciding it was late.
                        // The message wins; we retry next tick. This is exactly why OrderSaga
                        // carries a rowversion.
                        LogConcurrencyLoss(logger);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;   // shutdown, not a failure
            }
            catch (Exception ex)
            {
                LogScanFailed(logger, ex);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saga {SagaId} timed out in state {State}.")]
    private static partial void LogTimedOut(ILogger logger, Guid sagaId, string state);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saga timeout lost a concurrency race; retrying next tick.")]
    private static partial void LogConcurrencyLoss(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Saga timeout scan failed.")]
    private static partial void LogScanFailed(ILogger logger, Exception ex);
}
