namespace OrderFlow.Order.Application.Abstractions;

public sealed record AvailabilityResult(bool Known, bool AllAvailable, string? FirstUnavailableSku);

public interface IInventoryQueryClient
{
    Task<AvailabilityResult> CheckAvailabilityAsync(
        IReadOnlyList<(string Sku, int Quantity)> lines, CancellationToken ct);
}
