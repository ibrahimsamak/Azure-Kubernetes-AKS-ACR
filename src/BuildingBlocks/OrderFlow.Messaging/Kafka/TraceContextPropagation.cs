namespace OrderFlow.Messaging.Kafka;

using System.Diagnostics;

/// <summary>W3C trace context over Kafka headers. Without this, every consumer starts a
/// brand-new trace and your "distributed" tracing shows four disconnected islands.</summary>
public static class TraceContextPropagation
{
    public static void Inject(Activity? activity, IDictionary<string, string> headers)
    {
        if (activity is null)
        {
            return;
        }
        headers[KafkaHeaders.TraceParent] = activity.Id!;   // 00-<traceid>-<spanid>-01
    }

    public static ActivityContext Extract(IReadOnlyDictionary<string, string> headers) =>
        headers.TryGetValue(KafkaHeaders.TraceParent, out var tp) && ActivityContext.TryParse(tp, null, out var ctx) ? ctx : default;
}
