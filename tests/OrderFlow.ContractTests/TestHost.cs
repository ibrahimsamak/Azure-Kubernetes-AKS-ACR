namespace OrderFlow.ContractTests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Inventory.Api.Domain;
using OrderFlow.Inventory.Api.Persistence;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.Outbox;

public static class TestHost
{
    public static T Resolve<T>() where T : notnull => BuildProvider().GetRequiredService<T>();

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

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

    private static void SeedStock(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        db.StockItems.AddRange(new StockItem("SKU-1", 100), new StockItem("SKU-2", 100));
        db.SaveChanges();
    }
}
