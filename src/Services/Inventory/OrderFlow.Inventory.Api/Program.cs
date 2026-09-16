using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Contracts.Orders;
using OrderFlow.Inventory.Api.Handlers;
using OrderFlow.Inventory.Api.Persistence;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();     // OTel, health, service discovery

builder.Services.AddDbContext<InventoryDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("orderflow-inventory"),
        sql => sql.EnableRetryOnFailure()));   // transient SQL faults are normal in cloud DBs

// ---- Messaging: subscribe to Order's topic, own Inventory's topic ----
builder.Services.AddOrderFlowMessaging<InventoryDbContext>(
    builder.Configuration,
    consumerName: "inventory-service",         // Kafka group id AND Inbox discriminator
    subscribeTopics: [Topics.Orders],          // it reacts to OrderPlaced / OrderCancelled
    ownedTopics: [Topics.Inventory]);      // it may only publish its own facts

// ---- Handlers: registering one is how you subscribe to an event type ----
builder.Services.AddScoped<IIntegrationEventHandler<OrderPlaced>, OrderPlacedHandler>();
builder.Services.AddScoped<IIntegrationEventHandler<OrderCancelled>, OrderCancelledHandler>();

// TODO: inventory_query.proto is still empty, so there is no generated service base to
// implement and nothing to map. Restore these once the contract exists:
//   builder.Services.AddGrpc();               (needs the Grpc.AspNetCore package)
//   app.MapGrpcService<InventoryQueryService>();

var app = builder.Build();

app.MapDefaultEndpoints();          // /health, /alive — used by k8s probes in Week 3

// TODO: StockSeeder is still empty; without it there is no stock to reserve, so the saga
// will always fail at the first step. Restore once written:
//   await app.MigrateAndSeedAsync();
app.Run();
