using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderFlow.Notification.Api.Persistence;

/// <summary>Used only by `dotnet ef`. At design time the AppHost is not running, so the
/// Aspire-injected connection string does not exist; migrations need a model, not a live
/// database, so a local placeholder is enough.</summary>
public sealed class NotificationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlServer("Server=localhost;Database=orderflow-notifications;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new NotificationDbContext(options);
    }
}
