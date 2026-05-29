using Confluent.Kafka;

using Edict.Core;
using Edict.Core.Serialization;
using Edict.Kafka;
using Edict.Postgres;
using Edict.Telemetry;

using OpenTelemetry;

using Orleans.Serialization;

using Sample.Domain.Orders;
using Sample.Domain.Orders.CommandHandlers;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        // Silence Orleans stream/queue polling so the Aspire dashboard shows
        // only Edict-relevant telemetry. Mirror of the filter in Sample.ServiceDefaults.
        logging.AddFilter("Orleans", LogLevel.Warning);
        logging.AddFilter("Microsoft.Hosting", LogLevel.Warning);
    })
    .UseOrleans((context, silo) =>
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

        // three Action lambdas. Every option is on its own line at
        // its literal default — the sample doubles as the config catalogue, so
        // a consumer can compare and tune from this file.
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
            o.DeadLetterTableName       = "edict_dead_letter";
            o.ClaimCheckTableName       = "edict_claim_check";
            o.BootstrapSchema           = true;
        });
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton<IEmailNotifier, LoggingEmailNotifier>();

        services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddMeter(EdictDiagnostics.SourceName))
            .WithTracing(tracing => tracing.AddSource(EdictDiagnostics.SourceName))
            .UseOtlpExporter();
    })
    .Build();

await host.RunAsync();
