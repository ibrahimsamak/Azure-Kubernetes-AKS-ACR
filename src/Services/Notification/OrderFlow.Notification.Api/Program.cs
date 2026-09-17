using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Contracts.Orders;
using OrderFlow.Messaging.Abstractions;
using OrderFlow.Messaging.DependencyInjection;
using OrderFlow.Notification.Api.Handlers;
using OrderFlow.Notification.Api.Persistence;
using OrderFlow.Notification.Api.RabbitMq;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddDbContext<NotificationDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString(NotificationDbContext.ConnectionName),
        sql => sql.EnableRetryOnFailure()));

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

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
}

app.Run();
