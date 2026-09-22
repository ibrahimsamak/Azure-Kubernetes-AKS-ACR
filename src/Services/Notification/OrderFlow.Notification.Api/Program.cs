using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Contracts.Orders;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.DependencyInjection;
using OrderFlow.Notification.Api.Handlers;
using OrderFlow.Notification.Api.Persistence;
using OrderFlow.Notification.Api.RabbitMq;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using OrderFlow.Notification.Api.ServiceBus;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOrderFlowAzure();

var serviceBusNamespace = builder.Configuration["ServiceBus:FullyQualifiedNamespace"]
    ?? throw new InvalidOperationException("ServiceBus:FullyQualifiedNamespace is not configured.");

builder.Services.AddSingleton(sp =>
    new ServiceBusClient(serviceBusNamespace, sp.GetRequiredService<TokenCredential>()));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<ServiceBusClient>().CreateSender("notifications"));
builder.Services.AddSingleton<ServiceBusNotificationPublisher>();

builder.Services.AddDbContext<NotificationDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString(NotificationDbContext.ConnectionName),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<NotificationDbContext>("notifications-db", tags: [Extensions.ReadyTag]);

builder.Services.AddOrderFlowMessaging<NotificationDbContext>(
    builder.Configuration,
    consumerName: "notification-service",
    subscribeTopics: [Topics.Orders],
    ownedTopics: []);

builder.Services.AddNotificationFanout(builder.Configuration);

builder.Services.AddScoped<IIntegrationEventHandler<OrderConfirmed>, OrderConfirmedHandler>();
builder.Services.AddScoped<IIntegrationEventHandler<OrderCancelled>, OrderCancelledHandler>();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
}

app.Run();
