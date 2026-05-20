using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure.ClaimCheck;
using Edict.Azure.TableStorage;
using Edict.Telemetry;
using Edict.Core;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;

using OpenTelemetry;

using Orleans.Hosting;
using Orleans.Serialization;

using Sample.Silo.Orders;

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

        var tableServiceClient = new TableServiceClient(tableConnectionString);
        silo.Services.AddSingleton(tableServiceClient);
        silo.Services.AddSingleton(new BlobServiceClient(blobConnectionString));
        silo.Services.AddSingleton<IEdictTableStoreFactory>(
            _ => new AzureTableWriteStoreFactory(tableServiceClient));

        silo.AddAzureTableGrainStorage("PubSubStore", options =>
            options.TableServiceClient = tableServiceClient);
        silo.AddAzureTableGrainStorage("edict-state", options =>
            options.TableServiceClient = tableServiceClient);
        // A saga's SendCommand effect drains in-silo through IEdictSender, so
        // the silo needs the generated route map too (ADR 0020).
        silo.Services.AddEdict();
        // Claim-check store + tuned ClaimCheckPolicy must be in DI before
        // AddEdictOutbox so its TryAddSingleton(default policy) is a no-op
        // (ADR 0024).
        silo.Services.AddEdictAzureClaimCheck();
        silo.Services.AddEdictOutbox();
        silo.UseAzureTableReminderService(options =>
            options.TableServiceClient = tableServiceClient);
        silo.AddAzureQueueStreams("edict", configure =>
        {
            configure.ConfigureAzureQueue(opt => opt.Configure(o =>
                o.QueueServiceClient = new QueueServiceClient(queueConnectionString)));
            configure.ConfigurePullingAgent(opt => opt.Configure(o =>
                o.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(500)));
        });
    })
    .ConfigureServices(services =>
    {
        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(EdictDiagnostics.SourceName))
            .UseOtlpExporter();
    })
    .Build();

await host.RunAsync();
