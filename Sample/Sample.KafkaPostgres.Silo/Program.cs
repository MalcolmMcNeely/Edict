using Confluent.Kafka;

using Edict.Contracts.Audit;
using Edict.Core;
using Edict.Core.Audit;
using Edict.Core.Serialization;
using Edict.Kafka;
using Edict.Postgres;
using Edict.Telemetry;

using OpenTelemetry;

using Orleans.Serialization;

using Sample.Domain.Diagnostics.Metrics;
using Sample.Domain.Fulfillment;
using Sample.Domain.Orders;
using Sample.Domain.Orders.CommandHandlers;
using Sample.Domain.Orders.Notifications;
using Sample.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
// Aspire's WaitFor(silo) honors this — Web does not construct its Orleans
// client until the gateway is open, so cold-start cannot race.
builder.AddOrleansSiloReadyHealthCheck();

builder.Logging.AddFilter("Orleans", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting", LogLevel.Warning);

builder.Host.UseOrleans((context, silo) =>
{
    silo.UseLocalhostClustering();
    silo.Services.AddSerializer(ser =>
    {
        ser.AddAssembly(typeof(OrderCommandHandler).Assembly);
        ser.AddEdictContractSerializer();
    });

    // The AppHost injects these. Standalone is not supported — this silo's
    // job is to be the Kafka+Postgres half of the side-by-side sample;
    // running it without the AppHost would silently drop the substrate.
    var bootstrapServers = context.Configuration.GetConnectionString("kafka")
        ?? throw new InvalidOperationException(
            "Kafka connection string 'kafka' missing. Run via Sample.KafkaPostgres.AppHost.");
    var postgresConnectionString = context.Configuration.GetConnectionString("appdb")
        ?? throw new InvalidOperationException(
            "Postgres connection string 'appdb' missing. Run via Sample.KafkaPostgres.AppHost.");

    // Every option is on its own line at its literal default — the
    // sample doubles as the config catalogue, so a consumer can compare
    // and tune from this file.
    silo.AddEdict(o =>
    {
        o.IdempotencyWindowSize     = 100;
        o.CorrelationWindowSize     = 100;
        o.ProjectionReadTimeout     = TimeSpan.FromSeconds(2);
        // Tuned for demo, not production: OutboxBaseDelay+OutboxMaxAttempts
        // are shrunk so the Dead Letter buttons promote within ~5 seconds
        // instead of the multi-minute production retry budget.
        o.OutboxBaseDelay           = TimeSpan.FromSeconds(1);
        o.OutboxMaxDelay            = TimeSpan.FromMinutes(5);
        o.OutboxMaxAttempts         = 3;
        o.OutboxJitterFraction      = 0.2;
        o.OutboxDrainReminderPeriod = TimeSpan.FromMinutes(1);
    },
    saga =>
    {
        // The absolute lifetime cap for any saga without its own
        // [EdictSagaTimeout]. Ships finite at 7 days; null is fully opt-in.
        saga.DefaultTimeout = TimeSpan.FromDays(7);
    },
    schedule =>
    {
        // The absolute lifetime cap for any Command Handler schedule started
        // without its own timeout:. Ships finite at 7 days; null is fully opt-in.
        schedule.DefaultTimeout = TimeSpan.FromDays(7);
    });

    silo.AddEdictKafkaStreams(o =>
    {
        o.StreamProviderName  = "edict";
        o.BootstrapServers    = bootstrapServers;
        o.ConsumerGroupId     = "edict-sample-silo";
        o.PartitionCount      = 32;
        // Default rf=3 + provisioner auto-clamp lets the same Program.cs
        // work against a single-broker Aspire dev cluster (clamps to 1)
        // and a real production cluster (uses 3). Assigning explicitly
        // would flip into strict mode and throw on the dev container.
        o.Compression         = CompressionType.Lz4;
        o.AutoOffsetReset     = AutoOffsetReset.Latest;
    });

    silo.AddEdictPostgresPersistence(o =>
    {
        o.ConnectionString          = postgresConnectionString;
        o.Invariant                 = "Npgsql";
        o.GrainStorageProviderName  = "edict-state";
        o.ClaimCheckTableName       = "edict_claim_check";
        o.BootstrapSchema           = true;
    });

    // Turn on the audit log: WithAudit() captures each decision into the Postgres
    // WORM store AddEdictPostgresPersistence registered. The resolver supplies the
    // actor for any send that originates on the silo itself (background work has no
    // authenticated user), so the silo mints its own system principal — Edict ships
    // no sentinel. User-driven sends from the web carry their own actor explicitly.
    silo.Services.AddEdictAudit(() => EdictPrincipal.Of("kafkapostgres-silo"));
    silo.WithAudit();
});

// The Event Handler reaches out to the Web-hosted notifications sink over HTTP
// (service discovery resolves "web"), modelling a real external API call.
builder.Services.AddHttpEmailNotifier();
builder.Services.AddSingleton<IWarehouseGateway, LoggingWarehouseGateway>();

// Silo-side MeterListener feeding the Sample.Web Live Metrics spoke.
// Same singleton resolved as IHostedService (starts/stops the listener
// with the silo) and as itself (read by EdictMetricsProbeGrain).
builder.Services.AddSingleton<EdictMetricsAggregator>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<EdictMetricsAggregator>());

// AddServiceDefaults already calls UseOtlpExporter once OTEL_EXPORTER_OTLP_ENDPOINT
// is set (Aspire injects it). Only the Edict-specific Meter + ActivitySource
// need adding here.
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(EdictDiagnostics.SourceName))
    .WithTracing(tracing => tracing.AddSource(EdictDiagnostics.SourceName));

var app = builder.Build();

app.MapDefaultEndpoints();

await app.RunAsync();
