namespace OrderFlow.Messaging.UnitTests;

using System.Diagnostics;
using OrderFlow.Messaging.Kafka;
using OrderFlow.Messaging.Telemetry;

public sealed class TraceContextPropagationTests : IDisposable
{
    private readonly ActivityListener _listener = new()
    {
        // Without a listener StartActivity returns null — exactly what happened in production
        // with the misspelled source name.
        ShouldListenTo = source => source.Name == MessagingTelemetry.Name,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
    };

    public TraceContextPropagationTests() => ActivitySource.AddActivityListener(_listener);

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void Consumer_span_continues_the_producer_trace()
    {
        var headers = new Dictionary<string, string>();
        ActivityTraceId producerTrace;
        ActivitySpanId producerSpan;

        using (var producer = MessagingTelemetry.ActivitySource.StartActivity("publish", ActivityKind.Producer))
        {
            Assert.NotNull(producer);
            TraceContextPropagation.Inject(producer, headers);
            producerTrace = producer.TraceId;
            producerSpan = producer.SpanId;
        }

        var parent = TraceContextPropagation.Extract(headers);
        using var consumer = MessagingTelemetry.ActivitySource.StartActivity("consume", ActivityKind.Consumer, parent);

        Assert.NotNull(consumer);
        Assert.Equal(producerTrace, consumer.TraceId);
        Assert.Equal(producerSpan, consumer.ParentSpanId);
    }

    [Fact]
    public void Missing_traceparent_header_means_no_parent() =>
        Assert.Equal(default(ActivityContext), TraceContextPropagation.Extract(new Dictionary<string, string>()));

    [Fact]
    public void An_outbox_style_traceparent_round_trips()
    {
        using var request = MessagingTelemetry.ActivitySource.StartActivity("request", ActivityKind.Server);
        Assert.NotNull(request);

        var stored = request.Id;   // what EfOutboxStore writes into OutboxMessage.TraceParent

        Assert.True(ActivityContext.TryParse(stored, null, out var restored));
        Assert.Equal(request.TraceId, restored.TraceId);
        Assert.Equal(request.SpanId, restored.SpanId);
    }
}
