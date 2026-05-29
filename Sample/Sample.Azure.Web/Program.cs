using Azure.Data.Tables;

using Edict.Azure.TableStorage;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.Serialization;
using Edict.Telemetry;

using Orleans.Serialization;

using Sample.Contracts.Fulfillment.Projections;
using Sample.Contracts.Orders.Projections;
using Sample.Contracts.Payments.Projections;
using Sample.ServiceDefaults;
using Sample.Domain.Orders.CommandHandlers;
using Sample.Web.Components;
using Sample.Web.Components.Simulator;
using Sample.Web.Components.State;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(EdictDiagnostics.SourceName))
    .WithTracing(tracing => tracing.AddSource(EdictDiagnostics.SourceName));

builder.UseOrleansClient(client =>
{
    client.UseLocalhostClustering();
    client.Services.AddSerializer(ser =>
    {
        ser.AddAssembly(typeof(IOrderCommandHandler).Assembly);
        ser.AddAssembly(typeof(IEdictCommandHandler).Assembly);
        ser.AddEdictContractSerializer();
    });
});

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

// The framework projection writes to the literal table named by
// EdictDeadLetterProjectionBuilder.DeadLetterPartition; AddEdictAzurePersistence's
// DeadLetterTableName option configures the operator-facing repository facade but
// not the projection itself, so the consumer-side read must target the literal
// table to see what the projection actually wrote.
builder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(
    _ => new AzureTableRepository<EdictDeadLetterEntry>(
        tableServiceClient, EdictDeadLetterProjectionBuilder.DeadLetterPartition));

builder.Services.AddEdict();

builder.Services.AddSingleton<CurrentOrderTracker>();
builder.Services.AddSingleton<KnownOrdersRegistry>();
builder.Services.AddSingleton<IDeterministicOrderPlacer, FireOneOrderHelper>();
builder.Services.AddSingleton<OrderSimulatorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OrderSimulatorService>());

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
