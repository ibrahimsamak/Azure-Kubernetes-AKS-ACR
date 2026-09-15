using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Contracts.Orders;
using OrderFlow.Inventory.Api.Handlers;
using OrderFlow.Inventory.Api.Persistence;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddDbContext<InventoryDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("orderflow-inventory"),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddOrderFlowMessaging<InventoryDbContext>(
    builder.Configuration,
    consumerName: "inventory-service",
    subscribeTopics: [Topics.Orders],
    ownedTopics: [Topics.Inventory]);

builder.Services.AddScoped<IIntegrationEventHandler<OrderPlaced>, OrderPlacedHandler>();
builder.Services.AddScoped<IIntegrationEventHandler<OrderCancelled>, OrderCancelledHandler>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.Run();
