namespace OrderFlow.ContractTests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Inventory.Api.Domain;
using OrderFlow.Inventory.Api.Persistence;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Outbox;

/// <summary>Just enough container to resolve a real handler. The point of a consumer pact
/// test is that the ACTUAL handler runs against the message Pact generated — a hand-written
/// fake would only prove the fake can read it.</summary>
public static class TestHost
{
    public static T Resolve<T>() where T : notnull => BuildProvider().GetRequiredService<T>();

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        // A fresh in-memory store per resolve, so tests cannot leak stock into each other.
        services.AddDbContext<InventoryDbContext>(o =>
            o.UseInMemoryDatabase($"contract-{Guid.CreateVersion7()}"));

        services.AddScoped<IOutboxStore>(sp =>
            new EfOutboxStore<InventoryDbContext>(sp.GetRequiredService<InventoryDbContext>()));

        services.AddScoped<Inventory.Api.Handlers.OrderPlacedHandler>();
        services.AddScoped<Inventory.Api.Handlers.OrderCancelledHandler>();

        var provider = services.BuildServiceProvider();
        SeedStock(provider);
        return provider;
    }

    /// <summary>The pact's SKUs must exist, or the handler takes the "unknown SKU" path and
    /// the test would pass for the wrong reason.</summary>
    private static void SeedStock(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        db.StockItems.AddRange(new StockItem("SKU-1", 100), new StockItem("SKU-2", 100));
        db.SaveChanges();
    }
}
