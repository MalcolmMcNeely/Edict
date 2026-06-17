using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Core.Tenancy;
using Edict.Postgres;
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

// Projection reads route through the grain via the two reader species:
// IEdictListProjectionReader<TListProjection> for external-store List projections
// and IEdictProjectionReader<TProjection> for in-grain State projections (the
// Delivery panel reads its DeliveryStatus this way). AddEdict() registers both
// open-generic plus the dead-letter forensic facade, so the read tier needs no
// Postgres wiring of its own.
builder.Services.AddEdict();

// The silo captures the audit log into the Postgres WORM store; the web reads it
// back straight from the same database, the way a regulator's audit console is a
// read-only process over the store rather than a grain call. AddEdictPostgresAuditReader
// registers IEdictAuditRepository over the appdb the AppHost injects.
var auditConnectionString = builder.Configuration.GetConnectionString("appdb")
    ?? throw new InvalidOperationException(
        "Postgres connection string 'appdb' missing. Run via Sample.KafkaPostgres.AppHost.");
builder.Services.AddEdictPostgresAuditReader(o => o.ConnectionString = auditConnectionString);

// Hosts the in-memory notifications sink the silo POSTs to over HTTP and the
// Orders view reads back in-process.
builder.Services.AddNotificationsSink();

// The B2B Employees pages act as a signed-in company. TenantSwitcher stubs the
// signed-tenant seam an identity provider would supply; AddEdictTenant yields its
// Current value as the origin tenant, so a tenant-scoped send folds the company into
// its routed key and an ambient-scoped read answers only that company's own partition.
// A real app maps the user's tenant claim here, never a value off the request body.
builder.Services.AddSingleton<TenantSwitcher>();
builder.Services.AddSingleton<EmployeeDirectorySeeder>();
builder.Services.AddEdictTenant(serviceProvider => serviceProvider.GetRequiredService<TenantSwitcher>().Current);

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
