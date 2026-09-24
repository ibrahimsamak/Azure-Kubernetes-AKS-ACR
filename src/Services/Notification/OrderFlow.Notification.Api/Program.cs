using Azure.Core;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Contracts.Orders;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.DependencyInjection;
using OrderFlow.Notification.Api.Handlers;
using OrderFlow.Notification.Api.Persistence;
using OrderFlow.Notification.Api.RabbitMq;
using OrderFlow.Notification.Api.ServiceBus;
using OrderFlow.Notification.Api.Tasks;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();     // OTel, health, service discovery
builder.AddOrderFlowAzure();      // TokenCredential (+ Key Vault when configured)

// The task transport is picked by CONFIGURATION, not by environment name:
//   AKS (Helm sets ServiceBus__FullyQualifiedNamespace) -> Service Bus topic -> Azure Functions
//   Aspire / compose / the CI e2e stack                 -> RabbitMQ fanout (Week 2)
// Registering both would open a RabbitMQ connection in Azure that can never succeed.
var serviceBusNamespace = builder.Configuration["ServiceBus:FullyQualifiedNamespace"];
if (!string.IsNullOrWhiteSpace(serviceBusNamespace))
{
    // ServiceBusClient and ServiceBusSender are thread-safe and meant to be singletons:
    // they hold the AMQP connection. Creating one per message is a classic performance bug.
    builder.Services.AddSingleton(sp =>
        new ServiceBusClient(serviceBusNamespace, sp.GetRequiredService<TokenCredential>()));
    builder.Services.AddSingleton(sp =>
        sp.GetRequiredService<ServiceBusClient>().CreateSender("notifications"));
    builder.Services.AddSingleton<INotificationTaskPublisher, ServiceBusNotificationPublisher>();
}
else
{
    builder.Services.AddNotificationFanout(builder.Configuration);
    builder.Services.AddSingleton<INotificationTaskPublisher, RabbitMqNotificationPublisher>();
}

builder.Services.AddDbContext<NotificationDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString(NotificationDbContext.ConnectionName),
        sql => sql.EnableRetryOnFailure()));

// Readiness = "can this pod reach its own database?". Kafka and Service Bus are deliberately
// not here: a broker blip must not take every pod out of the Service's endpoints.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<NotificationDbContext>("notifications-db", tags: [Extensions.ReadyTag]);

// ---- Messaging: listen to Orders, publish nothing ----
// Notification is a pure consumer: it owns no topic because it produces no facts about
// the business, only side effects.
builder.Services.AddOrderFlowMessaging<NotificationDbContext>(
    builder.Configuration,
    consumerName: "notification-service",      // Kafka group id AND Inbox discriminator
    subscribeTopics: [Topics.Orders],
    ownedTopics: []);

// ---- Handlers: registering one is how you subscribe to an event type ----
builder.Services.AddScoped<IIntegrationEventHandler<OrderConfirmed>, OrderConfirmedHandler>();
builder.Services.AddScoped<IIntegrationEventHandler<OrderCancelled>, OrderCancelledHandler>();

var app = builder.Build();

app.MapDefaultEndpoints();          // /health, /alive

// Opt-out, so a migration Job can take over later (stretch goal).
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
}

app.Run();
