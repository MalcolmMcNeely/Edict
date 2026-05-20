using Azure.Data.Tables;
using Edict.Azure.TableStorage;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Orleans.Serialization;
using Sample.Api.Orders;
using Sample.Contracts.Orders.Projections;
using Sample.Contracts.Payments.Projections;
using Sample.Silo.Orders;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Host.UseOrleansClient(client =>
    {
        client.UseLocalhostClustering();
        client.Services.AddSerializer(ser =>
        {
            ser.AddAssembly(typeof(IOrderCommandHandler).Assembly);
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

    var tableConnectionString = builder.Configuration.GetConnectionString("tables")
                                ?? "UseDevelopmentStorage=true";
    var tableServiceClient = new TableServiceClient(tableConnectionString);
    builder.Services.AddSingleton(tableServiceClient);
    builder.Services.AddSingleton<IEdictTableRepository<OrderStatusRow>>(
        _ => new AzureTableRepository<OrderStatusRow>(tableServiceClient, "ordersbystatus"));
    builder.Services.AddSingleton<IEdictTableRepository<OrderOutcomeRow>>(
        _ => new AzureTableRepository<OrderOutcomeRow>(tableServiceClient, "orderoutcome"));
}

builder.Services.AddEdict();

var app = builder.Build();
app.MapOrdersEndpoints();
app.Run();

namespace Sample.Api
{
    public partial class Program { }
}
