namespace OrderFlow.IntegrationTests.Fixtures;

using System.Collections.Concurrent;
using OrderFlow.Contracts;
using OrderFlow.Messaging.Abstractions;

/// <summary>Stands in for Kafka. <see cref="FailUntilAttempt"/> makes the broker "fail" for
/// the first N attempts, which is how the dispatcher's retry and backoff get exercised
/// without unplugging a real one.</summary>
public sealed class RecordingEventPublisher : IEventPublisher
{
    private int _attempts;

    public ConcurrentQueue<(IntegrationEvent Event, string PartitionKey)> Published { get; } = new();

    public int FailUntilAttempt { get; set; }

    public int Attempts => Volatile.Read(ref _attempts);

    public Task PublishAsync(IntegrationEvent @event, string partitionKey, CancellationToken ct = default)
    {
        if (Interlocked.Increment(ref _attempts) <= FailUntilAttempt)
        {
            throw new InvalidOperationException("Broker unavailable.");
        }

        Published.Enqueue((@event, partitionKey));
        return Task.CompletedTask;
    }

    public Task PublishRawAsync(string topic, string key, string value,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct = default) => Task.CompletedTask;
}
