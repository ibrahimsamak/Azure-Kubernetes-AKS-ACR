using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderFlow.Payment.Api.Persistence;

/// <summary>Used only by `dotnet ef`. At design time the AppHost is not running, so the
/// Aspire-injected connection string does not exist; migrations need a model, not a live
/// database, so a local placeholder is enough.</summary>
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
