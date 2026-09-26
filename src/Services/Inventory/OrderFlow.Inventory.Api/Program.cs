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

builder.AddServiceDefaults();     // OTel, health, service discovery
builder.AddOrderFlowAzure();     // NEW
builder.AddOrderFlowAuthentication();     // audience = orderflow-inventory (AzureAd:ClientId)
builder.Services.AddAuthorizationBuilder()
    // App-only tokens carry app roles in "roles", exactly like user tokens. No scope: nobody calls
    // this on behalf of a user.
    .AddPolicy("inventory.query", p => p.RequireRole("Inventory.Read"));

builder.Services.AddDbContext<InventoryDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("orderflow-inventory"),
        sql => sql.EnableRetryOnFailure()));   // transient SQL faults are normal in cloud DBs

// Readiness = "can this pod reach its own database?". Kafka is deliberately not here:
// a broker blip must not take every pod out of the Service's endpoints.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<InventoryDbContext>("inventory-db", tags: [Extensions.ReadyTag]);

// ---- Messaging: subscribe to Order's topic, own Inventory's topic ----
builder.Services.AddOrderFlowMessaging<InventoryDbContext>(
    builder.Configuration,
    consumerName: "inventory-service",         // Kafka group id AND Inbox discriminator
    subscribeTopics: [Topics.Orders],          // it reacts to OrderPlaced / OrderCancelled
    ownedTopics: [Topics.Inventory]);      // it may only publish its own facts

// ---- Handlers: registering one is how you subscribe to an event type ----
builder.Services.AddScoped<IIntegrationEventHandler<OrderPlaced>, OrderPlacedHandler>();
builder.Services.AddScoped<IIntegrationEventHandler<OrderCancelled>, OrderCancelledHandler>();

// ---- gRPC server (Day 6) ----
builder.Services.AddGrpc();

var app = builder.Build();

app.MapDefaultEndpoints();                // /health/* stay anonymous for the kubelet
app.UseAuthentication();
app.UseAuthorization();
// No token -> gRPC Unauthenticated; a token without the role -> PermissionDenied.
app.MapGrpcService<InventoryQueryService>().RequireAuthorization("inventory.query");

// Opt-out, so a migration Job can take over later (stretch goal).
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await app.MigrateAndSeedAsync();
}

app.Run();
