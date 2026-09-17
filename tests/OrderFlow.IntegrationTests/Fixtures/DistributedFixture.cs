namespace OrderFlow.IntegrationTests.Fixtures;

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Messaging.Serialization;
using OrderFlow.Order.Infrastructure.Persistence;
using OrderFlow.Order.Infrastructure.Persistence.Interceptors;
using Testcontainers.MsSql;

/// <summary>A real SQL Server in a container. These tests exercise the outbox's UPDLOCK /
/// READPAST query and the inbox's duplicate-key detection, and both are SQL Server specific:
/// an in-memory provider would make them pass while production stayed broken.
///
/// Kafka is deliberately NOT started. The broker adds minutes of startup and a source of
/// flakiness, and what we actually want to assert — "the dispatcher publishes pending rows
/// and marks them processed" — is better served by a recording publisher.</summary>
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

        // AddOrderFlowMessaging does this in production. Without it Resolve() returns null,
        // the dispatcher cannot unwrap its own envelopes, and every row looks like an
        // unknown event type.
        EventTypeRegistry.RegisterAssembly(typeof(IntegrationEvent).Assembly);

        // Deliberately Migrate, not EnsureCreated: this is the same call the services make
        // at startup, so a migration that does not produce a working schema fails here
        // rather than in a deployment.
        await using var db = CreateOrderDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!DockerAvailability.IsAvailable) { return; }
        await _sql.DisposeAsync();
    }

    /// <summary>A fresh context per call: sharing one across a test would let the change
    /// tracker answer questions the database should be answering.</summary>
    public OrderDbContext CreateOrderDb()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlServer(ConnectionString)
            // The interceptor is what turns domain events into outbox rows, so it has to be
            // here or every outbox assertion would be testing a hand-written row.
            .AddInterceptors(new ConvertDomainEventsToOutboxInterceptor())
            .Options;

        return new OrderDbContext(options);
    }

    /// <summary>Configured the way the services actually configure it. Aspire's
    /// AddSqlServerDbContext turns retries on by default, and every other service calls
    /// EnableRetryOnFailure explicitly — so this, not the plain one above, is production.</summary>
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

/// <summary>xUnit v2 cannot skip a test at runtime, so the decision is made at discovery.</summary>
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
            // docker not installed, not on PATH, or the daemon is not listening.
            return false;
        }
    });

    public static bool IsAvailable => Probe.Value;
}
