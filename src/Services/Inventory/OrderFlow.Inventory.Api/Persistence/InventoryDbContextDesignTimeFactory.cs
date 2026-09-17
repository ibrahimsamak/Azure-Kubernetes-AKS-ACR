using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderFlow.Inventory.Api.Persistence;

/// <summary>Used only by `dotnet ef`. At design time the AppHost is not running, so the
/// Aspire-injected connection string does not exist; migrations need a model, not a live
/// database, so a local placeholder is enough.</summary>
public sealed class InventoryDbContextDesignTimeFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=localhost;Database=orderflow-inventory;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new InventoryDbContext(options);
    }
}
