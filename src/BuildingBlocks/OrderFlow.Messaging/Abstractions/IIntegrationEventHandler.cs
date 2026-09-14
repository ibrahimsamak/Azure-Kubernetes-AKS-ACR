namespace OrderFlow.Messaging.Abstractions;

using OrderFlow.Contracts;

#pragma warning disable CA1711
public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
#pragma warning restore CA1711
{
#pragma warning disable CA1716
    Task HandleAsync(TEvent @event, CancellationToken ct);
#pragma warning restore CA1716
}

#pragma warning disable CA1711
public interface IIntegrationEventHandler
#pragma warning restore CA1711
{
#pragma warning disable CA1716
    Task HandleAsync(IntegrationEvent @event, CancellationToken ct);
#pragma warning restore CA1716
}
