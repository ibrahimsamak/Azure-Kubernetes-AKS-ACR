using Microsoft.EntityFrameworkCore;
using OrderFlow.Grpc.Inventory;
using OrderFlow.Order.Application.Abstractions;
using OrderFlow.Order.Infrastructure.Grpc;
using OrderFlow.Order.Infrastructure.Messaging;
using OrderFlow.Order.Infrastructure.Persistence;
using Polly;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();     // OTel, health, service discovery

// Add services to the container.
builder.AddOrderPersistence();
builder.AddOrderMessaging();

// Readiness = "can this pod do its job right now?" — for Order that means reaching its own
// database. Kafka and Redis are deliberately NOT here: if Kafka is down orders still land in
// the outbox, and marking pods unready would turn a broker blip into a checkout outage.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrderDbContext>("orders-db", tags: [Extensions.ReadyTag]);

builder.Services
    .AddGrpcClient<InventoryQuery.InventoryQueryClient>(o =>
    {
        // "https://inventory" is resolved by Aspire service discovery, and in Week 3 by
        // the Kubernetes service name. Never a hard-coded host or port.
        //o.Address = new Uri("https://inventory");

        o.Address = new Uri(builder.Configuration["Inventory:GrpcAddress"] ?? "https://inventory");
    })
    .AddStandardResilienceHandler(o =>
    {
        // Retry ONLY transient, idempotent failures. CheckAvailability is a pure read,
        // so retrying is safe — never apply this blindly to a mutating call.
        o.Retry.MaxRetryAttempts = 2;
        o.Retry.Delay = TimeSpan.FromMilliseconds(100);
        o.Retry.BackoffType = DelayBackoffType.Exponential;

        // Circuit breaker: stop hammering a service that is clearly down, and give it
        // room to recover instead of adding to its load.
        o.CircuitBreaker.FailureRatio = 0.5;
        o.CircuitBreaker.MinimumThroughput = 10;
        o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);

        // Total timeout above the per-call deadline so the deadline is what usually fires.
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(2);
    });

builder.Services.AddScoped<IInventoryQueryClient, InventoryQueryGrpcClient>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    //app.MapOpenApi();
}

// Opt-out, so a migration Job can take over later (stretch goal). EF Core 9+ takes a
// database lock during Migrate(), so two replicas starting together are safe.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<OrderDbContext>().Database.MigrateAsync();
}

app.UseAuthorization();

app.MapDefaultEndpoints();          // /health, /alive
app.MapControllers();

app.Run();
