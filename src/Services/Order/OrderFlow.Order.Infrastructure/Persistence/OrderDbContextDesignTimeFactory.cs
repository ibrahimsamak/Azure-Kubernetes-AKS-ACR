using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderFlow.Order.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef`. At design time the AppHost is not running, so the
/// Aspire-injected connection string does not exist; migrations need a model, not a live
/// database, so a local placeholder is enough.</summary>
public sealed class OrderDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlServer("Server=localhost;Database=orderflow-orders;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new OrderDbContext(options);
    }
}
