using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure;
using Edict.Core;
using Edict.Core.Serialization;
using Edict.Telemetry;

using OpenTelemetry;

using Orleans.Serialization;

using Sample.Silo.Orders;
using Sample.Silo.Orders.CommandHandlers;

var host = Host.CreateDefaultBuilder(args)
    .UseOrleans((context, silo) =>
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

        silo.AddEdictAzurePersistence(o =>
        {
            o.GrainStateContainerName     = "edict-state";
            o.ClaimCheckBlobContainerName = "edict-claim-check";
            o.DeadLetterTableName         = "edict-dead-letter";
            o.TableServiceClient          = new TableServiceClient(tableConnectionString);
            o.BlobServiceClient           = new BlobServiceClient(blobConnectionString);
        });
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton<IEmailNotifier, LoggingEmailNotifier>();

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(EdictDiagnostics.SourceName))
            .UseOtlpExporter();
    })
    .Build();

await host.RunAsync();
