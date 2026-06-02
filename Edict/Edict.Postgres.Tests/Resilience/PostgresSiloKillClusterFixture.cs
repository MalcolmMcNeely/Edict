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
using Edict.Core.Serialization;
using Edict.Postgres.TableStorage;
using Edict.Tests.Conformance;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

using Npgsql;

using Orleans;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Postgres.Tests.Resilience;

// Owns its own cluster because KillSiloAsync permanently mutates membership.
// Postgres is not the fault point here (the silo is), so this reuses the shared
// PostgresAssemblyHost with its own per-fixture database — non-corrupting and
// avoiding a third container. The streams half runs over the shared Azurite; a
// 5s queue visibility timeout makes the mid-handle kill produce an observable
// redelivery within the test budget.
public sealed class PostgresSiloKillClusterFixture : IAsyncLifetime
{
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

    public NpgsqlDataSource PostgresDataSource => _dataSource;

    public string DeadLetterTableName { get; } = "edict_dead_letter";

    public string ClaimCheckTableName { get; } = "edict_claim_check";

    public async Task InitializeAsync()
    {
        var adminConnectionString = await PostgresAssemblyHost.GetAdminConnectionStringAsync();
        var databaseName = $"edict_{Guid.NewGuid():N}";
        _databaseConnectionString = await PostgresDatabaseFactory.CreateDatabaseAsync(adminConnectionString, databaseName);
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
            databaseName);
        _contextKey = PostgresClusterContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
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
    }

    // Lets a test target the kill at the silo that actually owns the in-flight
    // activation, captured by the slow projection on Handle entry.
    public SiloHandle FindSiloByAddress(SiloAddress address)
    {
        if (Cluster.Primary is { } primary && primary.SiloAddress.Equals(address))
        {
            return primary;
        }
        var match = Cluster.SecondarySilos.FirstOrDefault(
            s => s.SiloAddress.Equals(address));
        return match ?? throw new InvalidOperationException(
            $"No SiloHandle in the cluster matches address {address}.");
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(ConformanceFixture).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddAssembly(typeof(PostgresSiloKillProjectionEvent).Assembly)
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
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.AddEdict();
            siloBuilder.AddEdictAzureStreams(o =>
            {
                o.QueueServiceClient = ctx.QueueServiceClient;
                o.QueuePollingPeriod = TimeSpan.FromMilliseconds(200);
            });
            // The mid-handle kill relies on the queue message redelivering while
            // the slow projection is still blocking, so the visibility timeout
            // must sit below the handler's block. AddEdictAzureStreams does not
            // surface it, so set it on the underlying named AzureQueueOptions.
            siloBuilder.Services.Configure<AzureQueueOptions>("edict", queueOptions =>
                queueOptions.MessageVisibilityTimeout = TimeSpan.FromSeconds(5));
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
public sealed class PostgresSiloKillCollection : ICollectionFixture<PostgresSiloKillClusterFixture>
{
    public const string Name = "PostgresSiloKill";
}
