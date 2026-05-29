using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.Serialization;
using Edict.Postgres.TableStorage;
using Edict.Telemetry;

using Npgsql;

using Orleans.Serialization;

using Sample.Contracts.Fulfillment.Projections;
using Sample.Contracts.Orders.Projections;
using Sample.Contracts.Payments.Projections;
using Sample.Domain.Orders.CommandHandlers;
using Sample.ServiceDefaults;
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

var postgresConnectionString = builder.Configuration.GetConnectionString("appdb")
    ?? throw new InvalidOperationException(
        "Postgres connection string 'appdb' missing. Run via Sample.KafkaPostgres.AppHost.");

// The Web process holds its own NpgsqlDataSource for the read-side
// projection repositories. The silo's tuned DataSource lives in the silo
// process and is unreachable from here; default pool sizing is fine
// because the Web read path is not the throughput-sensitive surface.
builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(postgresConnectionString).Build());

builder.Services.AddSingleton<IEdictTableRepository<OrderStatusRow>>(serviceProvider =>
    new PostgresTableRepository<OrderStatusRow>(
        serviceProvider.GetRequiredService<NpgsqlDataSource>(), "ordersbystatus", serviceProvider.GetRequiredService<Serializer>()));
builder.Services.AddSingleton<IEdictTableRepository<OrderOutcomeRow>>(serviceProvider =>
    new PostgresTableRepository<OrderOutcomeRow>(
        serviceProvider.GetRequiredService<NpgsqlDataSource>(), "orderoutcome", serviceProvider.GetRequiredService<Serializer>()));
builder.Services.AddSingleton<IEdictTableRepository<LineItemFulfillmentRow>>(serviceProvider =>
    new PostgresTableRepository<LineItemFulfillmentRow>(
        serviceProvider.GetRequiredService<NpgsqlDataSource>(), "lineitemfulfillment", serviceProvider.GetRequiredService<Serializer>()));

// The framework projection writes to the literal table named by
// EdictDeadLetterProjectionBuilder.DeadLetterPartition; AddEdictPostgresPersistence's
// DeadLetterTableName option configures the operator-facing repository facade but
// not the projection itself, so the consumer-side read must target the literal
// table to see what the projection actually wrote.
builder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(serviceProvider =>
    new PostgresTableRepository<EdictDeadLetterEntry>(
        serviceProvider.GetRequiredService<NpgsqlDataSource>(),
        EdictDeadLetterProjectionBuilder.DeadLetterPartition,
        serviceProvider.GetRequiredService<Serializer>()));

builder.Services.AddEdict();

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

app.Run();
