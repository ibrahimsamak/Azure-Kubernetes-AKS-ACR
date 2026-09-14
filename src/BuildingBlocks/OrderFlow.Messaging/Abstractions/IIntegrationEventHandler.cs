namespace OrderFlow.Messaging.Abstractions;

using OrderFlow.Contracts;

#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{

#pragma warning disable CA1716 // Identifiers should not match keywords
    Task HandleAsync(TEvent @event, CancellationToken ct);
#pragma warning restore CA1716 // Identifiers should not match keywords
}

#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public interface IIntegrationEventHandler
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
#pragma warning disable CA1716 // Identifiers should not match keywords
    Task HandleAsync(IntegrationEvent @event, CancellationToken ct);
#pragma warning restore CA1716 // Identifiers should not match keywords
}
