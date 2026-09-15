namespace OrderFlow.Messaging.Abstractions;

public interface IInboxStore
{
    void Claim(Guid messageId, string type);

    Task<bool> AlreadyProcessedAsync(Guid messageId, CancellationToken ct);
}
