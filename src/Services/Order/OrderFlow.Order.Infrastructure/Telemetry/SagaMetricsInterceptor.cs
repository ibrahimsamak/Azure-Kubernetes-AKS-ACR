namespace OrderFlow.Order.Infrastructure.Telemetry;

using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OrderFlow.Order.Domain.Sagas;
using OrderFlow.Order.Domain.Sagas.Events;

/// <summary>Saga metrics derived from what EF is about to save, recorded only once the save
/// succeeded. The domain stays free of telemetry; this adapter watches its state.</summary>
public sealed class SagaMetricsInterceptor : SaveChangesInterceptor
{
    public const string MeterName = "OrderFlow.Orders";

    private readonly Counter<long> _started;
    private readonly Counter<long> _completed;
    private readonly Counter<long> _stuck;
    private readonly Histogram<double> _duration;

    // The interceptor is a singleton shared by every DbContext. Observations are parked per context
    // between "saving" and "saved"; ConditionalWeakTable lets an entry die with its context.
    private readonly ConditionalWeakTable<DbContext, List<Observation>> _pending = new();

    public SagaMetricsInterceptor(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);
        _started = meter.CreateCounter<long>("orderflow.saga.started", "{saga}", "Orders accepted (sagas started).");
        _completed = meter.CreateCounter<long>("orderflow.saga.completed", "{saga}", "Sagas that reached a terminal state, by outcome.");
        _stuck = meter.CreateCounter<long>("orderflow.saga.stuck", "{saga}", "Compensations that timed out and need a human.");
        _duration = meter.CreateHistogram<double>("orderflow.saga.duration", "s", "Order accepted to terminal state.");
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context)
        {
            var observations = Observe(context);
            if (observations.Count > 0) { _pending.AddOrUpdate(context, observations); }
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context && _pending.TryGetValue(context, out var observations))
        {
            _pending.Remove(context);
            foreach (var observation in observations) { Record(observation); }
        }
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        // Rolled back — a duplicate delivery, a concurrency conflict: nothing happened, count nothing.
        if (eventData.Context is { } context) { _pending.Remove(context); }
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private static List<Observation> Observe(DbContext context)
    {
        var observations = new List<Observation>();

        foreach (var entry in context.ChangeTracker.Entries<OrderSaga>())
        {
            var saga = entry.Entity;

            if (entry.State == EntityState.Added)
            {
                observations.Add(new Observation(Kind.Started, null, 0));
                continue;
            }
            if (entry.State != EntityState.Modified) { continue; }

            var state = entry.Property(s => s.State);
            if (state.IsModified && state.OriginalValue != state.CurrentValue
                && state.CurrentValue is OrderSagaState.Confirmed or OrderSagaState.Cancelled)
            {
                var outcome = state.CurrentValue == OrderSagaState.Confirmed ? "confirmed" : "cancelled";
                observations.Add(new Observation(Kind.Completed, outcome, (DateTime.UtcNow - saga.StartedAtUtc).TotalSeconds));
            }

            // Needs the domain events, so this interceptor is registered BEFORE the outbox
            // interceptor (which clears them).
            if (saga.DomainEvents.OfType<OrderSagaStuckDomainEvent>().Any())
            {
                observations.Add(new Observation(Kind.Stuck, null, 0));
            }
        }

        return observations;
    }

    private void Record(Observation observation)
    {
        switch (observation.Kind)
        {
            case Kind.Started:
                _started.Add(1);
                break;
            case Kind.Completed:
            {
                var outcome = new KeyValuePair<string, object?>("orderflow.outcome", observation.Outcome);
                _completed.Add(1, outcome);
                _duration.Record(observation.Seconds, outcome);
                break;
            }
            case Kind.Stuck:
                _stuck.Add(1);
                break;
        }
    }

    private enum Kind { Started, Completed, Stuck }

    private sealed record Observation(Kind Kind, string? Outcome, double Seconds);
}
