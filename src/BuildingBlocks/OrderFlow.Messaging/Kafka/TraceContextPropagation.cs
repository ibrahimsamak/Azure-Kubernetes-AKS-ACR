namespace OrderFlow.Messaging.Kafka;

using System.Diagnostics;

public static class TraceContextPropagation
{
    public static void Inject(Activity? activity, IDictionary<string, string> headers)
    {
        if (activity is null)
        {
            return;
        }
        headers[KafkaHeaders.TraceParent] = activity.Id!;
    }

    public static ActivityContext Extract(IReadOnlyDictionary<string, string> headers) =>
        headers.TryGetValue(KafkaHeaders.TraceParent, out var tp) && ActivityContext.TryParse(tp, null, out var ctx) ? ctx : default;
}
