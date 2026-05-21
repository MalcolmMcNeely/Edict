using Azure.Data.Tables;

using Edict.Azure.TableStorage;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Telemetry;

using OpenTelemetry;
using OpenTelemetry.Trace;

using Orleans.Serialization;

using Sample.Contracts.Fulfillment.Projections;
using Sample.Contracts.Orders.Projections;
using Sample.Contracts.Payments.Projections;
using Sample.Silo.Orders.CommandHandlers;
using Sample.Web.Components;
using Sample.Web.State;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSingleton<IEdictTableRepository<LineItemFulfillmentRow>>(
    _ => new AzureTableRepository<LineItemFulfillmentRow>(tableServiceClient, "lineitemfulfillment"));

// Forensic dead-letter projection lives on the same table the silo writes via
// AddEdictAzurePersistence; the Home page reads it through IEdictDeadLetterRepository
// which AddEdict() wires from this table repository.
builder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(
    _ => new AzureTableRepository<EdictDeadLetterEntry>(
        tableServiceClient, "edict-dead-letter"));

builder.Services.AddEdict();

builder.Services.AddSingleton<CurrentOrderTracker>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

namespace Sample.Web
{
    public partial class Program { }
}
