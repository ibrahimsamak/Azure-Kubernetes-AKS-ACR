using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Contracts.Inventory;
using OrderFlow.Contracts.Orders;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.DependencyInjection;
using OrderFlow.Payment.Api.Gateway;
using OrderFlow.Payment.Api.Handlers;
using OrderFlow.Payment.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();     // OTel, health, service discovery

builder.Services.AddDbContext<PaymentDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString(PaymentDbContext.ConnectionName),
        sql => sql.EnableRetryOnFailure()));   // transient SQL faults are normal in cloud DBs

// ---- Messaging: subscribe to Orders and Inventory, own Payments ----
// Orders for the amount (OrderPlaced) and the cancellation, Inventory for the go-ahead.
builder.Services.AddOrderFlowMessaging<PaymentDbContext>(
    builder.Configuration,
    consumerName: "payment-service",           // Kafka group id AND Inbox discriminator
    subscribeTopics: [Topics.Orders, Topics.Inventory],
    ownedTopics: [Topics.Payments]);           // it may only publish its own facts

// Swapped for a real PSP client in Week 3; the interface is the seam.
builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();

// ---- Handlers: registering one is how you subscribe to an event type ----
builder.Services.AddScoped<IIntegrationEventHandler<OrderPlaced>, OrderPlacedHandler>();
builder.Services.AddScoped<IIntegrationEventHandler<OrderCancelled>, OrderCancelledHandler>();
builder.Services.AddScoped<IIntegrationEventHandler<StockReserved>, StockReservedHandler>();

var app = builder.Build();

app.MapDefaultEndpoints();          // /health, /alive

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
}

app.Run();
