using Azure.Data.Tables;
using Azure.Storage.Queues;

using Edict.Telemetry;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;

using OpenTelemetry;

using Orleans.Serialization;

using Sample.Silo.Orders;

var host = Host.CreateDefaultBuilder(args)
    .UseOrleans((context, silo) =>
    {
        silo.UseLocalhostClustering();
        silo.Services.AddSerializer(ser =>
        {
            ser.AddAssembly(typeof(OrderGrain).Assembly);
            ser.AddEdictContractSerializer();
        });

        var connStr = context.Configuration.GetConnectionString("AzureStorage")
                      ?? "UseDevelopmentStorage=true";

        var tableServiceClient = new TableServiceClient(connStr);
        silo.Services.AddSingleton(tableServiceClient);
        silo.Services.AddSingleton<IEdictTableStoreFactory>(
            _ => new AzureTableWriteStoreFactory(tableServiceClient));

        silo.AddMemoryGrainStorage("PubSubStore");
        silo.AddMemoryGrainStorage("edict-dedup");
        silo.AddAzureQueueStreams("edict", configure =>
        {
            configure.ConfigureAzureQueue(opt => opt.Configure(o =>
                o.QueueServiceClient = new QueueServiceClient(connStr)));
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
