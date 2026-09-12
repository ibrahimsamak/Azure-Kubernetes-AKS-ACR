using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderFlow.Order.Infrastructure.Persistence.Interceptors;

namespace OrderFlow.Order.Infrastructure.Persistence;

public static class OrderPersistenceRegistration
{
    public static IHostApplicationBuilder AddOrderPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<ConvertDomainEventsToOutboxInterceptor>();

        builder.AddSqlServerDbContext<OrderDbContext>(OrderDbContext.ConnectionName);

        builder.Services.ConfigureDbContext<OrderDbContext>((sp, options) =>
            options.AddInterceptors(sp.GetRequiredService<ConvertDomainEventsToOutboxInterceptor>()));

        return builder;
    }
}
