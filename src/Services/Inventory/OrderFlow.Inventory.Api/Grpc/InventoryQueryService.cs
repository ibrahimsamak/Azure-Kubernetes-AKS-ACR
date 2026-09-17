namespace OrderFlow.Inventory.Api.Grpc;

using global::Grpc.Core;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Grpc.Inventory;
using OrderFlow.Inventory.Api.Persistence;

public sealed partial class InventoryQueryService(InventoryDbContext db, ILogger<InventoryQueryService> logger)
    : InventoryQuery.InventoryQueryBase
{
    public override async Task<CheckAvailabilityResponse> CheckAvailability(
        CheckAvailabilityRequest request, ServerCallContext context)
    {
        // DEADLINE PROPAGATION: the client's deadline arrives with the call. Respect it —
        // and pass context.CancellationToken down to EF so the query is cancelled too.
        var ct = context.CancellationToken;

        var skus = request.Items.Select(i => i.Sku).ToList();
        var items = await db.StockItems
            .AsNoTracking()                    // read path: no change tracking overhead
            .Where(s => skus.Contains(s.Sku))
            .Select(s => new { s.Sku, s.QuantityOnHand, s.QuantityReserved })
            .ToListAsync(ct);

        var response = new CheckAvailabilityResponse { AllAvailable = true };

        foreach (var requested in request.Items)
        {
            var stock = items.FirstOrDefault(i => i.Sku == requested.Sku);
            var available = stock is null ? 0 : stock.QuantityOnHand - stock.QuantityReserved;
            var sufficient = available >= requested.Quantity;

            response.Items.Add(new ItemAvailability
            {
                Sku = requested.Sku,
                Available = available,
                Sufficient = sufficient
            });

            if (!sufficient && response.AllAvailable)
            {
                response.AllAvailable = false;
                response.FirstUnavailableSku = requested.Sku;
            }
        }

        if (!response.AllAvailable)
        {
            LogUnavailable(logger, response.FirstUnavailableSku);
        }

        return response;
    }

    public override async Task WatchStockLevel(WatchStockLevelRequest request,
        IServerStreamWriter<StockLevelUpdate> responseStream, ServerCallContext context)
    {
        // Server streaming: the connection stays open and we push updates. The client's
        // cancellation token ends the loop when it disconnects — no orphaned streams.
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var stock = await db.StockItems.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Sku == request.Sku, context.CancellationToken);

            if (stock is not null)
            {
                await responseStream.WriteAsync(new StockLevelUpdate
                {
                    Sku = stock.Sku,
                    Available = stock.QuantityOnHand - stock.QuantityReserved,
                    ObservedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
            await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Availability check says {Sku} is short.")]
    private static partial void LogUnavailable(ILogger logger, string sku);
}
