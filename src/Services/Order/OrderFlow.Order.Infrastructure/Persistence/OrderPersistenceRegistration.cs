using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderFlow.Order.Infrastructure.Persistence.Interceptors;

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

        // Outbox dispatcher + cleanup are registered by AddOrderMessaging (they need Kafka).

        return builder;
    }
}
