using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderFlow.Order.Application.Abstractions;
using OrderFlow.Order.Application.Orders.Commands.PlaceOrder;
using OrderFlow.Order.Application.Sagas;
using OrderFlow.Order.Infrastructure.Persistence.Interceptors;
using OrderFlow.Order.Infrastructure.Persistence.Repositories;
using OrderFlow.Order.Infrastructure.Sagas;

namespace OrderFlow.Order.Infrastructure.Persistence;

public static class OrderPersistenceRegistration
{
    public static IHostApplicationBuilder AddOrderPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Stateless, so one instance serves every DbContext.
        builder.Services.AddSingleton<ConvertDomainEventsToOutboxInterceptor>();

        // Aspire integration: reads ConnectionStrings:orderflow-orders (injected by the AppHost)
        // and adds SQL Server retry-on-failure, a health check and OpenTelemetry tracing.
        builder.AddSqlServerDbContext<OrderDbContext>(OrderDbContext.ConnectionName);

        // AddSqlServerDbContext's options callback has no IServiceProvider, so the interceptor
        // is attached here, where it can come from DI.
        builder.Services.ConfigureDbContext<OrderDbContext>((sp, options) =>
            options.AddInterceptors(sp.GetRequiredService<ConvertDomainEventsToOutboxInterceptor>()));

        // Scoped: they share the request's DbContext, which is what makes one
        // IUnitOfWork.SaveChangesAsync commit the order and its saga together.
        builder.Services.AddScoped<IOrderRepository, OrderRepository>();
        builder.Services.AddScoped<IOrderSagaRepository, OrderSagaRepository>();
        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
        builder.Services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();

        builder.Services.AddScoped<PlaceOrderCommandHandler>();
        builder.Services.AddScoped<GetOrderSagaStatusQueryHandler>();

        // Nothing else notices a saga that simply went quiet, so a scanner has to.
        builder.Services.AddHostedService<SagaTimeoutService>();

        // Idempotency keys are only useful for the length of a client retry window.
        builder.Services.AddHostedService<IdempotencyCleanupService>();

        // Outbox dispatcher + cleanup are registered by AddOrderMessaging (they need Kafka).

        return builder;
    }
}
