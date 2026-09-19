var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();     // OTel, health checks, service discovery

builder.Services.AddReverseProxy()
    // Resolves "http://order" through service discovery under Aspire; in Kubernetes the
    // address "http://order:8080" is just DNS for the k8s Service and passes through.
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();



var app = builder.Build();

app.MapDefaultEndpoints();
app.MapReverseProxy();

app.Run();
