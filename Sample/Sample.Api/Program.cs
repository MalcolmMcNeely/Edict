using Azure.Data.Tables;

using Edict.Azure.TableStorage;
using Edict.Contracts.TableStorage;
using Edict.Telemetry;
using Edict.Core.Grains;
using Edict.Core.Serialization;
using Edict.Generated;

using OpenTelemetry;
using OpenTelemetry.Trace;

using Orleans.Serialization;

using Sample.Api.Orders;
using Sample.Silo.Orders;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Host.UseOrleansClient(client =>
    {
        client.UseLocalhostClustering();
        client.Services.AddSerializer(ser =>
        {
            ser.AddAssembly(typeof(IOrderGrain).Assembly);
            ser.AddAssembly(typeof(IEdictCommandHandler).Assembly);
            ser.AddEdictContractSerializer();
        });
    });

    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing =>
        {
            tracing.AddSource(EdictDiagnostics.SourceName);
            tracing.AddAspNetCoreInstrumentation();
        })
        .UseOtlpExporter();

    var tableConnectionString = builder.Configuration.GetConnectionString("AzureStorage")
                                ?? "UseDevelopmentStorage=true";
    var tableServiceClient = new TableServiceClient(tableConnectionString);
    builder.Services.AddSingleton(tableServiceClient);
    builder.Services.AddSingleton<IEdictTableRepository<OrderStatusRow>>(
        _ => new AzureTableRepository<OrderStatusRow>(tableServiceClient, "ordersbystatus"));
}

builder.Services.AddEdict();

var app = builder.Build();
app.MapOrdersEndpoints();
app.Run();

public partial class Program { }
