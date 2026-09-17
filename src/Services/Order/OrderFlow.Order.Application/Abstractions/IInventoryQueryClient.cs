namespace OrderFlow.Order.Application.Abstractions;

/// <summary>The answer to an advisory stock check. <c>Known</c> is false when Inventory could
/// not be reached: the caller must then treat the answer as "carry on", because if Inventory
/// being down stopped orders being accepted we would have built a distributed monolith by
/// accident. <c>FirstUnavailableSku</c> is only meaningful when <c>AllAvailable</c> is false.</summary>
public sealed record AvailabilityResult(bool Known, bool AllAvailable, string? FirstUnavailableSku);

/// <summary>A synchronous, READ-ONLY peek at stock. Never a reservation: reservations happen
/// through events, and a sync call that mutates state across a service boundary is a
/// distributed transaction wearing a disguise.</summary>
public interface IInventoryQueryClient
{
    Task<AvailabilityResult> CheckAvailabilityAsync(
        IReadOnlyList<(string Sku, int Quantity)> lines, CancellationToken ct);
}
