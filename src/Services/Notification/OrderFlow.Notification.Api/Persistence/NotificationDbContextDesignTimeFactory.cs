using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderFlow.Notification.Api.Persistence;

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
