namespace OrderFlow.Order.Domain.Common;

public interface IDomainEvent
{
    DateTime OccurredOnUtc { get; }
}
