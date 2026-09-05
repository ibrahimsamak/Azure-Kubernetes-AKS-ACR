namespace OrderFlow.Contracts;

public abstract record IntegrationEvent
{
    public Guid MessageId { get; init; } = Guid.CreateVersion7();

    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;

    public Guid CorrelationId { get; init; }

    public Guid? CausationId { get; init; }
}
