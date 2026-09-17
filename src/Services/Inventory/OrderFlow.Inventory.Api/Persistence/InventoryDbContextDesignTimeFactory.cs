using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderFlow.Inventory.Api.Persistence;

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
