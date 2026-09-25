namespace OrderFlow.Messaging.Telemetry;

using System.Diagnostics;
using System.Diagnostics.Metrics;

/// <summary>The ONE place the messaging building block names its telemetry. A source or meter
/// nobody subscribes to is silently dropped — which is exactly what the old "OrderFlow.Messeging"
/// literal did to every messaging span.</summary>
public static class MessagingTelemetry
{
    public const string Name = "OrderFlow.Messaging";

    public static readonly ActivitySource ActivitySource = new(Name);
    public static readonly Meter Meter = new(Name);

    /// <summary>Broker-acknowledged publishes (acks=all), by topic.</summary>
    public static readonly Counter<long> Published = Meter.CreateCounter<long>(
        "orderflow.messaging.published", "{message}", "Messages acknowledged by the broker.");

    /// <summary>Outbox rows whose publish attempt failed. They are retried with backoff — but a
    /// steady non-zero rate means events have stopped flowing. Alerted on (part 2).</summary>
    public static readonly Counter<long> OutboxPublishFailures = Meter.CreateCounter<long>(
        "orderflow.outbox.publish_failures", "{message}", "Outbox publish attempts that failed.");

    /// <summary>Time from "row committed" to "broker acked". The outbox's health in one number.</summary>
    public static readonly Histogram<double> OutboxLag = Meter.CreateHistogram<double>(
        "orderflow.outbox.lag", "s", "Delay between an outbox row being written and published.");

    /// <summary>Messages parked on a .dlq topic. Should be zero; any value pages someone.</summary>
    public static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>(
        "orderflow.messaging.dead_lettered", "{message}", "Messages moved to a dead-letter topic.");

    /// <summary>Consume-to-commit time for one message, retries included, by event type and outcome.</summary>
    public static readonly Histogram<double> HandlerDuration = Meter.CreateHistogram<double>(
        "orderflow.messaging.handler.duration", "s", "Time to handle one consumed message.");
}
