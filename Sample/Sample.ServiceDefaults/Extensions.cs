using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using Orleans;
using Orleans.Runtime;

namespace Sample.ServiceDefaults;

public static class Extensions
{
    const string HealthEndpointPath = "/health";
    const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // The sample's only telemetry consumer is the Edict ActivitySource. Anything
        // else (Orleans queue polling, Blazor render pipeline, HttpClient hops) just
        // drowns the Aspire dashboard. Each call site adds AddSource(EdictDiagnostics.SourceName).
        builder.Logging.AddFilter("Orleans", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Hosting", LogLevel.Warning);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddRuntimeInstrumentation();
                // Dormant until the metrics PRD lands an Edict instrument; once any
                // Edict meter is registered, exemplars are sampled from in-flight
                // traces with zero further wiring.
                metrics.SetExemplarFilter(ExemplarFilterType.TraceBased);
            })
            .WithTracing(_ =>
            {
                // Edict source is added by each project's own ConfigureServices call.
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddRequestTimeouts(
            configure: static timeouts => timeouts.AddPolicy("HealthChecks", TimeSpan.FromSeconds(5)));

        builder.Services.AddOutputCache(
            configureOptions: static caching => caching.AddPolicy("HealthChecks", build => build.Expire(TimeSpan.FromSeconds(10))));

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    // Flips Healthy only after the silo crosses ServiceLifecycleStage.Active —
    // i.e. once the Orleans gateway port is accepting connections. AppHost's
    // WaitFor(silo) honors this so the Web Orleans client cannot race the
    // gateway-open moment on cold start.
    public static TBuilder AddOrleansSiloReadyHealthCheck<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddSingleton<OrleansReadyGate>();
        builder.Services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(serviceProvider =>
            serviceProvider.GetRequiredService<OrleansReadyGate>());
        builder.Services.AddSingleton<OrleansReadyHealthCheck>();
        builder.Services.AddHealthChecks()
            .AddCheck<OrleansReadyHealthCheck>("orleans-silo-ready", tags: ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.UseRequestTimeouts();
        app.UseOutputCache();

        var healthChecks = app.MapGroup("");

        healthChecks
            .CacheOutput("HealthChecks")
            .WithRequestTimeout("HealthChecks");

        healthChecks.MapHealthChecks(HealthEndpointPath);

        healthChecks.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });

        return app;
    }
}
