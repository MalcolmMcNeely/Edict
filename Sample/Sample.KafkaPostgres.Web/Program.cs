using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Telemetry;

using Orleans.Serialization;

using Sample.Domain.Orders.CommandHandlers;
using Sample.ServiceDefaults;
using Sample.Web.Components;
using Sample.Web.Components.Notifications;
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

// Projection reads route through the grain via IEdictListProjectionReader<TListProjection>;
// AddEdict() registers it open-generic plus the dead-letter forensic facade, so
// the read tier needs no Postgres wiring of its own.
builder.Services.AddEdict();

// Hosts the in-memory notifications sink the silo POSTs to over HTTP and the
// Orders view reads back in-process.
builder.Services.AddNotificationsSink();

builder.Services.AddSingleton<CurrentOrderTracker>();
builder.Services.AddSingleton<KnownOrdersRegistry>();
builder.Services.AddSingleton<IDeterministicOrderPlacer, FireOneOrderHelper>();
builder.Services.AddSingleton<OrderSimulatorService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<OrderSimulatorService>());

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapNotificationsSink();

app.Run();
