namespace OrderFlow.Inventory.Api.Seed;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Inventory.Api.Domain;
using OrderFlow.Inventory.Api.Persistence;

public static class StockSeeder
{
    /// <summary>SKU-OOS exists but has nothing on hand, so the reservation-failure path can
    /// be exercised without editing the database by hand.</summary>
    private static readonly (string Sku, int Quantity)[] Catalogue =
    [
        ("SKU-1", 100),
        ("SKU-2", 50),
        ("SKU-3", 10),
        ("SKU-OOS", 0)
    ];

    /// <summary>Dev convenience: migrate, then top the catalogue up. Week 3 replaces this
    /// with a migration job — running schema changes from inside the app is fine with one
    /// replica and a race waiting to happen with several.</summary>
    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        await db.Database.MigrateAsync();

        // Only add what is missing: re-seeding on every restart would silently reset the
        // quantities that a running saga is in the middle of reserving against.
        var existing = await db.StockItems.Select(s => s.Sku).ToListAsync();
        var missing = Catalogue.Where(c => !existing.Contains(c.Sku)).ToList();

        if (missing.Count == 0) { return; }

        db.StockItems.AddRange(missing.Select(m => new StockItem(m.Sku, m.Quantity)));
        await db.SaveChangesAsync();
    }
}
