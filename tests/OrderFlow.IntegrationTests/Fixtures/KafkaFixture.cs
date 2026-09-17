namespace OrderFlow.IntegrationTests.Fixtures;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Messaging.Serialization;
using OrderFlow.Order.Infrastructure.Persistence;
using OrderFlow.Order.Infrastructure.Persistence.Interceptors;
using Testcontainers.Kafka;
using Testcontainers.MsSql;

/// <summary>A real broker AND a real database, in their own collection.
///
/// This is the expensive fixture — a Kafka container costs the better part of a minute to
/// come up — so it is deliberately kept apart from <see cref="DistributedFixture"/>. The SQL
/// tests stay fast; only the transport tests pay for the broker.</summary>
public sealed class KafkaFixture : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder("confluentinc/cp-kafka:7.6.1").Build();
    private readonly MsSqlContainer _sql =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string BootstrapServers { get; private set; } = string.Empty;
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!DockerAvailability.IsAvailable) { return; }

        // In parallel: two container pulls in sequence is most of a coffee break.
        await Task.WhenAll(_kafka.StartAsync(), _sql.StartAsync());

        BootstrapServers = _kafka.GetBootstrapAddress();
        ConnectionString = _sql.GetConnectionString();

        EventTypeRegistry.RegisterAssembly(typeof(IntegrationEvent).Assembly);

        await using var db = CreateOrderDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!DockerAvailability.IsAvailable) { return; }
        await Task.WhenAll(_kafka.DisposeAsync().AsTask(), _sql.DisposeAsync().AsTask());
    }

    public OrderDbContext CreateOrderDb()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure())
            .AddInterceptors(new ConvertDomainEventsToOutboxInterceptor())
            .Options;

        return new OrderDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class KafkaCollection : ICollectionFixture<KafkaFixture>
{
    public const string Name = "kafka";
}
