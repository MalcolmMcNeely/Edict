using Edict.Core.Diagnostics;
using Edict.Core.Serialization;
using OpenTelemetry;
using Orleans.Serialization;
using Sample.Silo.Orders;

var host = Host.CreateDefaultBuilder(args)
    .UseOrleans(silo =>
    {
        silo.UseLocalhostClustering();
        silo.Services.AddSerializer(ser =>
        {
            ser.AddAssembly(typeof(OrderGrain).Assembly);
            ser.AddEdictContractSerializer();
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
