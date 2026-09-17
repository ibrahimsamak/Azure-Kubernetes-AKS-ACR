namespace OrderFlow.Inventory.Api.Seed;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Inventory.Api.Domain;
using OrderFlow.Inventory.Api.Persistence;

public static class StockSeeder
{
    private static readonly (string Sku, int Quantity)[] Catalogue =
    [
        ("SKU-1", 100),
        ("SKU-2", 50),
        ("SKU-3", 10),
        ("SKU-OOS", 0)
    ];

    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        await db.Database.MigrateAsync();

        var existing = await db.StockItems.Select(s => s.Sku).ToListAsync();
        var missing = Catalogue.Where(c => !existing.Contains(c.Sku)).ToList();

        if (missing.Count == 0) { return; }

        db.StockItems.AddRange(missing.Select(m => new StockItem(m.Sku, m.Quantity)));
        await db.SaveChangesAsync();
    }
}
