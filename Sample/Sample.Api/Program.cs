using Edict.Core.Diagnostics;
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
}

builder.Services.AddEdict();

var app = builder.Build();
app.MapOrdersEndpoints();
app.Run();

public partial class Program { }
