using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Contracts.Orders;
using OrderFlow.Inventory.Api.Grpc;
using OrderFlow.Inventory.Api.Handlers;
using OrderFlow.Inventory.Api.Persistence;
using OrderFlow.Inventory.Api.Seed;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOrderFlowAzure();

builder.Services.AddDbContext<InventoryDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("orderflow-inventory"),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<InventoryDbContext>("inventory-db", tags: [Extensions.ReadyTag]);

builder.Services.AddOrderFlowMessaging<InventoryDbContext>(
    builder.Configuration,
    consumerName: "inventory-service",
    subscribeTopics: [Topics.Orders],
    ownedTopics: [Topics.Inventory]);

builder.Services.AddScoped<IIntegrationEventHandler<OrderPlaced>, OrderPlacedHandler>();
builder.Services.AddScoped<IIntegrationEventHandler<OrderCancelled>, OrderCancelledHandler>();

builder.Services.AddGrpc();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapGrpcService<InventoryQueryService>();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await app.MigrateAndSeedAsync();
}

app.Run();
