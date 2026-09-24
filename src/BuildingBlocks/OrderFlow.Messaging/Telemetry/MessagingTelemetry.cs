namespace OrderFlow.Messaging.Telemetry;

using System.Diagnostics;
using System.Diagnostics.Metrics;

public static class MessagingTelemetry
{
    public const string Name = "OrderFlow.Messaging";

    public static readonly ActivitySource ActivitySource = new(Name);
    public static readonly Meter Meter = new(Name);

    public static readonly Counter<long> Published = Meter.CreateCounter<long>(
        "orderflow.messaging.published", "{message}", "Messages acknowledged by the broker.");

    public static readonly Counter<long> OutboxPublishFailures = Meter.CreateCounter<long>(
        "orderflow.outbox.publish_failures", "{message}", "Outbox publish attempts that failed.");

    public static readonly Histogram<double> OutboxLag = Meter.CreateHistogram<double>(
        "orderflow.outbox.lag", "s", "Delay between an outbox row being written and published.");

    public static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>(
        "orderflow.messaging.dead_lettered", "{message}", "Messages moved to a dead-letter topic.");

    public static readonly Histogram<double> HandlerDuration = Meter.CreateHistogram<double>(
        "orderflow.messaging.handler.duration", "s", "Time to handle one consumed message.");
}
