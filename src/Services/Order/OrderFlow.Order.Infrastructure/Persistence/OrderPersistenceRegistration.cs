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

        builder.Services.AddSingleton<ConvertDomainEventsToOutboxInterceptor>();

        builder.AddSqlServerDbContext<OrderDbContext>(OrderDbContext.ConnectionName);

        builder.Services.ConfigureDbContext<OrderDbContext>((sp, options) =>
            options.AddInterceptors(sp.GetRequiredService<ConvertDomainEventsToOutboxInterceptor>()));

        builder.Services.AddScoped<IOrderRepository, OrderRepository>();
        builder.Services.AddScoped<IOrderSagaRepository, OrderSagaRepository>();
        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

        builder.Services.AddScoped<PlaceOrderCommandHandler>();
        builder.Services.AddScoped<GetOrderSagaStatusQueryHandler>();

        builder.Services.AddHostedService<SagaTimeoutService>();

        return builder;
    }
}
