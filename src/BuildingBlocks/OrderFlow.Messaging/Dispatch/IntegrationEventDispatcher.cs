using OrderFlow.Messaging.Serialization;

namespace OrderFlow.Messaging.Dispatch;

public sealed class IntegrationEventDispatcher()
{
#pragma warning disable CA1822
    public async Task DispatchAsync(MessageEnvelope envelope, CancellationToken ct)
#pragma warning restore CA1822
    {
         await Task.CompletedTask;
    }
}
