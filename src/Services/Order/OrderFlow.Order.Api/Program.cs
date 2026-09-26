using Azure.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;           // RequireScope
using OrderFlow.Grpc.Inventory;
using OrderFlow.Order.Api.Security;
using OrderFlow.Order.Application.Abstractions;
using OrderFlow.Order.Infrastructure.Grpc;
using OrderFlow.Order.Infrastructure.Messaging;
using OrderFlow.Order.Infrastructure.Persistence;
using Polly;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();     // OTel, health, service discovery
builder.AddOrderFlowAzure();     // NEW
builder.AddOrderFlowAuthentication();
builder.Services.AddAuthorizationBuilder()
    // The APP must hold the delegated scope AND the USER must hold the role. Either alone is not enough.
    .AddPolicy(OrderPolicies.PlaceOrder, p => p
        .RequireScope(OrderPolicies.Scope)
        .RequireRole(OrderPolicies.CustomerRole))
    .AddPolicy(OrderPolicies.ReadOrders, p => p
        .RequireScope(OrderPolicies.Scope)
        .RequireRole(OrderPolicies.CustomerRole, OrderPolicies.SupportRole));

// Add services to the container.
builder.AddOrderPersistence();
builder.AddOrderMessaging();

// Readiness = "can this pod do its job right now?" — for Order that means reaching its own
// database. Kafka and Redis are deliberately NOT here: if Kafka is down orders still land in
// the outbox, and marking pods unready would turn a broker blip into a checkout outage.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrderDbContext>("orders-db", tags: [Extensions.ReadyTag]);

var inventoryClient = builder.Services.AddGrpcClient<InventoryQuery.InventoryQueryClient>(o =>
    // "https://inventory" is resolved by Aspire service discovery, and in Kubernetes by the
    // Service name. Never a hard-coded host or port.
    o.Address = new Uri(builder.Configuration["Inventory:GrpcAddress"] ?? "https://inventory"));

inventoryClient.AddStandardResilienceHandler(o =>
{
    // Retry ONLY transient, idempotent failures. CheckAvailability is a pure read,
    // so retrying is safe — never apply this blindly to a mutating call.
    o.Retry.MaxRetryAttempts = 2;
    o.Retry.Delay = TimeSpan.FromMilliseconds(100);
    o.Retry.BackoffType = DelayBackoffType.Exponential;

    // Circuit breaker: stop hammering a service that is clearly down, and give it
    // room to recover instead of adding to its load.
    o.CircuitBreaker.FailureRatio = 0.5;
    o.CircuitBreaker.MinimumThroughput = 10;
    o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);

    // The standard handler requires TotalRequestTimeout > AttemptTimeout (default 10s).
    // This is a cheap advisory gRPC read, so use tight timeouts: 1s per attempt, 2s total.
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(1);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(2);
});

// Service-to-service auth: in Azure, every call carries an app-only token from Order's OWN managed
// identity (audience orderflow-inventory, role Inventory.Read). Locally the setting is absent and
// Inventory runs in Local auth mode.
var inventoryTokenScope = builder.Configuration["Inventory:TokenScope"];
if (!string.IsNullOrWhiteSpace(inventoryTokenScope))
{
    inventoryClient
        .AddCallCredentials(async (context, metadata, sp) =>
        {
            // DefaultAzureCredential caches the token until shortly before expiry: this is a
            // dictionary lookup on almost every call, not a round-trip to Entra.
            var token = await sp.GetRequiredService<TokenCredential>()
                .GetTokenAsync(new TokenRequestContext([inventoryTokenScope]), context.CancellationToken);
            metadata.Add("Authorization", $"Bearer {token.Token}");
        })
        // The in-cluster hop is plaintext h2c, and gRPC refuses to attach credentials to an insecure
        // channel unless told to. The production fix is mTLS between pods (service mesh) — see
        // docs/security.md. The token still proves WHO is calling; it just isn't encrypted in transit.
        .ConfigureChannel(o => o.UnsafeUseInsecureChannelCallCredentials = true);
}

builder.Services.AddScoped<IInventoryQueryClient, InventoryQueryGrpcClient>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    //app.MapOpenApi();
}

// Opt-out, so a migration Job can take over later (stretch goal). EF Core 9+ takes a
// database lock during Migrate(), so two replicas starting together are safe.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<OrderDbContext>().Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();          // /health/* stay anonymous: no policy is applied to them
app.MapControllers();

app.Run();
