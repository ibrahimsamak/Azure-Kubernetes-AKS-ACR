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

builder.AddServiceDefaults();

builder.Services.AddDbContext<PaymentDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString(PaymentDbContext.ConnectionName),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddOrderFlowMessaging<PaymentDbContext>(
    builder.Configuration,
    consumerName: "payment-service",
    subscribeTopics: [Topics.Orders, Topics.Inventory],
    ownedTopics: [Topics.Payments]);

builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();

builder.Services.AddScoped<IIntegrationEventHandler<OrderPlaced>, OrderPlacedHandler>();
builder.Services.AddScoped<IIntegrationEventHandler<OrderCancelled>, OrderCancelledHandler>();
builder.Services.AddScoped<IIntegrationEventHandler<StockReserved>, StockReservedHandler>();

var app = builder.Build();

app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
}

app.Run();
