using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure.Persistence;
using Edict.Azure.Streaming;
using Edict.Azure.Streaming.ClaimCheck;
using Edict.Core;
using Edict.Core.Serialization;
using Edict.Telemetry;

using OpenTelemetry;

using Orleans.Serialization;

using Sample.Domain.Diagnostics.Metrics;
using Sample.Domain.Orders;
using Sample.Domain.Orders.CommandHandlers;
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

    var tableConnectionString = context.Configuration.GetConnectionString("tables")
                                ?? "UseDevelopmentStorage=true";
    var queueConnectionString = context.Configuration.GetConnectionString("queues")
                                ?? "UseDevelopmentStorage=true";
    var blobConnectionString = context.Configuration.GetConnectionString("blobs")
                                ?? "UseDevelopmentStorage=true";

    // Every option is on its own line at its literal default — the
    // sample doubles as the config catalogue, so a consumer can compare
    // and tune from this file.
    silo.AddEdict(o =>
    {
        o.IdempotencyWindowSize     = 100;
        // Tuned for demo, not production: OutboxBaseDelay+OutboxMaxAttempts
        // are shrunk so the Dead Letter buttons promote within ~5 seconds
        // instead of the multi-minute production retry budget.
        o.OutboxBaseDelay           = TimeSpan.FromSeconds(1);
        o.OutboxMaxDelay            = TimeSpan.FromMinutes(5);
        o.OutboxMaxAttempts         = 3;
        o.OutboxJitterFraction      = 0.2;
        o.OutboxDrainReminderPeriod = TimeSpan.FromMinutes(1);
    });

    silo.AddEdictAzureStreams(o =>
    {
        o.StreamProviderName       = "edict";
        // Tuned for demo, not production: ClaimCheckThresholdBytes is lowered
        // so the Claim Check button trips the oversize-event path on a
        // single padded line item instead of needing realistic payload sizes.
        o.ClaimCheckThresholdBytes = 4 * 1024;
        o.QueuePollingPeriod       = TimeSpan.FromMilliseconds(500);
        o.QueueServiceClient       = new QueueServiceClient(queueConnectionString);
    });

    silo.AddEdictAzureBlobClaimCheck(o =>
    {
        o.ContainerName      = "edict-claim-check";
        o.BlobServiceClient  = new BlobServiceClient(blobConnectionString);
    });

    silo.AddEdictAzurePersistence(o =>
    {
        o.GrainStateContainerName = "edict-state";
        o.DeadLetterTableName     = "edict-dead-letter";
        o.TableServiceClient      = new TableServiceClient(tableConnectionString);
        o.BlobServiceClient       = new BlobServiceClient(blobConnectionString);
    });
});

builder.Services.AddSingleton<IEmailNotifier, LoggingEmailNotifier>();

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
