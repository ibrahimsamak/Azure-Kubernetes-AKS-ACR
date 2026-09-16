namespace OrderFlow.Order.Application.Abstractions;

/// <summary>One commit for the business change AND the outbox rows the interceptor adds.
/// Keeping it behind an interface stops Application from taking a dependency on EF.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
