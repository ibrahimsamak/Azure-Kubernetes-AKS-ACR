using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderFlow.Order.Infrastructure.Persistence;

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
