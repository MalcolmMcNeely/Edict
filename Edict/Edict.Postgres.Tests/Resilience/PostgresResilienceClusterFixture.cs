using System.Net;
using System.Net.Sockets;

using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure.Streaming;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Postgres.TableStorage;
using Edict.Tests.Conformance;
using Edict.Tests.Conformance.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

using Npgsql;

using Orleans;
using Orleans.Serialization;
using Orleans.TestingHost;

using Testcontainers.PostgreSql;

namespace Edict.Postgres.Tests.Resilience;

// Owns its own Postgres container instead of sharing PostgresAssemblyHost:
// faulting it mid-test would break every parallel conformance collection
// running against the shared container. Azurite (the AQS streams half) stays on
// the shared assembly host — it is the dependency, not the fault point.
//
// The outage is a container stop, not a Docker pause. Pause freezes an
// in-flight write inside the backend and replays it on resume, so a write the
// client already saw time out still commits — an ambiguous outcome that would
// mask the dirty-activation-drop the write-fault scenario asserts. Stopping the
// backend rolls the uncommitted statement back, which is the genuine
// connection-drop shape. The host port is pinned with a fixed binding so
// stop/start reuses it and Edict's own reconnect path runs, rather than a
// host-port rebind. A short command timeout keeps the down window prompt.
//
// The ControllableOutboxExecutor is wired so the drain-recovery scenario can
// stage a durable pending entry; the fault under test is the real Postgres
// outage on the recovery drain's state write-back.
public sealed class PostgresResilienceClusterFixture : IAsyncLifetime
{
    PostgreSqlContainer _postgres = null!;
    string _databaseConnectionString = "";
    string _azuriteConnectionString = "";
    NpgsqlDataSource _dataSource = null!;
    TableServiceClient _tableServiceClient = null!;
    BlobServiceClient _blobServiceClient = null!;
    QueueServiceClient _queueServiceClient = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public string DeadLetterTableName { get; } = "edict_dead_letter";

    public string ClaimCheckTableName { get; } = "edict_claim_check";

    public async Task InitializeAsync()
    {
        var hostPort = GetFreeTcpPort();
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithPortBinding(hostPort, 5432)
            .Build();
        await _postgres.StartAsync();

        // A bounded command/connect timeout keeps a write against the stopped
        // container failing promptly instead of hanging on the OS socket timeout.
        _databaseConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            CommandTimeout = 5,
            Timeout = 5,
        }.ConnectionString;
        _dataSource = new NpgsqlDataSourceBuilder(_databaseConnectionString).Build();

        _azuriteConnectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        _tableServiceClient = new TableServiceClient(_azuriteConnectionString);
        _blobServiceClient = new BlobServiceClient(_azuriteConnectionString);
        _queueServiceClient = new QueueServiceClient(_azuriteConnectionString);

        var context = new PostgresClusterContext(
            _databaseConnectionString,
            _azuriteConnectionString,
            _tableServiceClient,
            _blobServiceClient,
            _queueServiceClient,
            DeadLetterTableName,
            ClaimCheckTableName,
            $"resilience-{Guid.NewGuid():N}");
        _contextKey = PostgresClusterContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Properties[PostgresClusterContextRegistry.ContextKeyProperty] = _contextKey;
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (Cluster is not null)
        {
            await Cluster.DisposeAsync();
        }
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }
        PostgresClusterContextRegistry.Unregister(_contextKey);
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    // The fixed port binding survives stop/start, so the restarted backend is
    // reachable on the same host port and Edict reconnects through its existing
    // data source rather than seeing a rebind.
    public async Task StopPostgresAsync() => await _postgres.StopAsync();

    public async Task StartPostgresAsync() => await _postgres.StartAsync();

    // Tests call this on entry so the fixture starts from a known-good baseline
    // even if a previous test panicked mid outage.
    public async Task EnsureRunningAsync()
    {
        if (_postgres.State != DotNet.Testcontainers.Containers.TestcontainersStates.Running)
        {
            await _postgres.StartAsync();
        }
    }

    static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(CounterAggregate).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(Orleans.Hosting.ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[PostgresClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing.");
            var ctx = PostgresClusterContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddSingleton(ctx.TableServiceClient);
            siloBuilder.Services.AddSingleton(ctx.BlobServiceClient);
            siloBuilder.Services.AddSingleton(ctx.QueueServiceClient);
            siloBuilder.Services.AddSingleton(TimeProvider.System);
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.AddEdict(o =>
            {
                o.OutboxMaxAttempts = 5;
                o.OutboxBaseDelay = TimeSpan.FromMilliseconds(200);
                o.OutboxJitterFraction = 0;
            });
            // Replace, not append — see ControllableExecutor recurring trap.
            var publish = siloBuilder.Services.Single(d =>
                d.ServiceType == typeof(IOutboxEffectExecutor)
                && d.ImplementationType == typeof(PublishEventExecutor));
            siloBuilder.Services.Remove(publish);
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, ControllableOutboxExecutor>();
            siloBuilder.AddEdictAzureStreams(o =>
            {
                o.QueueServiceClient = ctx.QueueServiceClient;
                o.QueuePollingPeriod = TimeSpan.FromMilliseconds(200);
            });
            siloBuilder.AddEdictPostgresPersistence(o =>
            {
                o.ConnectionString = ctx.PostgresConnectionString;
                o.DeadLetterTableName = ctx.DeadLetterTableName;
                o.ClaimCheckTableName = ctx.ClaimCheckTableName;
            });
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(
            IConfiguration configuration,
            IClientBuilder clientBuilder)
        {
            var key = configuration[PostgresClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing.");
            var ctx = PostgresClusterContextRegistry.Get(key);

            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
            clientBuilder.Services.AddSingleton(
                new NpgsqlDataSourceBuilder(ctx.PostgresConnectionString).Build());
            clientBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(serviceProvider =>
                new PostgresTableRepository<EdictDeadLetterEntry>(
                    serviceProvider.GetRequiredService<NpgsqlDataSource>(),
                    ctx.DeadLetterTableName,
                    serviceProvider.GetRequiredService<Serializer>()));
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresResilienceCollection : ICollectionFixture<PostgresResilienceClusterFixture>
{
    public const string Name = "PostgresResilience";
}
