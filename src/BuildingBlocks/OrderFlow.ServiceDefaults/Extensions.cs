using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/aspire/service-defaults
public static class Extensions
{
    public const string LivePath = "/health/live";
    public const string ReadyPath = "/health/ready";

    public const string LiveTag = "live";
    public const string ReadyTag = "ready";


    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();
            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Kubernetes sends SIGTERM, waits terminationGracePeriodSeconds (30s in our chart),
        // then SIGKILLs. Give hosted services (Kafka consumer, outbox dispatcher) 25s to
        // finish the current message and commit, so shutdown never becomes a crash.
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(25));

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;      // CorrelationId / MessageId scopes become log attributes
        });

        // Configured through OPTIONS rather than the AddAspNetCoreInstrumentation(o => ...) lambda, so
        // the same settings also apply to the instrumentation the Azure Monitor distro registers (part 2).
        builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(o =>
        {
            // Probes hit every pod every 10s — tracing them buries real traffic, and costs money in Azure.
            o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");

            // APIM stamps every request with X-Correlation-Id. Keeping it on our server span lets you
            // go from an APIM log line to our trace.
            o.EnrichWithHttpRequest = (activity, request) =>
            {
                if (request.Headers.TryGetValue("X-Correlation-Id", out var edgeId))
                {
                    activity.SetTag("orderflow.edge_correlation_id", edgeId.ToString());
                }
            };
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                // Every Meter we own is named "OrderFlow.<Area>": one wildcard, no names to mistype.
                .AddMeter("OrderFlow.*"))
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddSource("OrderFlow.*")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());   // also covers the gRPC client (it's HttpClient underneath)

        builder.AddOpenTelemetryExporters();
        return builder;
    }


    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        // if (useOtlpExporter)
        // {
        //     builder.Services.AddOpenTelemetry().UseOtlpExporter();
        // }

           // One destination, chosen by configuration:
        //   AKS    -> APPLICATIONINSIGHTS_CONNECTION_STRING (Helm values)  -> Azure Monitor distro
        //   Aspire -> OTEL_EXPORTER_OTLP_ENDPOINT (set by the AppHost)     -> the dashboard
        if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            builder.Services.AddOpenTelemetry().UseAzureMonitor(o =>
            {
                // Entra ID auth for ingestion. The App Insights resource has local auth DISABLED, so the
                // connection string alone cannot be used to send (or spoof) telemetry.
                o.Credential = AzureExtensions.Credential;

                // Fixed-ratio sampling decides from the TRACE ID, so all five services keep or drop
                // the same trace: a sampled saga is always complete. 1.0 = keep everything, which is
                // right at demo volumes; lower it (0.25, 0.1) when ingestion cost matters.
                o.SamplingRatio = 1.0F;
            });
        }
        else if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
        
        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Liveness = "the process can run code". Deliberately NO dependencies here.
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Mapped in ALL environments now: the kubelet calls these in production.
        // They're safe to expose inside the cluster because the default response is just
        // "Healthy"/"Unhealthy" — no exception text, no connection strings.
        // They are NOT routed publicly: the ingress only forwards /api/*.

        app.MapHealthChecks(LivePath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(LiveTag)
        });
        app.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(ReadyTag)
        });

        return app;
    }
}
