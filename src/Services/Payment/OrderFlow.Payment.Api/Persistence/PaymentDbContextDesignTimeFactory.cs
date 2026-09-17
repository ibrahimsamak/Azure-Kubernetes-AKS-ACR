using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderFlow.Payment.Api.Persistence;

public sealed class PaymentDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseSqlServer("Server=localhost;Database=orderflow-payments;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new PaymentDbContext(options);
    }
}
