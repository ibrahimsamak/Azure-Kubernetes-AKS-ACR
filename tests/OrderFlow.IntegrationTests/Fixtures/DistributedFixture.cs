namespace OrderFlow.IntegrationTests.Fixtures;

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Messaging.Serialization;
using OrderFlow.Order.Infrastructure.Persistence;
using OrderFlow.Order.Infrastructure.Persistence.Interceptors;
using Testcontainers.MsSql;

public sealed class DistributedFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!DockerAvailability.IsAvailable) { return; }

        await _sql.StartAsync();
        ConnectionString = _sql.GetConnectionString();

        EventTypeRegistry.RegisterAssembly(typeof(IntegrationEvent).Assembly);

        await using var db = CreateOrderDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!DockerAvailability.IsAvailable) { return; }
        await _sql.DisposeAsync();
    }

    public OrderDbContext CreateOrderDb()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(new ConvertDomainEventsToOutboxInterceptor())
            .Options;

        return new OrderDbContext(options);
    }

    public OrderDbContext CreateRetryingOrderDb()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure())
            .AddInterceptors(new ConvertDomainEventsToOutboxInterceptor())
            .Options;

        return new OrderDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class DistributedCollection : ICollectionFixture<DistributedFixture>
{
    public const string Name = "distributed";
}

public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerAvailability.IsAvailable)
        {
            Skip = "Docker is not available; start Docker Desktop to run the integration tests.";
        }
    }
}

internal static class DockerAvailability
{
    private static readonly Lazy<bool> Probe = new(() =>
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null) { return false; }
            return process.WaitForExit(milliseconds: 15_000) && process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    });

    public static bool IsAvailable => Probe.Value;
}
