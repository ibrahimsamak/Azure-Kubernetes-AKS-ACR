namespace OrderFlow.Order.Domain.Common;

/// <summary>Something that happened inside an aggregate. Internal to the Order service;
/// mapped to a public IntegrationEvent (or dropped) before it leaves.</summary>
public interface IDomainEvent
{
    DateTime OccurredOnUtc { get; }
}
