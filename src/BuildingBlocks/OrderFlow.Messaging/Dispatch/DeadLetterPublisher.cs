namespace OrderFlow.Messaging.Dispatch;

using Microsoft.Extensions.Logging;
using OrderFlow.Contracts;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Kafka;

public sealed partial class DeadLetterPublisher(IEventPublisher publisher, ILogger<DeadLetterPublisher> logger)
{
    public async Task SendAsync(string sourceTopic, string key, string value,
        IReadOnlyDictionary<string, string> originalHeaders, string reason, CancellationToken ct)
    {
        var headers = new Dictionary<string, string>(originalHeaders)
        {
            [KafkaHeaders.DeathReason] = Truncate(reason, 900),
            [KafkaHeaders.DeathSource] = sourceTopic,
            ["dlq-timestamp"] = DateTime.UtcNow.ToString("O")
        };

        // Same key: a replayed DLQ message lands on the same partition and keeps its ordering.
        await publisher.PublishRawAsync(Topics.DeadLetter(sourceTopic), key, value, headers, ct);

        // Alert on this in Week 4. A DLQ nobody watches is a silent data-loss queue.
        LogDeadLettered(logger, sourceTopic, key, reason);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    [LoggerMessage(Level = LogLevel.Error, Message = "Dead-lettered message from {Topic} key={Key}: {Reason}")]
    private static partial void LogDeadLettered(ILogger logger, string topic, string key, string reason);
}
