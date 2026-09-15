namespace OrderFlow.Order.Domain.Common;

/// <summary>Non-generic view of an aggregate's pending events, so infrastructure can find
/// them without knowing each aggregate's id type.</summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
