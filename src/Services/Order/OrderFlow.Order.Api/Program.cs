using Azure.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using OrderFlow.Grpc.Inventory;
using OrderFlow.Order.Api.Security;
using OrderFlow.Order.Application.Abstractions;
using OrderFlow.Order.Infrastructure.Grpc;
using OrderFlow.Order.Infrastructure.Messaging;
using OrderFlow.Order.Infrastructure.Persistence;
using Polly;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOrderFlowAzure();
builder.AddOrderFlowAuthentication();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(OrderPolicies.PlaceOrder, p => p
        .RequireScope(OrderPolicies.Scope)
        .RequireRole(OrderPolicies.CustomerRole))
    .AddPolicy(OrderPolicies.ReadOrders, p => p
        .RequireScope(OrderPolicies.Scope)
        .RequireRole(OrderPolicies.CustomerRole, OrderPolicies.SupportRole));

builder.AddOrderPersistence();
builder.AddOrderMessaging();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrderDbContext>("orders-db", tags: [Extensions.ReadyTag]);

var inventoryClient = builder.Services.AddGrpcClient<InventoryQuery.InventoryQueryClient>(o =>
    o.Address = new Uri(builder.Configuration["Inventory:GrpcAddress"] ?? "https://inventory"));

inventoryClient.AddStandardResilienceHandler(o =>
{
    o.Retry.MaxRetryAttempts = 2;
    o.Retry.Delay = TimeSpan.FromMilliseconds(100);
    o.Retry.BackoffType = DelayBackoffType.Exponential;

    o.CircuitBreaker.FailureRatio = 0.5;
    o.CircuitBreaker.MinimumThroughput = 10;
    o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);

    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(1);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(2);
});

var inventoryTokenScope = builder.Configuration["Inventory:TokenScope"];
if (!string.IsNullOrWhiteSpace(inventoryTokenScope))
{
    inventoryClient
        .AddCallCredentials(async (context, metadata, sp) =>
        {
            var token = await sp.GetRequiredService<TokenCredential>()
                .GetTokenAsync(new TokenRequestContext([inventoryTokenScope]), context.CancellationToken);
            metadata.Add("Authorization", $"Bearer {token.Token}");
        })
        .ConfigureChannel(o => o.UnsafeUseInsecureChannelCallCredentials = true);
}

builder.Services.AddScoped<IInventoryQueryClient, InventoryQueryGrpcClient>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<OrderDbContext>().Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapControllers();

app.Run();
