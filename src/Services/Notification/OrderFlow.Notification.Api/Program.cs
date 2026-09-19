using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Contracts.Orders;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.DependencyInjection;
using OrderFlow.Notification.Api.Handlers;
using OrderFlow.Notification.Api.Persistence;
using OrderFlow.Notification.Api.RabbitMq;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();     // OTel, health, service discovery

builder.Services.AddDbContext<NotificationDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString(NotificationDbContext.ConnectionName),
        sql => sql.EnableRetryOnFailure()));

// Readiness = "can this pod reach its own database?". Kafka and RabbitMQ are deliberately
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

// Kafka carries the FACT, RabbitMQ carries the TASK.
builder.Services.AddNotificationFanout(builder.Configuration);

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
