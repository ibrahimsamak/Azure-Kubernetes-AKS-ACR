var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();             // OTel, health checks, service discovery
builder.AddOrderFlowAuthentication();     // same validation as Order, audience orderflow-api

builder.Services.AddAuthorizationBuilder()
    // The gateway only checks "a valid user token for our API". WHAT the user may do is decided by
    // the service that owns the data — the gateway doesn't know what an order is, and shouldn't.
    .AddPolicy("authenticated-user", p => p.RequireAuthenticatedUser());

builder.Services.AddReverseProxy()
    // Resolves "http://order" through service discovery under Aspire; in Kubernetes the
    // address "http://order:8080" is just DNS for the k8s Service and passes through.
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();
app.MapReverseProxy();                    // routes carry their policy in configuration

app.Run();
