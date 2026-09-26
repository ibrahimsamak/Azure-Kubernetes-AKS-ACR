namespace OrderFlow.Order.Infrastructure.Grpc;

using global::Grpc.Core;
using Microsoft.Extensions.Logging;
using OrderFlow.Grpc.Inventory;
using OrderFlow.Order.Application.Abstractions;

public sealed partial class InventoryQueryGrpcClient(
    InventoryQuery.InventoryQueryClient client,
    ILogger<InventoryQueryGrpcClient> logger) : IInventoryQueryClient
{
    public async Task<AvailabilityResult> CheckAvailabilityAsync(
        IReadOnlyList<(string Sku, int Quantity)> lines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var request = new CheckAvailabilityRequest();
        foreach (var (sku, qty) in lines)
        {
            request.Items.Add(new RequestedItem { Sku = sku, Quantity = qty });
        }

        try
        {
            var response = await client.CheckAvailabilityAsync(
                request, deadline: DateTime.UtcNow.AddMilliseconds(500), cancellationToken: ct);

            return new AvailabilityResult(
                Known: true,
                AllAvailable: response.AllAvailable,
                FirstUnavailableSku: response.FirstUnavailableSku);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.DeadlineExceeded or StatusCode.Unavailable)
        {
            LogUnavailable(logger, ex);
            return new AvailabilityResult(Known: false, AllAvailable: true, FirstUnavailableSku: null);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Unauthenticated or StatusCode.PermissionDenied)
        {
            LogRejected(logger, ex.StatusCode, ex);
            return new AvailabilityResult(Known: false, AllAvailable: true, FirstUnavailableSku: null);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inventory availability check unavailable; proceeding without it.")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Inventory rejected Order's credentials ({Status}); check the Inventory.Read app role and Inventory:TokenScope.")]
    private static partial void LogRejected(ILogger logger, StatusCode status, Exception exception);
}
