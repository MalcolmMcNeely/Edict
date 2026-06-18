using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

using Edict.Contracts.Configuration;
using Edict.Contracts.Sending;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Postgres;
using Edict.Tests.Conformance.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

using Npgsql;

using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

using Testcontainers.PostgreSql;

using Xunit;

namespace Edict.Postgres.Tests.Resilience;

// The persistence-axis resilience fixture: real Postgres is the fault point, the
// stream is the dumb deliver-once MemoryStreams reference. Owns its own Postgres
// container instead of sharing the assembly host — faulting it mid-test would
// break every parallel collection.
//
// The outage is a container stop, not a Docker pause. Pause freezes an in-flight
// write inside the backend and replays it on resume, so a write the client already
// saw time out still commits — an ambiguous outcome that would mask the
// dirty-activation-drop the write-fault scenario asserts. Stopping the backend
// rolls the uncommitted statement back, the genuine connection-drop shape. The
// host port is pinned with a fixed binding so stop/start reuses it and Edict's own
// reconnect path runs. A short command timeout keeps the down window prompt.
//
// The ControllableOutboxExecutor is wired so the drain-recovery scenario can stage
// a durable pending entry; the fault under test is the real Postgres outage on the
// recovery drain's state write-back.
public sealed class PostgresResilienceClusterFixture : IAsyncLifetime
{
    PostgreSqlContainer _postgres = null!;
    NpgsqlDataSource _dataSource = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    // The fault switch the drain-recovery scenario flips to stage a durable
    // pending entry. Fixture-owned, wired into this cluster's controllable
    // executor.
    public OutboxFaultState OutboxFault { get; } = new();

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public async Task InitializeAsync()
    {
        var hostPort = GetFreeTcpPort();
        _postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithPortBinding(hostPort, 5432)
            .Build();
        await _postgres.StartAsync();

        // A bounded command/connect timeout keeps a write against the stopped
        // container failing promptly instead of hanging on the OS socket timeout.
        var connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            CommandTimeout = 5,
            Timeout = 5,
        }.ConnectionString;
        _dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();

        var context = new PostgresResilienceContext(connectionString, OutboxFault);
        _contextKey = PostgresResilienceContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Properties[PostgresResilienceContextRegistry.ContextKeyProperty] = _contextKey;
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
        PostgresResilienceContextRegistry.Unregister(_contextKey);
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
        public void Configure(ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[PostgresResilienceContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from silo configuration.");
            var ctx = PostgresResilienceContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Configure<SiloMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromMinutes(2));
            siloBuilder.Services.AddSingleton(TimeProvider.System);
            // MemoryStreams registers no Edict streams provider, so the marker the
            // startup validator inspects is supplied here.
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.AddEdict(o =>
            {
                o.OutboxMaxAttempts = 5;
                o.OutboxBaseDelay = TimeSpan.FromMilliseconds(200);
                o.OutboxJitterFraction = 0;
            });
            ControllableOutboxExecutor.Replace(siloBuilder.Services, ctx.OutboxFault);
            // The dumb deliver-once reference stream.
            siloBuilder.AddMemoryStreams("edict");
            siloBuilder.AddEdictPostgresPersistence(o =>
            {
                o.ConnectionString = ctx.ConnectionString;
            });
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
        }
    }
}

sealed record PostgresResilienceContext(string ConnectionString, OutboxFaultState OutboxFault);

static class PostgresResilienceContextRegistry
{
    public const string ContextKeyProperty = "PostgresResilienceContextKey";

    static readonly ConcurrentDictionary<string, PostgresResilienceContext> _contexts = new();

    public static string Register(PostgresResilienceContext context)
    {
        var key = Guid.NewGuid().ToString("N");
        _contexts[key] = context;
        return key;
    }

    public static PostgresResilienceContext Get(string key) => _contexts[key];

    public static void Unregister(string key) => _contexts.TryRemove(key, out _);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresResilienceCollection : ICollectionFixture<PostgresResilienceClusterFixture>
{
    public const string Name = "PostgresResilience";
}
