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

builder.Services
    .AddGrpcClient<InventoryQuery.InventoryQueryClient>(o =>
    {
        // "https://inventory" is resolved by Aspire service discovery, and in Week 3 by
        // the Kubernetes service name. Never a hard-coded host or port.
        o.Address = new Uri("https://inventory");
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
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapDefaultEndpoints();          // /health, /alive
app.MapControllers();

app.Run();
